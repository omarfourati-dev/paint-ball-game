using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Paintball.Core.Progression;
using Paintball.Net.Accounts;
using Paintball.Net.Rooms;

namespace Paintball.Server
{
    /// <summary>Session-Cookie, /api/me, Namenswahl, Abmelden, Dev-Login (Google-OAuth: siehe GoogleAuthApi).</summary>
    public static class AuthApi
    {
        public const string SessionCookie = "pb_session";

        /// <summary>Obergrenze für den JSON-Body von /api/me/name.</summary>
        private const long MaxNameBodyBytes = 1024;

        public static string SessionToken(HttpContext ctx)
            => ctx.Request.Cookies.TryGetValue(SessionCookie, out string v) ? v : null;

        public static void SetSession(HttpContext ctx, string token)
            => ctx.Response.Cookies.Append(SessionCookie, token, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/",
                MaxAge = AccountStore.SessionLifetime, IsEssential = true
            });

        public static void ClearSession(HttpContext ctx)
            => ctx.Response.Cookies.Delete(SessionCookie, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" });

        /// <summary>CSRF-Schutz für ändernde Requests: Origin muss zur eigenen Herkunft passen.</summary>
        public static bool SameOrigin(HttpContext ctx)
        {
            string origin = ctx.Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out Uri uri)) return false;
            return string.Equals(uri.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
        }

        private static string PlayerId(HttpContext ctx, AccountStore accounts) => accounts.PlayerIdForSession(SessionToken(ctx));

        /// <summary>Persönliche Antworten nie zwischenspeichern (Browser, Proxys).</summary>
        private static void NoStore(HttpContext ctx) => ctx.Response.Headers.CacheControl = "no-store";

        /// <param name="limiter">Pro Host eine Instanz (20/min/IP); auch für die Google-Routen (Task 6).</param>
        public static void Map(WebApplication app, GameServer game, AccountStore accounts, ServerHostOptions options, RateLimiter limiter)
        {
            app.MapGet("/api/me", (HttpContext ctx) =>
            {
                NoStore(ctx);
                string id = PlayerId(ctx, accounts);
                if (id == null) return Results.Unauthorized();
                PlayerAccount a = accounts.GetAccount(id);
                if (a == null) return Results.Unauthorized();
                bool needsName = accounts.NeedsName(id);
                return Results.Json(new
                {
                    id, name = needsName ? null : a.DisplayName, needsName,
                    suggestedName = needsName ? PendingSuggestion(ctx) : null,
                    level = a.Level, mmr = a.Mmr
                });
            });

            app.MapPost("/api/me/name", async (HttpContext ctx) =>
            {
                NoStore(ctx);
                if (limiter.Exceeded(ctx)) return Results.StatusCode(429);
                if (!SameOrigin(ctx)) return Results.StatusCode(403);
                string id = PlayerId(ctx, accounts);
                if (id == null) return Results.Unauthorized();
                if (ctx.Request.ContentLength > MaxNameBodyBytes) return Results.Json(new { error = "invalid" }, statusCode: 400);
                IHttpMaxRequestBodySizeFeature limit = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit != null && !limit.IsReadOnly) limit.MaxRequestBodySize = MaxNameBodyBytes;
                string name;
                try
                {
                    using JsonDocument doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                    name = doc.RootElement.GetProperty("name").GetString();
                }
                catch (Exception) { return Results.Json(new { error = "invalid" }, statusCode: 400); }
                NameResult r = accounts.SetName(id, name);
                if (r == NameResult.Taken) return Results.Json(new { error = "taken" }, statusCode: 409);
                if (r == NameResult.Invalid) return Results.Json(new { error = "invalid" }, statusCode: 400);
                ctx.Response.Cookies.Delete("pb_suggest", new CookieOptions { Path = "/" });
                game.Renamed(id);
                return Results.Json(new { name = accounts.GetAccount(id)?.DisplayName });
            });

            app.MapPost("/api/auth/logout", (HttpContext ctx) =>
            {
                if (limiter.Exceeded(ctx)) return Results.StatusCode(429);
                if (!SameOrigin(ctx)) return Results.StatusCode(403);
                string token = SessionToken(ctx);
                string id = accounts.PlayerIdForSession(token);
                accounts.EndSession(token);
                ClearSession(ctx);
                if (id != null) game.KickAccount(id, "logout");
                return Results.NoContent();
            });

            app.MapGet("/api/me/export", (HttpContext ctx) =>
            {
                NoStore(ctx);
                string id = PlayerId(ctx, accounts);
                return id == null ? Results.Unauthorized() : Results.Text(accounts.Export(id), "application/json");
            });

            app.MapDelete("/api/me", (HttpContext ctx) =>
            {
                if (!SameOrigin(ctx)) return Results.StatusCode(403);
                string id = PlayerId(ctx, accounts);
                if (id == null) return Results.Unauthorized();
                // Erst löschen (Sessions ungültig), dann kicken: kein /ws-Handshake mehr dazwischen.
                accounts.Delete(id);
                game.KickAccount(id, "deleted");
                ClearSession(ctx);
                return Results.NoContent();
            });

            // Bewusst ohne Rate-Limit: existiert nur mit --dev-login (nie in Produktion), die Tests melden sich darüber oft an.
            app.MapGet("/api/auth/dev", (HttpContext ctx) =>
            {
                if (!options.DevLogin) return Results.NotFound();
                accounts.EndSession(SessionToken(ctx)); // Re-Login: altes Token nicht gültig lassen
                string name = ctx.Request.Query["name"].ToString();
                string sub = "dev:" + (string.IsNullOrEmpty(name) ? Guid.NewGuid().ToString("N") : name.ToLowerInvariant());
                SignInResult s = accounts.SignIn(sub, "dev@localhost");
                if (s.NeedsName && !string.IsNullOrEmpty(name)) accounts.SetName(s.PlayerId, name);
                SetSession(ctx, accounts.CreateSession(s.PlayerId));
                string join = ctx.Request.Query["join"].ToString();
                return Results.Redirect(GoogleAuthApi.ValidJoin(join) != null ? "/play?join=" + join : "/play");
            });
        }

        /// <summary>Namensvorschlag aus dem Google-Vornamen, nach dem Callback kurz im Cookie pb_suggest.</summary>
        private static string PendingSuggestion(HttpContext ctx)
            => ctx.Request.Cookies.TryGetValue("pb_suggest", out string v) ? AccountStore.SuggestName(v) : string.Empty;
    }
}
