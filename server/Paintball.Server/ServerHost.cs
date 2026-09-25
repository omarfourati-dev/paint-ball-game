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
        /// <summary>Nur für Tests: Vorrang vor DatabaseUrl.</summary>
        public IPlayerRepository Repository;
    }

    /// <summary>
    /// WSS-Gameserver-Host (NFR-11 TLS, AR-07, NFR-06/20 Health & Metriken, P-06 Web-Client).
    /// </summary>
    public static class ServerHost
    {
        public const string Version = "1.0.0-mvp";
        public const int ProtocolVersion = 1;

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
            var accounts = new AccountStore(repo);
            var game = new GameServer(options.Game, accounts);
            builder.Services.AddSingleton(game);
            if (options.RunGameLoop)
            {
                builder.Services.AddHostedService(_ => new GameLoopService(game));
                builder.Services.AddHostedService(_ => new SessionCleanupService(accounts));
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

            app.Use(async (ctx, next) =>
            {
                // HTTP → HTTPS (NFR-11); hinter dem Proxy bleibt /api/health für den Container-Healthcheck erreichbar
                bool internalProbe = options.BehindProxy && ctx.Request.Path == "/api/health";
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
                h["Referrer-Policy"] = "no-referrer";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                h["Strict-Transport-Security"] = "max-age=31536000";
                h["Content-Security-Policy"] =
                    "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
                    "connect-src 'self' wss:; font-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
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
                await connection.RunAsync(game, ctx.Connection.RemoteIpAddress?.ToString(), playerId, ctx.RequestAborted);
            });

            MapApi(app, game, accounts);
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

        private static void MapApi(WebApplication app, GameServer game, AccountStore accounts)
        {
            DateTime startedAt = DateTime.UtcNow;

            app.MapGet("/api/health", () =>
            {
                try
                {
                    int count = accounts.Count; // fragt die Datenbank ab, wirft bei DB-Fehler
                    return Results.Json(new
                    {
                        status = "ok",
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
                        }
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

    /// <summary>Räumt abgelaufene Sessions auf: beim Start und danach stündlich.</summary>
    internal sealed class SessionCleanupService : BackgroundService
    {
        private readonly AccountStore _accounts;
        public SessionCleanupService(AccountStore accounts) { _accounts = accounts; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield(); // Start des Hosts nicht durch den ersten DB-Zugriff blockieren
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            do
            {
                try { _accounts.CleanupSessions(); }
                catch (Exception ex) { Console.Error.WriteLine("[Sessions] Aufräumen fehlgeschlagen: " + ex.GetType().Name); }
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

        public WsConnection(WebSocket socket, int maxMessageBytes)
        {
            _socket = socket;
            _maxMessageBytes = maxMessageBytes;
        }

        public void Send(string json) => _outbox.Writer.TryWrite(json);

        public void Close(string reason)
        {
            _closeReason = reason;
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
                if (_closeReason != null && _socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, _closeReason, ct);
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }
        }
    }
}
