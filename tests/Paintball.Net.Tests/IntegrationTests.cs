using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Paintball.Server;

namespace Paintball.Net.Tests
{
    /// <summary>End-to-End über echtes TLS/WebSocket (QA-02, NFR-11, P-06).</summary>
    internal static class IntegrationTests
    {
        public static void Register(TestRunner r)
        {
            r.RunAsync("WSS: Login, Training starten, Snapshots empfangen über wss:// (FR-25/NFR-11)", WssEndToEnd);
            r.RunAsync("HTTPS: Health-Endpoint und Security-Header (NFR-06/NFR-11)", HealthAndHeaders);
            r.RunAsync("Health: Datenbank nicht erreichbar → 503 mit db=error", HealthDatabaseUnavailable);
            r.RunAsync("Health: persistence-Kennzahlen und degraded", HealthPersistenceMetricsAndDegraded);
            r.RunAsync("HTTPS: Kartendaten aus Core-MapCatalog für den Client (FR-53)", MapsApi);
            r.RunAsync("WSS: Fremde Origin wird abgewiesen (CSWSH-Schutz)", ForeignOriginRejected);
            r.RunAsync("HTTP: Weiterleitung auf HTTPS (NFR-11)", HttpRedirectsToHttps);
            r.RunAsync("DSGVO: Export und Löschung per Session-Cookie (NFR-12)", GdprEndpoints);
            r.RunAsync("WSS: Übergroße Nachricht wird abgelehnt, Verbindung bleibt (NFR-10)", OversizeMessage);
            r.RunAsync("Web: Client-Dateien werden ausgeliefert und komprimiert (PA-03)", ServesClient);
            r.RunAsync("Web: 3D-Modelle (glTF/bin) und HDRI werden mit korrektem Typ ausgeliefert", ServesModels);
            r.RunAsync("Web: Landingpage unter /, Einladung /?join= leitet ins Spiel (Query bleibt)", LandingAndJoinRedirect);
            r.RunAsync("Web: /play, /impressum, /datenschutz liefern ihre Seite, /play/ → /play", PageRoutes);
            r.RunAsync("Web: Service Worker wird nie gecacht (no-cache)", ServiceWorkerNoCache);
            r.RunAsync("Proxy: Hinter TLS-Reverse-Proxy nur HTTP, X-Forwarded-Proto zählt als HTTPS", ProxyTrustsForwardedProto);
            r.RunAsync("Proxy: Ohne Forwarded-Proto Weiterleitung auf HTTPS ohne internen Port", ProxyRedirectsWithoutPort);
            r.RunAsync("Proxy: WebSocket über den Proxy mit gleicher Origin", ProxyWebSocket);
            r.RunAsync("Proxy: Container-Healthcheck erreicht /api/health ohne Proxy-Header", ProxyHealthWithoutForwarding);
            r.RunAsync("Auth: Dev-Login nur mit Flag, Cookie HttpOnly/Secure/SameSite=Lax", DevLoginCookie);
            r.RunAsync("Auth: altes Cookie pb_session wird nicht mehr akzeptiert", LegacySessionCookieRejected);
            r.RunAsync("Auth: /api/me 401 ohne Session, needsName nach erstem Login, Namenswahl mit taken/invalid", MeAndName);
            r.RunAsync("Auth: /ws ohne Cookie 401, ohne Namen 403", WsRequiresSession);
            r.RunAsync("Auth: Abmelden macht Session ungültig", Logout);
            r.RunAsync("Auth: POST ohne gleiche Origin wird abgelehnt", CsrfOrigin);
            r.RunAsync("Auth: Rate-Limit 20/min/IP auf /api/auth/logout, pro Server-Instanz", LogoutRateLimit);
            r.Run("Auth: RateLimiter – Fenster läuft ab, IPs getrennt, alte Einträge werden entfernt", RateLimiterWindow);
            r.RunAsync("Google: Start setzt state-Cookie und leitet mit Client-ID/Scope/Redirect zu Google", GoogleStart);
            r.RunAsync("Google: Callback mit falschem oder fehlendem state erzeugt keine Session", GoogleBadState);
            r.RunAsync("Google: Callback legt Konto an, setzt Session, behält Einladungscode", GoogleCallbackOk);
            r.RunAsync("Google: Fehler beim Token-Tausch → oauth_failed, ohne Details", GoogleExchangeFails);
            r.RunAsync("Google: nicht konfiguriert → not_configured", GoogleNotConfigured);
            r.RunAsync("Google: Abbruch bei Google → auth_error=cancelled", GoogleCancelled);
            r.RunAsync("Google: Login ohne Namensbedarf löscht einen alten Namensvorschlag", GoogleClearsSuggest);
            r.RunAsync("Google: Cookie ohne Verifier (altes Format) → invalid_state", GoogleCookieWithoutVerifier);
            r.RunAsync("Google: Cookie mit fehlerhaftem Verifier (Länge/Zeichen) → invalid_state", GoogleMalformedVerifier);
            r.RunAsync("Google: Abbruch ohne pb_oauth-Cookie → invalid_state, keine Session (Anti-Login-CSRF)", GoogleCancelledWithoutCookie);
            r.RunAsync("Google: no-store auch bei Rate-Limit (429) auf der Start-Route", GoogleStartRateLimitNoStore);
            r.RunAsync("Herunterfahren schreibt offene Spielstände", ShutdownFlushesPendingWrites);
            r.RunAsync("Herunterfahren mit offener WebSocket-Verbindung: 1001 server_restart, alles geschrieben, zügig", ShutdownWithOpenWebSocket);
            r.RunAsync("Spieltakt: StopAsync kehrt erst zurück, wenn die Takt-Schleife beendet ist", GameLoopStopWaitsForLoop);
            r.RunAsync("Auth: Dev-Login auf ein gerade gelöschtes Konto → 409 statt 500", DevLoginDeletedDuringSignIn);
        }

        private static string HarnessWebRoot;

        private sealed class FakeGoogle : IGoogleOAuthClient
        {
            public string LastCode, LastRedirect, LastVerifier;
            public string GivenName = "Omar";
            public Task<GoogleUser> ExchangeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct)
            {
                LastCode = code; LastRedirect = redirectUri; LastVerifier = codeVerifier;
                if (code == "bad") throw new InvalidOperationException("token error");
                return Task.FromResult(new GoogleUser { Sub = "g-" + code, Email = code + "@gmail.com", GivenName = GivenName });
            }
        }

        private sealed class ThrowingRepository : Paintball.Net.Accounts.IPlayerRepository
        {
            private readonly Paintball.Net.Accounts.InMemoryPlayerRepository _inner;

            public ThrowingRepository()
            {
                _inner = new Paintball.Net.Accounts.InMemoryPlayerRepository();
            }

            public Paintball.Net.Accounts.PlayerRecord FindBySub(string googleSub) => _inner.FindBySub(googleSub);
            public Paintball.Net.Accounts.PlayerRecord Get(string playerId) => _inner.Get(playerId);
            public Paintball.Net.Accounts.PlayerRecord Create(string googleSub, string email) => _inner.Create(googleSub, email);
            public void RecordLogin(string playerId, string email, DateTime when) => _inner.RecordLogin(playerId, email, when);
            public void SaveProgress(Paintball.Net.Accounts.PlayerRecord player) => _inner.SaveProgress(player);
            public Paintball.Net.Accounts.NameResult TrySetName(string playerId, string name) => _inner.TrySetName(playerId, name);
            public void AddMatch(string playerId, Paintball.Net.Accounts.MatchRecord match) => _inner.AddMatch(playerId, match);
            public IReadOnlyList<Paintball.Net.Accounts.MatchRecord> RecentMatches(string playerId, int limit) => _inner.RecentMatches(playerId, limit);
            public IReadOnlyList<Paintball.Net.Accounts.PlayerRecord> TopByMmr(int limit) => _inner.TopByMmr(limit);
            public int Count() => throw new InvalidOperationException("db down");
            public bool Delete(string playerId) => _inner.Delete(playerId);
            public void CreateSession(string tokenHash, string playerId, DateTime expiresAt) => _inner.CreateSession(tokenHash, playerId, expiresAt);
            public string PlayerIdForSession(string tokenHash, DateTime now) => _inner.PlayerIdForSession(tokenHash, now);
            public void DeleteSession(string tokenHash) => _inner.DeleteSession(tokenHash);
            public void DeleteSessionsOf(string playerId) => _inner.DeleteSessionsOf(playerId);
            public int DeleteExpiredSessions(DateTime now) => _inner.DeleteExpiredSessions(now);
        }

        private static string Challenge(string v) => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(v)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static (string State, string Cookie) ReadOAuthCookie(HttpResponseMessage start)
        {
            string set = start.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_oauth="));
            string cookie = set.Substring(0, set.IndexOf(';'));
            var q = System.Web.HttpUtility.ParseQueryString(start.Headers.Location.Query);
            return (q["state"], cookie);
        }

        private static bool SetsSession(HttpResponseMessage res)
            => res.Headers.TryGetValues("Set-Cookie", out var sc) && sc.Any(v => v.StartsWith("__Host-pb_session="));

        private static async Task GoogleStart()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage res = await h.Http.GetAsync("/api/auth/google?join=AB12");
            Assert.AreEqual(HttpStatusCode.Found, res.StatusCode, "Weiterleitung");
            Uri loc = res.Headers.Location;
            Assert.AreEqual("accounts.google.com", loc.Host, "zu Google");
            var q = System.Web.HttpUtility.ParseQueryString(loc.Query);
            Assert.AreEqual("test-client", q["client_id"], "Client-ID");
            Assert.AreEqual("openid email profile", q["scope"], "Scope");
            Assert.AreEqual("code", q["response_type"], "Code-Flow");
            Assert.IsTrue(q["redirect_uri"].EndsWith("/api/auth/google/callback"), "Redirect-URI");
            Assert.IsTrue(q["state"].Length >= 32, "state zufällig");
            Assert.AreEqual("S256", q["code_challenge_method"], "PKCE-Methode");
            Assert.IsTrue(q["code_challenge"].Length == 43, "code_challenge base64url(SHA256) ohne Padding");
            Assert.IsTrue(res.Headers.CacheControl?.NoStore == true, "no-store");
            string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_oauth=")).ToLowerInvariant();
            Assert.IsTrue(set.Contains("httponly") && set.Contains("secure") && set.Contains("path=/api/auth"), "Cookie-Attribute");
            Assert.IsFalse(res.Headers.Location.ToString().Contains("test-secret"), "Secret nie in der URL");
        }

        private static async Task GoogleBadState()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, cookie) = ReadOAuthCookie(start);
            foreach (var (qs, ck) in new[] { ($"code=c1&state=falsch", cookie), ($"code=c1&state={state}", (string)null), ("code=c1", cookie) })
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/google/callback?" + qs);
                if (ck != null) req.Headers.Add("Cookie", ck);
                HttpResponseMessage res = await h.Http.SendAsync(req);
                Assert.AreEqual("/play?auth_error=invalid_state", res.Headers.Location.OriginalString, "Fehler-Weiterleitung: " + qs);
                Assert.IsFalse(SetsSession(res), "keine Session");
                Assert.IsTrue(res.Headers.CacheControl?.NoStore == true, "no-store: " + qs);
            }
        }

        private static async Task GoogleCallbackOk()
        {
            var fake = new FakeGoogle();
            await using Harness h = await Harness.StartAsync(google: fake);
            string oldSession = await h.LoginAsync("Vorher");
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google?join=AB12");
            var (state, cookie) = ReadOAuthCookie(start);
            string expectedChallenge = System.Web.HttpUtility.ParseQueryString(start.Headers.Location.Query)["code_challenge"];
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code=c77&state={state}");
            req.Headers.Add("Cookie", cookie + "; " + oldSession);
            HttpResponseMessage res = await h.Http.SendAsync(req);
            Assert.AreEqual("/play?join=AB12", res.Headers.Location.OriginalString, "zurück mit Einladungscode");
            Assert.AreEqual("c77", fake.LastCode, "Code weitergereicht");
            Assert.IsTrue(fake.LastRedirect.EndsWith("/api/auth/google/callback"), "gleiche Redirect-URI");
            Assert.IsTrue(fake.LastVerifier?.Length == 43, "Verifier 43 Zeichen");
            Assert.AreEqual(expectedChallenge, Challenge(fake.LastVerifier), "Challenge aus Verifier passt zur Start-Weiterleitung");
            Assert.IsTrue(res.Headers.CacheControl?.NoStore == true, "no-store auf der Callback-Antwort");
            string session = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("__Host-pb_session="));
            session = session.Substring(0, session.IndexOf(';'));
            var meReq = h.Req(HttpMethod.Get, "/api/me", session);
            string suggest = res.Headers.GetValues("Set-Cookie").FirstOrDefault(v => v.StartsWith("pb_suggest="));
            if (suggest != null) meReq.Headers.Add("Cookie", suggest.Substring(0, suggest.IndexOf(';')));
            JsonElement me = JsonDocument.Parse(await (await h.Http.SendAsync(meReq)).Content.ReadAsStringAsync()).RootElement;
            Assert.IsTrue(me.GetProperty("needsName").GetBoolean(), "neu → Namenswahl");
            Assert.AreEqual("Omar", me.GetProperty("suggestedName").GetString(), "Vorschlag aus Google-Vorname");
            HttpResponseMessage oldMe = await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", oldSession));
            Assert.AreEqual(HttpStatusCode.Unauthorized, oldMe.StatusCode, "alte Session nach Google-Login ungültig");
        }

        private static async Task GoogleExchangeFails()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, cookie) = ReadOAuthCookie(start);
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code=bad&state={state}");
            req.Headers.Add("Cookie", cookie);
            HttpResponseMessage res = await h.Http.SendAsync(req);
            Assert.AreEqual("/play?auth_error=oauth_failed", res.Headers.Location.OriginalString, "allgemeiner Fehler");
            Assert.IsFalse(SetsSession(res), "keine Session");
        }

        private static async Task GoogleNotConfigured()
        {
            await using Harness h = await Harness.StartAsync(google: null, googleConfigured: false);
            HttpResponseMessage res = await h.Http.GetAsync("/api/auth/google");
            Assert.AreEqual("/play?auth_error=not_configured", res.Headers.Location.OriginalString, "nicht konfiguriert");
        }

        private static async Task GoogleCancelled()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, cookie) = ReadOAuthCookie(start);
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?error=access_denied&state={state}");
            req.Headers.Add("Cookie", cookie);
            HttpResponseMessage res = await h.Http.SendAsync(req);
            Assert.AreEqual("/play?auth_error=cancelled", res.Headers.Location.OriginalString, "Abbruch bei Google");
            Assert.IsFalse(SetsSession(res), "keine Session");
            Assert.IsTrue(res.Headers.CacheControl?.NoStore == true, "no-store");
        }

        private static async Task GoogleCancelledWithoutCookie()
        {
            // Anti-Login-CSRF: ohne den pb_oauth-Cookie (state gehört zum Browser, nicht zur URL) darf ein
            // fremd verlinkter "error=access_denied&state=..." nicht als eigener Abbruch durchgehen.
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, _) = ReadOAuthCookie(start);
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?error=access_denied&state={state}");
            HttpResponseMessage res = await h.Http.SendAsync(req);
            Assert.AreEqual("/play?auth_error=invalid_state", res.Headers.Location.OriginalString, "ohne Cookie greift die state-Prüfung zuerst");
            Assert.IsFalse(SetsSession(res), "keine Session");
        }

        private static async Task GoogleClearsSuggest()
        {
            var fake = new FakeGoogle { GivenName = null };
            await using Harness h = await Harness.StartAsync(google: fake);
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, cookie) = ReadOAuthCookie(start);
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code=c1&state={state}");
            req.Headers.Add("Cookie", cookie);
            HttpResponseMessage res = await h.Http.SendAsync(req);
            string suggest = res.Headers.GetValues("Set-Cookie").FirstOrDefault(v => v.StartsWith("pb_suggest="));
            Assert.IsTrue(suggest != null, "pb_suggest wird gelöscht");
            string value = suggest.Substring("pb_suggest=".Length, suggest.IndexOf(';') - "pb_suggest=".Length);
            Assert.AreEqual("", value, "Cookie-Wert leer");
            Assert.IsTrue(suggest.ToLowerInvariant().Contains("expires="), "abgelaufenes expires");
            Assert.IsTrue(suggest.ToLowerInvariant().Contains("path=/"), "Lösch-Cookie mit path=/");

            // Zweiter Login desselben Google-Kontos: der Name wurde inzwischen vergeben (kein Namensbedarf
            // mehr), diesmal liefert Google sogar einen Vornamen – trotzdem muss pb_suggest gelöscht werden,
            // denn die Bedingung ist s.NeedsName, nicht das Vorhandensein eines Vornamens.
            string session = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("__Host-pb_session="));
            session = session.Substring(0, session.IndexOf(';'));
            HttpResponseMessage named = await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/me/name", session, "{\"name\":\"Cleo\"}"));
            Assert.AreEqual(HttpStatusCode.OK, named.StatusCode, "Name gesetzt");

            fake.GivenName = "Omar";
            HttpResponseMessage start2 = await h.Http.GetAsync("/api/auth/google");
            var (state2, cookie2) = ReadOAuthCookie(start2);
            var req2 = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code=c1&state={state2}");
            req2.Headers.Add("Cookie", cookie2);
            HttpResponseMessage res2 = await h.Http.SendAsync(req2);
            string suggest2 = res2.Headers.GetValues("Set-Cookie").FirstOrDefault(v => v.StartsWith("pb_suggest="));
            Assert.IsTrue(suggest2 != null, "pb_suggest wird auch ohne Namensbedarf gelöscht");
            string value2 = suggest2.Substring("pb_suggest=".Length, suggest2.IndexOf(';') - "pb_suggest=".Length);
            Assert.AreEqual("", value2, "Cookie-Wert leer (kein Namensbedarf, obwohl Vorname vorhanden)");
        }

        private static async Task GoogleCookieWithoutVerifier()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, _) = ReadOAuthCookie(start);
            string oldCookie = "pb_oauth=" + state + ".";
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code=c1&state={state}");
            req.Headers.Add("Cookie", oldCookie);
            HttpResponseMessage res = await h.Http.SendAsync(req);
            Assert.AreEqual("/play?auth_error=invalid_state", res.Headers.Location.OriginalString, "Cookie ohne Verifier (altes Format)");
            Assert.IsFalse(SetsSession(res), "keine Session");
        }

        private static async Task GoogleMalformedVerifier()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, _) = ReadOAuthCookie(start);
            foreach (string verifier in new[] { "short", new string('a', 42) + "!" })
            {
                string cookie = "pb_oauth=" + state + ".." + verifier;
                var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code=c1&state={state}");
                req.Headers.Add("Cookie", cookie);
                HttpResponseMessage res = await h.Http.SendAsync(req);
                Assert.AreEqual("/play?auth_error=invalid_state", res.Headers.Location.OriginalString, "Verifier ungültig: " + verifier);
                Assert.IsFalse(SetsSession(res), "keine Session: " + verifier);
            }
        }

        private static async Task GoogleStartRateLimitNoStore()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage res = null;
            for (int i = 1; i <= 21; i++)
                res = await h.Http.GetAsync("/api/auth/google");
            Assert.AreEqual((HttpStatusCode)429, res.StatusCode, "21. Anfrage begrenzt");
            Assert.IsTrue(res.Headers.CacheControl?.NoStore == true, "no-store auch bei 429");
        }

        private sealed class Harness : IAsyncDisposable
        {
            public WebApplication App;
            public int HttpsPort;
            public int HttpPort;
            public HttpClient Http;

            public static async Task<Harness> StartAsync(bool devLogin = true, IGoogleOAuthClient google = null, bool googleConfigured = true, Paintball.Net.Accounts.IPlayerRepository repository = null, bool backgroundPersistence = false, int[] retryDelaysMs = null)
            {
                string web = AccountTests.TempDir();
                HarnessWebRoot = web;
                File.WriteAllText(Path.Combine(web, "index.html"), "<!doctype html><title>Paint-Ball</title>" + new string('x', 4000));
                File.WriteAllText(Path.Combine(web, "play.html"), "<!doctype html><title>Spiel</title>");
                File.WriteAllText(Path.Combine(web, "impressum.html"), "<!doctype html><title>Impressum</title>");
                File.WriteAllText(Path.Combine(web, "datenschutz.html"), "<!doctype html><title>Datenschutz</title>");
                File.WriteAllText(Path.Combine(web, "sw.js"), "self.PB_SW = {};\r\n");
                var options = new ServerHostOptions
                {
                    HttpsPort = 0,
                    HttpPort = 0,
                    WebRoot = web,
                    DevLogin = devLogin,
                    GoogleClientId = googleConfigured ? "test-client" : null,
                    GoogleClientSecret = googleConfigured ? "test-secret" : null,
                    Google = google ?? new FakeGoogle(), // der echte Client wird in Tests nie aufgerufen
                    PublicUrl = null,
                    Repository = repository,
                    BackgroundPersistence = backgroundPersistence,
                    RetryDelaysMs = retryDelaysMs,
                    Game = new Paintball.Net.Rooms.ServerOptions { LobbyCountdownSeconds = 0.5f }
                };
                WebApplication app = ServerHost.Build(Array.Empty<string>(), options);
                await app.StartAsync();
                var h = new Harness { App = app };
                var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses;
                h.HttpsPort = new Uri(addresses.First(a => a.StartsWith("https"))).Port;
                h.HttpPort = new Uri(addresses.First(a => a.StartsWith("http:"))).Port;
                h.Http = new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.None
                }) { BaseAddress = new Uri($"https://localhost:{h.HttpsPort}") };
                return h;
            }

            /// <summary>Meldet sich per Dev-Login an und gibt den Cookie-Header "__Host-pb_session=…" zurück.</summary>
            public async Task<string> LoginAsync(string name)
            {
                HttpResponseMessage res = await Http.GetAsync("/api/auth/dev?name=" + Uri.EscapeDataString(name));
                Assert.AreEqual(HttpStatusCode.Found, res.StatusCode, "Dev-Login leitet weiter");
                string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("__Host-pb_session=", StringComparison.Ordinal));
                return set.Substring(0, set.IndexOf(';'));
            }

            public HttpRequestMessage Req(HttpMethod m, string path, string cookie, string json = null)
            {
                var req = new HttpRequestMessage(m, path);
                if (cookie != null) req.Headers.Add("Cookie", cookie);
                req.Headers.Add("Origin", $"https://localhost:{HttpsPort}");
                if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return req;
            }

            public async Task<ClientWebSocket> ConnectAsync(string origin = null, string cookie = null)
            {
                var ws = new ClientWebSocket();
                ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true; // Entwicklerzertifikat
                if (origin != null) ws.Options.SetRequestHeader("Origin", origin);
                if (cookie != null) ws.Options.SetRequestHeader("Cookie", cookie);
                await ws.ConnectAsync(new Uri($"wss://localhost:{HttpsPort}/ws"), CancellationToken.None);
                return ws;
            }

            public async ValueTask DisposeAsync()
            {
                Http.Dispose();
                await App.StopAsync();
                await App.DisposeAsync();
            }
        }

        private static Task SendAsync(ClientWebSocket ws, object msg)
            => ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg)), WebSocketMessageType.Text, true, CancellationToken.None);

        private static async Task<JsonElement> ReceiveUntil(ClientWebSocket ws, string type, int timeoutMs = 8000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var buffer = new byte[65536];
            while (true)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, cts.Token);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);
                JsonElement e = JsonDocument.Parse(sb.ToString()).RootElement;
                if (e.GetProperty("t").GetString() == type) return e;
            }
        }

        private static async Task WssEndToEnd()
        {
            await using Harness h = await Harness.StartAsync();
            string cookie = await h.LoginAsync("Integration");
            using ClientWebSocket ws = await h.ConnectAsync($"https://localhost:{h.HttpsPort}", cookie);
            await SendAsync(ws, new { t = "hello", input = "kbm", crossPlay = true });
            JsonElement welcome = await ReceiveUntil(ws, "welcome");
            Assert.IsFalse(string.IsNullOrEmpty(welcome.GetProperty("account").GetString()), "Konto über WSS erhalten (Google-Login)");

            await SendAsync(ws, new { t = "create", mode = "training", map = "arena", bots = 3 });
            JsonElement start = await ReceiveUntil(ws, "start");
            Assert.AreEqual("arena", start.GetProperty("map").GetString(), "Match gestartet");
            JsonElement snap = await ReceiveUntil(ws, "s");
            Assert.IsTrue(snap.GetProperty("pl").GetArrayLength() >= 1, "Snapshot mit Spielern");

            await SendAsync(ws, new { t = "in", s = 1, mx = 0, mz = 1, y = 0, p = 0, ay = 0, ap = 0, b = 0 });
            await SendAsync(ws, new { t = "ping", c = 42.0 });
            JsonElement pong = await ReceiveUntil(ws, "pong");
            Assert.AreEqual(42.0, pong.GetProperty("c").GetDouble(), "Ping/Pong für RTT (FR-29)");
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }

        private static async Task HealthAndHeaders()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage res = await h.Http.GetAsync("/api/health");
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, "Health 200");
            JsonElement body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            Assert.AreEqual("ok", body.GetProperty("status").GetString(), "Status ok");
            Assert.IsTrue(res.Headers.Contains("Content-Security-Policy"), "CSP gesetzt");
            Assert.IsTrue(res.Headers.GetValues("X-Content-Type-Options").First() == "nosniff", "nosniff");
            Assert.IsTrue(res.Headers.Contains("Strict-Transport-Security"), "HSTS");
        }

        private static async Task HealthDatabaseUnavailable()
        {
            var throwingRepo = new ThrowingRepository();
            await using Harness h = await Harness.StartAsync(repository: throwingRepo);
            HttpResponseMessage res = await h.Http.GetAsync("/api/health");
            Assert.AreEqual((HttpStatusCode)503, res.StatusCode, "Health 503");
            string bodyText = await res.Content.ReadAsStringAsync();
            JsonElement body = JsonDocument.Parse(bodyText).RootElement;
            Assert.AreEqual("degraded", body.GetProperty("status").GetString(), "Status degraded");
            Assert.AreEqual("error", body.GetProperty("db").GetString(), "db=error");
            Assert.IsFalse(bodyText.Contains("db down"), "Fehlermeldung nicht nach außen");
        }

        private static async Task HealthPersistenceMetricsAndDegraded()
        {
            await using (Harness ok = await Harness.StartAsync())
            {
                HttpResponseMessage res = await ok.Http.GetAsync("/api/health");
                Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, "Health 200");
                JsonElement body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
                Assert.AreEqual("ok", body.GetProperty("status").GetString(), "Status ok ohne Fehler");
                Assert.AreEqual(0, body.GetProperty("persistence").GetProperty("pending").GetInt32(), "nichts offen");
            }

            var repo = new RecordingRepository { FailTimes = int.MaxValue };   // SaveProgress/AddMatch werfen immer
            await using Harness degraded = await Harness.StartAsync(repository: repo, backgroundPersistence: true, retryDelaysMs: new[] { 1, 1, 1 });
            string cookie = await degraded.LoginAsync("Persistenz");
            Paintball.Net.Accounts.AccountStore accounts = degraded.App.Services.GetRequiredService<Paintball.Net.Rooms.GameServer>().Accounts;
            string id = accounts.PlayerIdForSession(cookie.Substring(cookie.IndexOf('=') + 1));
            Assert.IsTrue(id != null, "Spieler per Dev-Login angelegt");
            accounts.ApplyMatch(id, new Paintball.Net.Accounts.MatchSummary { Mode = "tdm", Map = "arena", XpGained = 100 });
            await accounts.FlushAsync(TimeSpan.FromSeconds(10));

            HttpResponseMessage degradedRes = await degraded.Http.GetAsync("/api/health");
            Assert.AreEqual(HttpStatusCode.OK, degradedRes.StatusCode, "Health bleibt 200 bei degraded (Container-Healthcheck grün)");
            JsonElement degradedBody = JsonDocument.Parse(await degradedRes.Content.ReadAsStringAsync()).RootElement;
            Assert.AreEqual("degraded", degradedBody.GetProperty("status").GetString(), "Status degraded");
            Assert.IsTrue(degradedBody.GetProperty("persistence").GetProperty("failures").GetInt64() >= 1, "persistence.failures >= 1");
        }

        private static async Task ServesModels()
        {
            await using Harness h = await Harness.StartAsync();
            var files = new (string Path, string Type)[]
            {
                ("assets/characters/human.gltf", "model/gltf+json"),
                ("assets/models/tire/tire.bin", "application/octet-stream"),
                ("assets/hdri/sky.hdr", "image/vnd.radiance")
            };
            foreach (var f in files)
            {
                string full = System.IO.Path.Combine(HarnessWebRoot, f.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
                File.WriteAllText(full, "{}");
                HttpResponseMessage res = await h.Http.GetAsync("/" + f.Path);
                Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, f.Path + " ausgeliefert");
                Assert.AreEqual(f.Type, res.Content.Headers.ContentType?.MediaType, f.Path + " Typ");
            }
        }

        private static async Task MapsApi()
        {
            await using Harness h = await Harness.StartAsync();
            JsonElement body = JsonDocument.Parse(await h.Http.GetStringAsync("/api/maps")).RootElement;
            JsonElement[] maps = body.GetProperty("maps").EnumerateArray().ToArray();
            Assert.AreEqual(4, maps.Length, "3 Launch-Karten + Turnierfeld");
            Assert.IsTrue(maps.Any(m => m.GetProperty("id").GetString() == "speedball" && m.GetProperty("covers")[0].GetArrayLength() == 8), "Bunker mit realer Form (Kind) für den Client");
            Assert.IsTrue(maps.All(m => m.GetProperty("covers").GetArrayLength() > 3), "Deckung enthalten");
        }

        private static async Task ForeignOriginRejected()
        {
            await using Harness h = await Harness.StartAsync();
            bool rejected = false;
            string cookie = await h.LoginAsync("Fremd");
            try { using ClientWebSocket ws = await h.ConnectAsync("https://evil.example", cookie); }
            catch (WebSocketException) { rejected = true; }
            Assert.IsTrue(rejected, "Fremde Origin abgewiesen");
        }

        private static async Task HttpRedirectsToHttps()
        {
            await using Harness h = await Harness.StartAsync();
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            HttpResponseMessage res = await http.GetAsync($"http://localhost:{h.HttpPort}/index.html");
            Assert.IsTrue((int)res.StatusCode >= 300 && (int)res.StatusCode < 400, "Redirect");
            Assert.IsTrue(res.Headers.Location.ToString().StartsWith($"https://localhost:{h.HttpsPort}/"), "Ziel ist HTTPS");
        }

        private static async Task GdprEndpoints()
        {
            await using Harness h = await Harness.StartAsync();
            string cookie = await h.LoginAsync("Datenschutz");

            HttpResponseMessage export = await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me/export", cookie));
            Assert.AreEqual(HttpStatusCode.OK, export.StatusCode, "Export erlaubt");
            Assert.IsTrue((await export.Content.ReadAsStringAsync()).Contains("Datenschutz"), "Daten enthalten");

            Assert.AreEqual(HttpStatusCode.Unauthorized, (await h.Http.GetAsync("/api/me/export")).StatusCode, "Ohne Session kein Zugriff");

            Assert.AreEqual(HttpStatusCode.NoContent, (await h.Http.SendAsync(h.Req(HttpMethod.Delete, "/api/me", cookie))).StatusCode, "Gelöscht");
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me/export", cookie))).StatusCode, "Session nach Löschung ungültig");
        }

        private static async Task OversizeMessage()
        {
            await using Harness h = await Harness.StartAsync();
            string cookie = await h.LoginAsync("Gross");
            using ClientWebSocket ws = await h.ConnectAsync(cookie: cookie);
            await SendAsync(ws, new { t = "hello" });
            await ReceiveUntil(ws, "welcome");
            await ws.SendAsync(Encoding.UTF8.GetBytes("{\"t\":\"ping\",\"x\":\"" + new string('a', 100000) + "\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
            JsonElement err = await ReceiveUntil(ws, "error");
            Assert.AreEqual("too_large", err.GetProperty("code").GetString(), "Zu groß");
            await SendAsync(ws, new { t = "ping", c = 7.0 });
            Assert.AreEqual(7.0, (await ReceiveUntil(ws, "pong")).GetProperty("c").GetDouble(), "Verbindung lebt");
        }

        private static async Task LandingAndJoinRedirect()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage landing = await h.Http.GetAsync("/");
            Assert.AreEqual(HttpStatusCode.OK, landing.StatusCode, "Landingpage 200");
            Assert.IsTrue((await landing.Content.ReadAsStringAsync()).Contains("<title>Paint-Ball</title>"), "index.html unter /");

            HttpResponseMessage join = await h.Http.GetAsync("/?join=AB12&x=1");
            Assert.AreEqual(HttpStatusCode.Found, join.StatusCode, "Einladung wird weitergeleitet");
            Assert.AreEqual("/play?join=AB12&x=1", join.Headers.Location.OriginalString, "Query bleibt vollständig erhalten");
        }

        private static async Task PageRoutes()
        {
            await using Harness h = await Harness.StartAsync();
            foreach (var (path, title) in new[] { ("/play", "Spiel"), ("/impressum", "Impressum"), ("/datenschutz", "Datenschutz") })
            {
                HttpResponseMessage res = await h.Http.GetAsync(path);
                Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, path + " 200");
                Assert.IsTrue((await res.Content.ReadAsStringAsync()).Contains($"<title>{title}</title>"), path + " liefert eigene Seite");
                Assert.AreEqual("text/html", res.Content.Headers.ContentType?.MediaType, path + " als HTML");
            }
            HttpResponseMessage slash = await h.Http.GetAsync("/play/?join=X");
            Assert.AreEqual(HttpStatusCode.MovedPermanently, slash.StatusCode, "/play/ dauerhaft umgeleitet");
            Assert.AreEqual("/play?join=X", slash.Headers.Location.OriginalString, "ohne Slash, Query erhalten");
        }

        private static async Task ServiceWorkerNoCache()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage res = await h.Http.GetAsync("/sw.js");
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, "sw.js 200");
            Assert.IsTrue(res.Headers.CacheControl?.NoCache == true, "sw.js mit no-cache, damit Updates sofort ankommen");
        }

        private static async Task ServesClient()
        {
            await using Harness h = await Harness.StartAsync();
            var req = new HttpRequestMessage(HttpMethod.Get, "/");
            req.Headers.AcceptEncoding.ParseAdd("br");
            HttpResponseMessage res = await h.Http.SendAsync(req);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, "index.html ausgeliefert");
            Assert.IsTrue(res.Content.Headers.ContentEncoding.Contains("br"), "Brotli-Kompression (PA-03)");
        }
            private static async Task<(WebApplication App, int Port)> StartProxyModeAsync()
        {
            var options = new ServerHostOptions
            {
                BehindProxy = true,
                HttpPort = 0,
                DevLogin = true,
                WebRoot = AccountTests.TempDir(),
                Game = new Paintball.Net.Rooms.ServerOptions { LobbyCountdownSeconds = 0.5f }
            };
            WebApplication app = ServerHost.Build(Array.Empty<string>(), options);
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses;
            Assert.IsTrue(addresses.All(a => a.StartsWith("http:")), "Kein eigener TLS-Listener hinter dem Proxy");
            return (app, new Uri(addresses.First()).Port);
        }

        private static async Task ProxyTrustsForwardedProto()
        {
            var (app, port) = await StartProxyModeAsync();
            try
            {
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                var req = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/health");
                req.Headers.Add("X-Forwarded-Proto", "https");
                HttpResponseMessage res = await http.SendAsync(req);
                Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, "Health 200 über den Proxy");
                Assert.IsTrue(res.Headers.Contains("Strict-Transport-Security"), "HSTS auch hinter dem Proxy");
            }
            finally { await app.StopAsync(); await app.DisposeAsync(); }
        }

        private static async Task ProxyRedirectsWithoutPort()
        {
            var (app, port) = await StartProxyModeAsync();
            try
            {
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                var req = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/index.html");
                req.Headers.Host = "paint-ball-game.example";
                HttpResponseMessage res = await http.SendAsync(req);
                Assert.IsTrue((int)res.StatusCode >= 300 && (int)res.StatusCode < 400, "Redirect");
                Assert.AreEqual("https://paint-ball-game.example/index.html", res.Headers.Location.ToString(), "Öffentliche HTTPS-URL ohne Port");
            }
            finally { await app.StopAsync(); await app.DisposeAsync(); }
        }

        private static async Task ProxyHealthWithoutForwarding()
        {
            var (app, port) = await StartProxyModeAsync();
            try
            {
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                HttpResponseMessage res = await http.GetAsync($"http://localhost:{port}/api/health");
                Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, "Docker-Healthcheck ohne Umleitung");
            }
            finally { await app.StopAsync(); await app.DisposeAsync(); }
        }

        private static async Task ProxyWebSocket()
        {
            var (app, port) = await StartProxyModeAsync();
            try
            {
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
                var login = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/auth/dev?name=Proxy");
                login.Headers.Add("X-Forwarded-Proto", "https");
                HttpResponseMessage res = await http.SendAsync(login);
                Assert.AreEqual(HttpStatusCode.Found, res.StatusCode, "Dev-Login über den Proxy");
                string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("__Host-pb_session=", StringComparison.Ordinal));

                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Origin", $"https://localhost:{port}");
                ws.Options.SetRequestHeader("X-Forwarded-Proto", "https");
                ws.Options.SetRequestHeader("Cookie", set.Substring(0, set.IndexOf(';')));
                await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);
                await SendAsync(ws, new { t = "hello" });
                Assert.IsFalse(string.IsNullOrEmpty((await ReceiveUntil(ws, "welcome")).GetProperty("account").GetString()), "Login über den Proxy");
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            finally { await app.StopAsync(); await app.DisposeAsync(); }
        }

        private static async Task DevLoginCookie()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage res = await h.Http.GetAsync("/api/auth/dev?name=Tester");
            string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("__Host-pb_session="));
            string lower = set.ToLowerInvariant();
            Assert.IsTrue(lower.Contains("httponly") && lower.Contains("secure") && lower.Contains("samesite=lax") && lower.Contains("path=/"), "Cookie-Attribute");
            Assert.IsFalse(lower.Contains("domain="), "__Host- verbietet ein Domain-Attribut");
            Assert.AreEqual("/play", res.Headers.Location.OriginalString, "zurück ins Spiel");

            // Das alte Cookie pb_session wird beim Setzen der neuen Session mitgelöscht (leer, abgelaufen).
            string legacyDelete = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_session=", StringComparison.Ordinal));
            string legacyValue = legacyDelete.Substring("pb_session=".Length, legacyDelete.IndexOf(';') - "pb_session=".Length);
            Assert.AreEqual("", legacyValue, "altes Cookie: leerer Wert");
            Assert.IsTrue(legacyDelete.ToLowerInvariant().Contains("expires="), "altes Cookie: abgelaufenes expires");

            // Erneuter Login mit altem Cookie beendet die alte Session
            string old = set.Substring(0, set.IndexOf(';'));
            var again = new HttpRequestMessage(HttpMethod.Get, "/api/auth/dev?name=Tester");
            again.Headers.Add("Cookie", old);
            HttpResponseMessage relogin = await h.Http.SendAsync(again);
            string fresh = relogin.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("__Host-pb_session="));
            fresh = fresh.Substring(0, fresh.IndexOf(';'));
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", old))).StatusCode, "alte Session nach Re-Login ungültig");
            Assert.AreEqual(HttpStatusCode.OK, (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", fresh))).StatusCode, "neue Session gültig");

            await using Harness prod = await Harness.StartAsync(devLogin: false);
            Assert.AreEqual(HttpStatusCode.NotFound, (await prod.Http.GetAsync("/api/auth/dev?name=X")).StatusCode, "ohne Flag 404");
        }

        private static async Task LegacySessionCookieRejected()
        {
            // Das alte Cookie pb_session darf nach der Umstellung auf __Host-pb_session nicht mehr akzeptiert werden,
            // selbst mit einem gültigen Token.
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage res = await h.Http.GetAsync("/api/auth/dev?name=Alt");
            string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("__Host-pb_session="));
            string token = set.Substring("__Host-pb_session=".Length, set.IndexOf(';') - "__Host-pb_session=".Length);
            HttpResponseMessage me = await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", "pb_session=" + token));
            Assert.AreEqual(HttpStatusCode.Unauthorized, me.StatusCode, "altes Cookie pb_session wird nicht mehr akzeptiert");
        }

        private static async Task MeAndName()
        {
            await using Harness h = await Harness.StartAsync();
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await h.Http.GetAsync("/api/me")).StatusCode, "ohne Session 401");
            string cookie = await h.LoginAsync("");                      // Dev-Login ohne Namen → needsName
            JsonElement me = JsonDocument.Parse(await (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", cookie))).Content.ReadAsStringAsync()).RootElement;
            Assert.IsTrue(me.GetProperty("needsName").GetBoolean(), "braucht Namen");

            HttpResponseMessage bad = await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/me/name", cookie, "{\"name\":\"x\"}"));
            Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode, "ungültig");
            HttpResponseMessage ok = await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/me/name", cookie, "{\"name\":\"Kira\"}"));
            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode, "gesetzt");

            string other = await h.LoginAsync("");
            HttpResponseMessage taken = await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/me/name", other, "{\"name\":\"KIRA\"}"));
            Assert.AreEqual(HttpStatusCode.Conflict, taken.StatusCode, "vergeben");
            Assert.IsTrue((await taken.Content.ReadAsStringAsync()).Contains("taken"), "Fehlercode taken");
        }

        private static async Task WsRequiresSession()
        {
            await using Harness h = await Harness.StartAsync();
            var noCookie = new ClientWebSocket();
            noCookie.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            noCookie.Options.CollectHttpResponseDetails = true;
            try { await noCookie.ConnectAsync(new Uri($"wss://localhost:{h.HttpsPort}/ws"), CancellationToken.None); } catch (WebSocketException) { }
            Assert.AreEqual(HttpStatusCode.Unauthorized, noCookie.HttpStatusCode, "ohne Cookie 401");

            string noName = await h.LoginAsync("");
            var probe = new ClientWebSocket();
            probe.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            probe.Options.SetRequestHeader("Cookie", noName);
            probe.Options.CollectHttpResponseDetails = true;
            try { await probe.ConnectAsync(new Uri($"wss://localhost:{h.HttpsPort}/ws"), CancellationToken.None); } catch (WebSocketException) { }
            Assert.AreEqual(HttpStatusCode.Forbidden, probe.HttpStatusCode, "ohne Namen 403");
        }

        private static async Task Logout()
        {
            await using Harness h = await Harness.StartAsync();
            string cookie = await h.LoginAsync("Lou");
            Assert.AreEqual(HttpStatusCode.NoContent, (await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/auth/logout", cookie))).StatusCode, "abgemeldet");
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", cookie))).StatusCode, "Session ungültig");
        }

        private static async Task CsrfOrigin()
        {
            await using Harness h = await Harness.StartAsync();
            string cookie = await h.LoginAsync("Cleo");
            HttpRequestMessage req = h.Req(HttpMethod.Delete, "/api/me", cookie);
            req.Headers.Remove("Origin");
            req.Headers.Add("Origin", "https://evil.example");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await h.Http.SendAsync(req)).StatusCode, "fremde Origin");
            Assert.AreEqual(HttpStatusCode.OK, (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", cookie))).StatusCode, "Konto noch da");
        }

        private static void RateLimiterWindow()
        {
            DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var limiter = new RateLimiter(limit: 2, window: TimeSpan.FromMinutes(1), clock: () => now);
            Assert.IsFalse(limiter.Exceeded("a") || limiter.Exceeded("a"), "2 erlaubt");
            Assert.IsTrue(limiter.Exceeded("a"), "3. begrenzt");
            Assert.IsFalse(limiter.Exceeded("b"), "andere IP unabhängig");
            now = now.AddMinutes(1);
            Assert.IsFalse(limiter.Exceeded("a"), "neues Fenster");
            Assert.AreEqual(1, limiter.Tracked, "abgelaufener Eintrag b entfernt");
        }

        private static async Task LogoutRateLimit()
        {
            await using Harness h = await Harness.StartAsync();
            for (int i = 1; i <= 20; i++)
                Assert.AreEqual(HttpStatusCode.NoContent, (await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/auth/logout", null))).StatusCode, $"Abmelden {i} erlaubt");
            Assert.AreEqual((HttpStatusCode)429, (await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/auth/logout", null))).StatusCode, "21. Anfrage begrenzt");

            await using Harness other = await Harness.StartAsync();
            Assert.AreEqual(HttpStatusCode.NoContent, (await other.Http.SendAsync(other.Req(HttpMethod.Post, "/api/auth/logout", null))).StatusCode, "frische Instanz unbeeinflusst");
        }
        private static async Task ShutdownFlushesPendingWrites()
        {
            var repo = new RecordingRepository { DelayMs = 100 };
            Harness h = await Harness.StartAsync(repository: repo, backgroundPersistence: true);
            try
            {
                // Reihenfolge: Hosted Services stoppen in umgekehrter Registrierungsreihenfolge – der Dienst, der die
                // Warteschlange leert, muss VOR dem Spieltakt registriert sein, damit er NACH ihm stoppt.
                List<string> services = h.App.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Select(s => s.GetType().Name).ToList();
                int drain = services.IndexOf("PersistenceDrainService"), loop = services.IndexOf("GameLoopService");
                Assert.IsTrue(drain >= 0 && loop >= 0, "beide Dienste registriert: " + string.Join(", ", services));
                Assert.IsTrue(drain < loop, "Warteschlangen-Dienst vor dem Spieltakt registriert (stoppt zuletzt): " + string.Join(", ", services));

                string cookie = await h.LoginAsync("Shutdown");
                Paintball.Net.Rooms.GameServer game = h.App.Services.GetRequiredService<Paintball.Net.Rooms.GameServer>();
                Paintball.Net.Accounts.AccountStore accounts = game.Accounts;
                string id = accounts.PlayerIdForSession(cookie.Substring(cookie.IndexOf('=') + 1));
                Assert.IsTrue(id != null, "Spieler per Dev-Login angelegt");
                for (int i = 0; i < 5; i++)
                    accounts.ApplyMatch(id, new Paintball.Net.Accounts.MatchSummary { Mode = "tdm", Map = "arena", Kills = i, XpGained = 100 });
                Assert.IsTrue(accounts.Queue.Pending > 0, "vor dem Stopp ist noch etwas offen (sonst prüft der Test nichts)");

                await h.App.StopAsync();

                Assert.AreEqual(5, repo.CallsFor(id).Count(c => c.Op == "match"), "alle 5 AddMatch-Aufrufe geschrieben");
                Assert.AreEqual(0, accounts.Queue.Pending, "nichts mehr offen");
                Assert.AreEqual(0L, accounts.Queue.Failures, "nichts verworfen");
                Assert.AreEqual(50, repo.Get(id).Coins, "letzter Stand gespeichert");
            }
            finally { await h.DisposeAsync(); }   // zweites StopAsync ist harmlos
        }
        private static async Task ShutdownWithOpenWebSocket()
        {
            var repo = new RecordingRepository { DelayMs = 100 };
            Harness h = await Harness.StartAsync(repository: repo, backgroundPersistence: true);
            try
            {
                string cookie = await h.LoginAsync("Offen");
                using ClientWebSocket ws = await h.ConnectAsync($"https://localhost:{h.HttpsPort}", cookie);
                await SendAsync(ws, new { t = "hello", input = "kbm", crossPlay = true });
                await ReceiveUntil(ws, "welcome");

                Paintball.Net.Accounts.AccountStore accounts = h.App.Services.GetRequiredService<Paintball.Net.Rooms.GameServer>().Accounts;
                string id = accounts.PlayerIdForSession(cookie.Substring(cookie.IndexOf('=') + 1));
                for (int i = 0; i < 5; i++)
                    accounts.ApplyMatch(id, new Paintball.Net.Accounts.MatchSummary { Mode = "tdm", Map = "arena", Kills = i, XpGained = 100 });
                Assert.IsTrue(accounts.Queue.Pending > 0, "vor dem Stopp ist noch etwas offen");

                // Client wie ein Browser: liest bis zum Close-Frame und bestätigt ihn.
                Task<(WebSocketCloseStatus? Status, string Reason)> closed = Task.Run(async () =>
                {
                    var buffer = new byte[65536];
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    while (true)
                    {
                        WebSocketReceiveResult r = await ws.ReceiveAsync(buffer, cts.Token);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch (WebSocketException) { }
                            return (r.CloseStatus, r.CloseStatusDescription);
                        }
                    }
                });

                var watch = System.Diagnostics.Stopwatch.StartNew();
                await h.App.StopAsync();
                watch.Stop();

                var (status, reason) = await closed;
                Console.WriteLine($"       (StopAsync mit offener Verbindung und 10 Aufträgen à 100 ms: {watch.Elapsed.TotalSeconds:F1} s)");
                Assert.AreEqual((WebSocketCloseStatus?)WebSocketCloseStatus.EndpointUnavailable, status, "Server schließt mit 1001");
                Assert.AreEqual("server_restart", reason, "Grund server_restart");
                Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(15), $"StopAsync zügig ({watch.Elapsed.TotalSeconds:F1} s)");
                Assert.AreEqual(5, repo.CallsFor(id).Count(c => c.Op == "match"), "alle 5 Matches geschrieben");
                Assert.AreEqual(0, accounts.Queue.Pending, "nichts mehr offen");
                Assert.AreEqual(0L, accounts.Queue.Failures, "nichts verworfen");
            }
            finally { await h.DisposeAsync(); }
        }

        private static async Task GameLoopStopWaitsForLoop()
        {
            var game = new Paintball.Net.Rooms.GameServer(new Paintball.Net.Rooms.ServerOptions(), AccountTests.NewStore());
            for (int i = 0; i < 20; i++)
            {
                var loop = new GameLoopService(game);
                await loop.StartAsync(CancellationToken.None);
                await Task.Delay(40);
                // Abgelaufenes Stopp-Budget des Hosts: BackgroundService.StopAsync würde sofort zurückkehren.
                await loop.StopAsync(new CancellationToken(true));
                Assert.IsTrue(loop.LoopExited, $"Takt-Schleife beendet, bevor StopAsync zurückkehrt (Lauf {i + 1})");
                loop.Dispose();
            }
        }

        private static async Task DevLoginDeletedDuringSignIn()
        {
            var repo = new WrappingRepository();
            await using Harness h = await Harness.StartAsync(repository: repo);
            await h.LoginAsync("Geist");                                    // legt an (kein RecordLogin)
            Paintball.Net.Accounts.AccountStore accounts = h.App.Services.GetRequiredService<Paintball.Net.Rooms.GameServer>().Accounts;
            repo.OnRecordLogin = pid => accounts.Delete(pid);              // DSGVO-Löschung mitten in der zweiten Anmeldung
            HttpResponseMessage res = await h.Http.GetAsync("/api/auth/dev?name=Geist");
            Assert.AreEqual(HttpStatusCode.Conflict, res.StatusCode, "409 statt 500");
            Assert.IsFalse(SetsSession(res), "keine Session für ein gelöschtes Konto");
        }
    }
}
