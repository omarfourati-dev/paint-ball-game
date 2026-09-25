using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paintball.Core.Maps;
using Paintball.Net.Accounts;
using Paintball.Net.Protocol;
using Paintball.Net.Rooms;
using Paintball.Net.Simulation;

namespace Paintball.Server
{
    public sealed class ServerHostOptions
    {
        /// <summary>HTTPS/WSS-Port (0 = zufällig, für Tests).</summary>
        public int HttpsPort = 5443;
        /// <summary>HTTP-Port nur für die Weiterleitung auf HTTPS (-1 = aus, 0 = zufällig).</summary>
        public int HttpPort = 5080;
        public bool ListenAnyIp;
        /// <summary>
        /// Betrieb hinter einem TLS-terminierenden Reverse-Proxy (z. B. Caddy): nur HTTP auf <see cref="HttpPort"/>,
        /// X-Forwarded-Proto/-For werden übernommen, Weiterleitung auf die öffentliche HTTPS-URL ohne Port.
        /// </summary>
        public bool BehindProxy;
        public string WebRoot;
        /// <summary>Zusätzlich erlaubte WebSocket-Origins (gleicher Host ist immer erlaubt).</summary>
        public List<string> AllowedOrigins = new();
        public ServerOptions Game = new();
        public bool RunGameLoop = true;
        /// <summary>Aktiviert /api/auth/dev (nur Tests und lokale Entwicklung).</summary>
        public bool DevLogin;
        /// <summary>Postgres-URL; leer = In-Memory (Warnung im Log).</summary>
        public string DatabaseUrl;
        /// <summary>Öffentliche Basis-URL für OAuth-Redirects, z. B. https://paint-ball-game.omarfourati.de.</summary>
        public string PublicUrl;
        public string GoogleClientId;
        public string GoogleClientSecret;
        /// <summary>Überschreibbar für Tests (Task 6).</summary>
        public IGoogleOAuthClient Google;
        /// <summary>Google AdSense; null = aus (Program.cs liest die Umgebung).</summary>
        public AdsConfig Ads;
        /// <summary>Nur für Tests: Vorrang vor DatabaseUrl.</summary>
        public IPlayerRepository Repository;
        /// <summary>Spielstände über die Hintergrund-Warteschlange schreiben (Produktion); false = synchron (Tests).</summary>
        public bool BackgroundPersistence;
        /// <summary>Nur für Tests: eigene Wiederholungs-Wartezeiten der Hintergrund-Warteschlange (Standard: 500/2000/5000 ms).</summary>
        internal int[] RetryDelaysMs;
    }

    /// <summary>
    /// WSS-Gameserver-Host (NFR-11 TLS, AR-07, NFR-06/20 Health & Metriken, P-06 Web-Client).
    /// </summary>
    public static class ServerHost
    {
        public const string Version = "1.0.0-mvp";
        public const int ProtocolVersion = 1;
        /// <summary>
        /// Stopp-Budget des Hosts. Schlimmster Fall 25 s Host + 5 s Warten auf die Takt-Schleife (<see cref="GameLoopService.LoopExitWait"/>)
        /// + 10 s Leeren der Warteschlange (<see cref="PersistenceQueue.DisposeFlushTimeout"/>), plus darin schon enthalten ein noch
        /// laufender Datenbank-Schreibversuch (bis zu dessen Befehls-Zeitlimit, <see cref="Paintball.Net.Accounts.PostgresPlayerRepository.DefaultCommandTimeoutSeconds"/>,
        /// Standard 5 s) = 40 s, passt unter die 45 s von <c>docker stop -t 45</c> bzw. <c>stop_grace_period</c>; offene WebSockets
        /// werden beim Stoppen sofort geschlossen und blockieren es nicht.
        /// </summary>
        public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(25);
        /// <summary>So lange darf ein Client nach dem 1001-Close noch antworten, dann wird die Verbindung abgebrochen.</summary>
        public static readonly TimeSpan RestartCloseGrace = TimeSpan.FromSeconds(3);

        public static WebApplication Build(string[] args, ServerHostOptions options)
        {
            string webRoot = options.WebRoot ?? FindWebRoot();
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                WebRootPath = webRoot,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("Paintball", LogLevel.Information);
            builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = ShutdownTimeout);

            builder.WebHost.ConfigureKestrel(k =>
            {
                k.AddServerHeader = false;
                IPAddress ip = options.ListenAnyIp ? IPAddress.Any : IPAddress.Loopback;
                // TLS: Zertifikat aus Kestrel-Konfiguration oder ASP.NET-Entwicklerzertifikat (dotnet dev-certs https)
                if (options.BehindProxy)
                {
                    k.Listen(ip, options.HttpPort);
                    return;
                }
                k.Listen(ip, options.HttpsPort, o => o.UseHttps());
                if (options.HttpPort >= 0) k.Listen(ip, options.HttpPort);
            });

            builder.Services.AddResponseCompression(o =>
            {
                o.EnableForHttps = true;
                o.Providers.Add<BrotliCompressionProvider>();
                o.Providers.Add<GzipCompressionProvider>();
                o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/javascript", "text/css", "image/svg+xml", "model/gltf+json" });
            });
            builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);

            IPlayerRepository repo;
            if (options.Repository != null)
            {
                repo = options.Repository;
            }
            else if (string.IsNullOrWhiteSpace(options.DatabaseUrl))
            {
                Console.Error.WriteLine("[DB] DATABASE_URL nicht gesetzt – Konten nur im Arbeitsspeicher (gehen beim Neustart verloren)");
                repo = new InMemoryPlayerRepository();
            }
            else
            {
                var pg = new PostgresPlayerRepository(options.DatabaseUrl);
                pg.EnsureSchema();
                repo = pg;
            }
            PersistenceQueue queue = options.BackgroundPersistence
                ? (options.RetryDelaysMs != null ? PersistenceQueue.Background(repo, options.RetryDelaysMs) : PersistenceQueue.Background(repo))
                : PersistenceQueue.Inline(repo);
            var accounts = new AccountStore(repo, null, queue);
            var game = new GameServer(options.Game, accounts);
            builder.Services.AddSingleton(game);
            // Hosted Services stoppen in umgekehrter Registrierungsreihenfolge: Der Dienst, der die Warteschlange leert, wird
            // ZUERST registriert, damit er ZULETZT stoppt – nach dem Spieltakt, der bis dahin noch Matchergebnisse einreiht.
            builder.Services.AddHostedService(_ => new PersistenceDrainService(queue));
            if (options.RunGameLoop)
            {
                builder.Services.AddHostedService(_ => new GameLoopService(game));
                builder.Services.AddHostedService(_ => new MaintenanceService(accounts, game));
            }

            WebApplication app = builder.Build();

            if (options.BehindProxy)
            {
                // Der Container ist nur im internen Docker-Netz des Proxys erreichbar – daher jedem Proxy vertrauen.
                var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
                forwarded.KnownIPNetworks.Clear();
                forwarded.KnownProxies.Clear();
                app.UseForwardedHeaders(forwarded);
            }

            AdsConfig ads = options.Ads ?? AdsConfig.Disabled;
            string csp = ads.ContentSecurityPolicy();
            app.Use(async (ctx, next) =>
            {
                // HTTP → HTTPS (NFR-11); hinter dem Proxy bleiben /api/health (Container-Healthcheck) und /metrics (Prometheus
                // im Docker-Netz) ohne Umleitung erreichbar
                bool internalProbe = options.BehindProxy && (ctx.Request.Path == "/api/health" || ctx.Request.Path == "/metrics");
                if (!ctx.Request.IsHttps && !internalProbe)
                {
                    string host = ctx.Request.Host.Host;
                    string authority = options.BehindProxy ? host : $"{host}:{HttpsPortOf(app, options)}";
                    ctx.Response.Redirect($"https://{authority}{ctx.Request.Path}{ctx.Request.QueryString}", permanent: false);
                    return;
                }
                IHeaderDictionary h = ctx.Response.Headers;
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "DENY";
                h["Referrer-Policy"] = ads.ReferrerPolicy;
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                h["Strict-Transport-Security"] = "max-age=31536000";
                h["Content-Security-Policy"] = csp; // Google-Domains nur bei aktiver Werbung
                await next();
            });

            app.UseResponseCompression();
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

            app.Map("/ws", async ctx =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                if (!OriginAllowed(ctx, options)) { ctx.Response.StatusCode = 403; return; }
                string playerId = accounts.PlayerIdForSession(AuthApi.SessionToken(ctx));
                if (playerId == null) { ctx.Response.StatusCode = 401; return; }
                if (accounts.NeedsName(playerId)) { ctx.Response.StatusCode = 403; return; }
                using WebSocket socket = await ctx.WebSockets.AcceptWebSocketAsync();
                var connection = new WsConnection(socket, game.Options.MaxMessageBytes);
                // Herunterfahren (Deploy): Verbindung sofort mit 1001 "server_restart" schließen, damit Kestrel nicht bis zum
                // Stopp-Budget auf offene Sockets wartet und die Warteschlange rechtzeitig geleert wird. Der Client verbindet neu.
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                using CancellationTokenRegistration onStopping = app.Lifetime.ApplicationStopping.Register(() =>
                {
                    connection.Close("server_restart", WebSocketCloseStatus.EndpointUnavailable);
                    try { stop.CancelAfter(RestartCloseGrace); } catch (ObjectDisposedException) { }
                });
                await connection.RunAsync(game, ctx.Connection.RemoteIpAddress?.ToString(), playerId, stop.Token);
            });

            MapApi(app, game, accounts);
            MapAds(app, ads);
            MapMetrics(app, game, accounts, options);
            var authLimiter = new RateLimiter(limit: 20, window: TimeSpan.FromMinutes(1)); // gemeinsam für alle Auth-Routen
            AuthApi.Map(app, game, accounts, options, authLimiter);
            GoogleAuthApi.Map(app, accounts, options, authLimiter);

            if (Directory.Exists(webRoot))
            {
                app.Use(RoutePages);
                app.UseDefaultFiles();
                var types = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
                types.Mappings[".hdr"] = "image/vnd.radiance";
                types.Mappings[".gltf"] = "model/gltf+json";   // echte Menschen und Props (CC0)
                types.Mappings[".bin"] = "application/octet-stream";
                types.Mappings[".webmanifest"] = "application/manifest+json";
                // robots.txt, llms.txt (Umlaute) und sitemap.xml: ohne charset raten Browser und Crawler sonst Latin-1
                types.Mappings[".txt"] = "text/plain; charset=utf-8";
                types.Mappings[".xml"] = "application/xml; charset=utf-8";
                app.UseStaticFiles(new StaticFileOptions
                {
                    ContentTypeProvider = types,
                    OnPrepareResponse = c => c.Context.Response.Headers["Cache-Control"] =
                        c.File.PhysicalPath != null && c.File.PhysicalPath.Contains("assets") ? "public, max-age=604800" : "no-cache"
                });
            }
            return app;
        }

        /// <summary>Seiten ohne Dateiendung: Spiel und Rechtstexte (Landingpage ist index.html unter /).</summary>
        private static readonly Dictionary<string, string> Pages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/play"] = "/play.html",
            ["/impressum"] = "/impressum.html",
            ["/datenschutz"] = "/datenschutz.html"
        };

        /// <summary>Alte Einladungslinks /?join= → /play, /play/ → /play, saubere URLs → HTML-Datei.</summary>
        private static Task RoutePages(HttpContext ctx, Func<Task> next)
        {
            string path = ctx.Request.Path.Value ?? "/";
            QueryString query = ctx.Request.QueryString;
            if (path == "/" && ctx.Request.Query.ContainsKey("join"))
            {
                ctx.Response.Redirect("/play" + query);
                return Task.CompletedTask;
            }
            if (path.Length > 1 && path.EndsWith('/') && Pages.ContainsKey(path.TrimEnd('/')))
            {
                ctx.Response.Redirect(path.TrimEnd('/') + query, permanent: true);
                return Task.CompletedTask;
            }
            if (Pages.TryGetValue(path, out string file)) ctx.Request.Path = file;
            return next();
        }

        private static int HttpsPortOf(WebApplication app, ServerHostOptions options)
        {
            if (options.HttpsPort != 0) return options.HttpsPort;
            IServerAddressesFeature f = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            string https = f?.Addresses.FirstOrDefault(a => a.StartsWith("https", StringComparison.Ordinal));
            return https != null ? new Uri(https).Port : 443;
        }

        /// <summary>WebSocket-Origin-Prüfung gegen Cross-Site-WebSocket-Hijacking.</summary>
        public static bool OriginAllowed(HttpContext ctx, ServerHostOptions options)
        {
            string origin = ctx.Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin)) return true; // Nicht-Browser-Clients (Unity, Tests)
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri uri)) return false;
            if (string.Equals(uri.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase)) return true;
            return options.AllowedOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Werbe-Konfiguration für den Client und ads.txt (404, solange keine Publisher-ID gesetzt ist).</summary>
        private static void MapAds(WebApplication app, AdsConfig ads)
        {
            app.MapGet("/api/ads", (HttpContext ctx) =>
            {
                ctx.Response.Headers.CacheControl = "no-cache";
                if (!ads.Enabled) return Results.Json(new { enabled = false });
                return Results.Json(new { enabled = true, client = ads.Client, slots = ads.Slots, interstitialEvery = ads.InterstitialEvery });
            });
            app.MapGet("/ads.txt", (HttpContext ctx) =>
            {
                if (!ads.Enabled) return Results.NotFound();
                ctx.Response.Headers.CacheControl = "public, max-age=3600";
                return Results.Text(ads.AdsTxt, "text/plain; charset=utf-8");
            });
        }

        /// <summary>Prometheus-Textformat 0.0.4, von Hand geschrieben (keine zusätzliche Abhängigkeit).</summary>
        public const string MetricsContentType = "text/plain; version=0.0.4; charset=utf-8";

        /// <summary>
        /// /metrics für Prometheus im Docker-Netz <c>web</c>. Nicht öffentlich: Caddy antwortet dafür mit 404, und hinter dem
        /// Proxy lehnt der Server zusätzlich jede Anfrage ab, die über den Proxy kam (X-Forwarded-Proto https).
        /// </summary>
        private static void MapMetrics(WebApplication app, GameServer game, AccountStore accounts, ServerHostOptions options)
        {
            app.MapGet("/metrics", (HttpContext ctx) =>
            {
                if (options.BehindProxy && ctx.Request.IsHttps) return Results.NotFound();
                ctx.Response.Headers.CacheControl = "no-store";
                return Results.Text(MetricsText(game, accounts), MetricsContentType);
            });
        }

        internal static string MetricsText(GameServer game, AccountStore accounts)
        {
            ServerMetrics m = game.Metrics;
            var sb = new StringBuilder(2048);
            void Metric(string name, string type, string help, double value)
            {
                sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
                sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
                sb.Append(name).Append(' ').Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            }
            Metric("paintball_sessions", "gauge", "Verbundene Sitzungen", game.SessionCount);
            Metric("paintball_rooms", "gauge", "Offene Räume", RetryOnConcurrentChange(() => game.Rooms.Count));
            Metric("paintball_matches_running", "gauge", "Räume mit laufendem Match",
                RetryOnConcurrentChange(() => game.Rooms.Count(r => r.State == RoomState.Match)));
            Metric("paintball_tick_ms", "gauge", "Dauer des letzten Spieltakts in Millisekunden", m.LastTickMs);
            Metric("paintball_tick_max_ms", "gauge", "Gleitendes Maximum der Spieltakt-Dauer in Millisekunden", m.MaxTickMs);
            Metric("paintball_persistence_pending", "gauge", "Offene Schreibaufträge", accounts.Queue.Pending);
            Metric("paintball_persistence_failures_total", "counter", "Endgültig gescheiterte Schreibaufträge", accounts.Queue.Failures);
            Metric("paintball_logins_total", "counter", "Anmeldungen am Spielserver", Interlocked.Read(ref m.Logins));
            Metric("paintball_matches_started_total", "counter", "Gestartete Matches", Interlocked.Read(ref m.MatchesStarted));
            Metric("paintball_matches_finished_total", "counter", "Beendete Matches", Interlocked.Read(ref m.MatchesFinished));
            Metric("paintball_reconnects_total", "counter", "Wiederverbindungen", Interlocked.Read(ref m.Reconnects));
            Metric("paintball_messages_in_total", "counter", "Empfangene Nachrichten", Interlocked.Read(ref m.MessagesIn));
            Metric("paintball_messages_rejected_total", "counter", "Abgelehnte Nachrichten", Interlocked.Read(ref m.MessagesRejected));
            Metric("paintball_afk_kicks_total", "counter", "Wegen Inaktivität entfernte Spieler", Interlocked.Read(ref m.AfkKicks));
            Metric("paintball_flood_kicks_total", "counter", "Wegen Nachrichtenflut entfernte Spieler", Interlocked.Read(ref m.FloodKicks));
            return sb.ToString();
        }

        /// <summary>
        /// Die Räume gehören dem Spieltakt-Thread (einfaches Dictionary). Wird beim Aufzählen gleichzeitig ein Raum angelegt oder
        /// entfernt, wirft die Aufzählung – dann einfach noch einmal versuchen; im Notfall NaN statt eines 500ers.
        /// </summary>
        private static double RetryOnConcurrentChange(Func<int> count)
        {
            for (int i = 0; i < 3; i++)
            {
                try { return count(); }
                catch (InvalidOperationException) { }
            }
            return double.NaN;
        }

        private static void MapApi(WebApplication app, GameServer game, AccountStore accounts)
        {
            DateTime startedAt = DateTime.UtcNow;

            app.MapGet("/api/health", () =>
            {
                try
                {
                    int count = accounts.Count; // fragt die Datenbank ab, wirft bei DB-Fehler
                    int pending = accounts.Queue.Pending;
                    long failures = accounts.Queue.Failures;
                    bool recentError = accounts.Queue.LastErrorAt is DateTime t && DateTime.UtcNow - t < TimeSpan.FromSeconds(60);
                    string status = pending > 1000 || recentError ? "degraded" : "ok";
                    return Results.Json(new
                    {
                        status,
                        version = Version,
                        protocol = ProtocolVersion,
                        uptimeSeconds = (int)(DateTime.UtcNow - startedAt).TotalSeconds,
                        sessions = game.SessionCount,
                        rooms = game.Rooms.Count,
                        matches = game.Rooms.Count(r => r.State == RoomState.Match),
                        accounts = count,
                        db = count >= 0 ? "ok" : "error",
                        tickMs = Math.Round(game.Metrics.LastTickMs, 3),
                        maxTickMs = Math.Round(game.Metrics.MaxTickMs, 3),
                        metrics = new
                        {
                            game.Metrics.MessagesIn, game.Metrics.MessagesRejected, game.Metrics.Logins,
                            game.Metrics.MatchesStarted, game.Metrics.MatchesFinished, game.Metrics.Reconnects,
                            game.Metrics.AfkKicks, game.Metrics.FloodKicks
                        },
                        // Der Container-Healthcheck (wget auf /api/health) bleibt bei degraded grün, weil HTTP 200 zurückkommt – gewollt:
                        // ein Neustart würde nichts an einer überlasteten/fehlerhaften Datenbank ändern, nur Verbindungen kappen.
                        persistence = new { pending, failures }
                    });
                }
                catch (Exception ex)
                {
                    // Nur den Typ loggen: Npgsql-Meldungen können Host/Benutzer enthalten.
                    Console.Error.WriteLine("[Health] Datenbank nicht erreichbar: " + ex.GetType().Name);
                    return Results.Json(new { status = "degraded", db = "error" }, statusCode: 503);
                }
            });

            app.MapGet("/api/maps", () => Results.Text(MapsJson(), "application/json"));
            app.MapGet("/api/config", () => Results.Json(new
            {
                protocol = ProtocolVersion,
                tickRate = GameMatch.TickRate,
                modes = new[] { "tdm", "ffa", "ctf", "elim", "koth", "training" },
                phrases = GameServer.QuickChatPhrases,
                movement = new
                {
                    walk = Movement.WalkSpeed, sprint = Movement.SprintSpeed, crouch = Movement.CrouchSpeed,
                    radius = Movement.Radius, gravity = Movement.Gravity, jump = Movement.JumpVelocity
                }
            }));

            app.MapGet("/api/leaderboard", (HttpRequest req) =>
            {
                int top = int.TryParse(req.Query["top"], out int t) ? Math.Clamp(t, 1, 100) : 50;
                return Results.Json(accounts.Leaderboard(top).Select(r => new { r.Rank, r.Name, r.Mmr, r.Level, r.League, r.Division }));
            });
            // DSGVO (NFR-12): Auskunft und Löschung über das Session-Cookie → AuthApi.
        }

        /// <summary>Kartengeometrie aus dem Core-MapCatalog – eine Quelle für Server und Client.</summary>
        public static string MapsJson()
        {
            var catalog = new MapCatalog();
            return Json.Write(w =>
            {
                w.WriteNumber("dynamicAmplitude", World.DynamicAmplitude);
                w.WriteNumber("dynamicPeriod", World.DynamicPeriodSeconds);
                w.WriteStartArray("maps");
                foreach (MapDefinition m in catalog.All)
                {
                    w.WriteStartObject();
                    w.WriteString("id", m.Id);
                    w.WriteString("name", m.DisplayName);
                    w.WriteString("description", m.Description);
                    w.WriteString("symmetry", m.Symmetry.ToString().ToLowerInvariant());
                    w.WriteNumber("sizeX", m.SizeX);
                    w.WriteNumber("sizeZ", m.SizeZ);
                    w.WriteNumber("maxPlayers", m.MaxPlayers);
                    w.WriteStartArray("covers");
                    foreach (MapCoverBlock c in m.Covers)
                    {
                        w.WriteStartArray();
                        w.WriteNumberValue(c.X); w.WriteNumberValue(c.Y); w.WriteNumberValue(c.Z);
                        w.WriteNumberValue(c.ScaleX); w.WriteNumberValue(c.ScaleY); w.WriteNumberValue(c.ScaleZ);
                        w.WriteNumberValue((c.IsDynamic ? 1 : 0) | (c.IsResupply ? 2 : 0));
                        w.WriteStringValue(c.Kind ?? string.Empty);
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                    w.WriteStartArray("spawns");
                    foreach (MapSpawnZone s in m.Spawns)
                    {
                        w.WriteStartArray();
                        w.WriteNumberValue(s.TeamId); w.WriteNumberValue(s.X); w.WriteNumberValue(s.Z);
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });
        }

        public static string FindWebRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "web");
                if (File.Exists(Path.Combine(candidate, "play.html"))) return candidate;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            return Path.Combine(AppContext.BaseDirectory, "web");
        }
    }

    /// <summary>Feste Tickrate (NFR-01): 30 Hz, driftfrei über Stopwatch.</summary>
    internal sealed class GameLoopService : BackgroundService
    {
        private readonly GameServer _game;
        public GameLoopService(GameServer game) { _game = game; }
        /// <summary>Höchstens so lange wartet StopAsync zusätzlich auf das Ende der Takt-Schleife (ein Takt dauert Millisekunden).</summary>
        private static readonly TimeSpan LoopExitWait = TimeSpan.FromSeconds(5);
        internal bool LoopExited => ExecuteTask?.IsCompleted == true;

        /// <summary>
        /// Kehrt erst zurück, wenn die Takt-Schleife beendet ist – auch wenn das Stopp-Budget des Hosts schon abgelaufen ist
        /// (BackgroundService.StopAsync kehrt dann sofort zurück). So überlappt das Leeren der Warteschlange nie den letzten Takt.
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try { await base.StopAsync(cancellationToken); }
            finally
            {
                Task loop = ExecuteTask;
                if (loop != null && !loop.IsCompleted && await Task.WhenAny(loop, Task.Delay(LoopExitWait)) != loop)
                    Console.Error.WriteLine("[GameLoop] Takt-Schleife nach dem Stopp nicht beendet");
            }
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.Factory.StartNew(() =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double next = 0;
                double step = 1000.0 / GameServer.TickRate;
                while (!stoppingToken.IsCancellationRequested)
                {
                    double now = clock.Elapsed.TotalMilliseconds;
                    if (now < next)
                    {
                        Thread.Sleep(Math.Max(0, (int)(next - now) - 1));
                        continue;
                    }
                    try { _game.Tick(); }
                    catch (Exception ex) { Console.Error.WriteLine("[GameLoop] " + ex); }
                    next += step;
                    if (now - next > 250) next = now; // Nach Hängern nicht nachholen
                }
            }, stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Leert beim Herunterfahren die <see cref="PersistenceQueue"/> (Budget 10 s, siehe <see cref="PersistenceQueue.DisposeAsync"/>).
    /// Muss als erster Hosted Service registriert sein, damit er als letzter stoppt.
    /// </summary>
    internal sealed class PersistenceDrainService : IHostedService
    {
        private readonly PersistenceQueue _queue;
        public PersistenceDrainService(PersistenceQueue queue) { _queue = queue; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            int pending = _queue.Pending;
            if (pending > 0) Console.WriteLine($"[Persistenz] Herunterfahren: {pending} offene Schreibaufträge werden geschrieben");
            await _queue.DisposeAsync();
        }
    }

    /// <summary>
    /// Wartungsdienst (Task 6): räumt abgelaufene Sessions auf (beim Start und danach stündlich) und verdrängt inaktive
    /// Konten aus dem <see cref="AccountStore"/>-Cache (alle 5 Minuten, <see cref="EvictIdle"/> ohne Zugriff, online und
    /// offene Schreibaufträge ausgenommen – siehe <see cref="AccountStore.Evict"/>).
    /// </summary>
    internal sealed class MaintenanceService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SessionCleanupInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan EvictIdle = TimeSpan.FromMinutes(30);

        private readonly AccountStore _accounts;
        private readonly GameServer _game;
        public MaintenanceService(AccountStore accounts, GameServer game) { _accounts = accounts; _game = game; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield(); // Start des Hosts nicht durch den ersten DB-Zugriff blockieren
            using var timer = new PeriodicTimer(Interval);
            DateTime nextSessionCleanup = DateTime.UtcNow;
            do
            {
                try { _accounts.Evict(_game.IsOnline, EvictIdle); }
                catch (Exception ex) { Console.Error.WriteLine("[Wartung] Speicherbereinigung fehlgeschlagen: " + ex.GetType().Name); }

                if (DateTime.UtcNow >= nextSessionCleanup)
                {
                    try { _accounts.CleanupSessions(); }
                    catch (Exception ex) { Console.Error.WriteLine("[Sessions] Aufräumen fehlgeschlagen: " + ex.GetType().Name); }
                    nextSessionCleanup = DateTime.UtcNow + SessionCleanupInterval;
                }
            }
            while (await WaitAsync(timer, stoppingToken));
        }

        private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
        {
            try { return await timer.WaitForNextTickAsync(ct); }
            catch (OperationCanceledException) { return false; }
        }
    }

    /// <summary>WebSocket-Verbindung ↔ GameServer-Session. Senden entkoppelt über begrenzten Kanal.</summary>
    internal sealed class WsConnection : IClientSink
    {
        private readonly WebSocket _socket;
        private readonly int _maxMessageBytes;
        private readonly Channel<string> _outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        private string _closeReason;
        private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.PolicyViolation;

        public WsConnection(WebSocket socket, int maxMessageBytes)
        {
            _socket = socket;
            _maxMessageBytes = maxMessageBytes;
        }

        public void Send(string json) => _outbox.Writer.TryWrite(json);

        public void Close(string reason) => Close(reason, WebSocketCloseStatus.PolicyViolation);

        /// <summary>Schließt nach dem Senden der ausstehenden Nachrichten; der erste Grund gewinnt.</summary>
        public void Close(string reason, WebSocketCloseStatus status)
        {
            lock (_outbox)
            {
                if (_closeReason != null) return;
                _closeStatus = status;
                _closeReason = reason;
            }
            _outbox.Writer.TryComplete();
        }

        public async Task RunAsync(GameServer game, string remote, string playerId, CancellationToken ct)
        {
            Session session = game.Connect(this, remote, playerId);
            Task writer = WriteLoop(ct);
            var buffer = new byte[4096];
            var message = new MemoryStream();
            try
            {
                while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await _socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (message.Length <= _maxMessageBytes) message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string text = message.Length > _maxMessageBytes
                            ? new string(' ', _maxMessageBytes + 1)
                            : Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                        game.Receive(session, text);
                    }
                    message.SetLength(0);
                }
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }
            finally
            {
                game.Disconnect(session);
                _outbox.Writer.TryComplete();
                try { await writer; } catch { }
                try
                {
                    if (_socket.State == WebSocketState.CloseReceived || _socket.State == WebSocketState.Open)
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch (WebSocketException) { }
            }
        }

        private async Task WriteLoop(CancellationToken ct)
        {
            try
            {
                await foreach (string json in _outbox.Reader.ReadAllAsync(ct))
                {
                    if (_socket.State != WebSocketState.Open) break;
                    await _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);
                }
                string reason;
                WebSocketCloseStatus status;
                lock (_outbox) { reason = _closeReason; status = _closeStatus; }
                if (reason != null && _socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(status, reason, ct);
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }
        }
    }
}
