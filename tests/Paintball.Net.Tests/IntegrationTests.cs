using System;
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
            r.RunAsync("Auth: /api/me 401 ohne Session, needsName nach erstem Login, Namenswahl mit taken/invalid", MeAndName);
            r.RunAsync("Auth: /ws ohne Cookie 401, ohne Namen 403", WsRequiresSession);
            r.RunAsync("Auth: Abmelden macht Session ungültig", Logout);
            r.RunAsync("Auth: POST ohne gleiche Origin wird abgelehnt", CsrfOrigin);
        }

        private static string HarnessWebRoot;

        private sealed class Harness : IAsyncDisposable
        {
            public WebApplication App;
            public int HttpsPort;
            public int HttpPort;
            public HttpClient Http;

            public static async Task<Harness> StartAsync(bool devLogin = true)
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
                    DataDirectory = AccountTests.TempDir(),
                    WebRoot = web,
                    DevLogin = devLogin,
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

            /// <summary>Meldet sich per Dev-Login an und gibt den Cookie-Header "pb_session=…" zurück.</summary>
            public async Task<string> LoginAsync(string name)
            {
                HttpResponseMessage res = await Http.GetAsync("/api/auth/dev?name=" + Uri.EscapeDataString(name));
                Assert.AreEqual(HttpStatusCode.Found, res.StatusCode, "Dev-Login leitet weiter");
                string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_session=", StringComparison.Ordinal));
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
                DataDirectory = AccountTests.TempDir(),
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
                string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_session=", StringComparison.Ordinal));

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
            string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_session="));
            string lower = set.ToLowerInvariant();
            Assert.IsTrue(lower.Contains("httponly") && lower.Contains("secure") && lower.Contains("samesite=lax") && lower.Contains("path=/"), "Cookie-Attribute");
            Assert.AreEqual("/play", res.Headers.Location.OriginalString, "zurück ins Spiel");

            await using Harness prod = await Harness.StartAsync(devLogin: false);
            Assert.AreEqual(HttpStatusCode.NotFound, (await prod.Http.GetAsync("/api/auth/dev?name=X")).StatusCode, "ohne Flag 404");
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
            bool rejected = false;
            try { using ClientWebSocket ws = await h.ConnectAsync($"https://localhost:{h.HttpsPort}"); }
            catch (WebSocketException) { rejected = true; }
            Assert.IsTrue(rejected, "ohne Cookie abgelehnt");

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
    }
}
