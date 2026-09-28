using System;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Paintball.Net.Accounts;

namespace Paintball.Server
{
    /// <summary>POST /api/auth/desktop/redeem: Die Desktop-App tauscht ihren verifier gegen die normale Sitzung (Spec §2.4).</summary>
    public static class DesktopAuthApi
    {
        private const long MaxBodyBytes = 1024;

        /// <param name="limiter">Eigene Instanz (60/min/IP): Die App fragt alle 2 s nach.</param>
        public static void Map(WebApplication app, AccountStore accounts, DesktopGrantStore grants, RateLimiter limiter)
        {
            app.MapPost("/api/auth/desktop/redeem", async (HttpContext ctx) =>
            {
                ctx.Response.Headers.CacheControl = "no-store"; // auch für 429 und Fehler
                if (limiter.Exceeded(ctx)) return Results.StatusCode(429);
                // Bewusst ohne Origin-Prüfung: Die App hat keine Web-Origin, der Schutz liegt allein im verifier.
                // application/json ist Pflicht: Ein fremdes HTML-Formular kann diesen Typ nicht senden (kein Login-CSRF per Formular).
                if (!IsJson(ctx.Request.ContentType)) return Results.StatusCode(415);
                if (ctx.Request.ContentLength > MaxBodyBytes) return Invalid();
                IHttpMaxRequestBodySizeFeature limit = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit != null && !limit.IsReadOnly) limit.MaxRequestBodySize = MaxBodyBytes;
                string challenge, verifier;
                try
                {
                    using JsonDocument doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                    challenge = doc.RootElement.GetProperty("challenge").GetString();
                    verifier = doc.RootElement.GetProperty("verifier").GetString();
                }
                catch (Exception) { return Invalid(); }

                switch (grants.TryRedeem(challenge, verifier, out DesktopGrant grant))
                {
                    case RedeemStatus.Pending: return Results.Json(new { status = "pending" }, statusCode: 202);
                    case RedeemStatus.WrongVerifier: return Results.Json(new { error = "invalid" }, statusCode: 401);
                    case RedeemStatus.Malformed: return Invalid();
                }

                string token;
                try { token = accounts.CreateSession(grant.PlayerId); }
                catch (ArgumentException) { return Results.Json(new { error = "gone" }, statusCode: 409); } // Konto inzwischen gelöscht
                accounts.EndSession(AuthApi.SessionToken(ctx)); // Re-Login in der App: altes Token nicht gültig lassen
                AuthApi.SetSession(ctx, accounts, token);
                var suggestOpts = new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" };
                if (!string.IsNullOrEmpty(grant.SuggestedName))
                {
                    suggestOpts.MaxAge = TimeSpan.FromMinutes(30);
                    ctx.Response.Cookies.Append("pb_suggest", grant.SuggestedName, suggestOpts);
                }
                else ctx.Response.Cookies.Delete("pb_suggest", suggestOpts);
                return Results.Json(new { status = "ok" });
            });
        }

        internal static bool IsJson(string contentType)
            => contentType != null && contentType.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase);

        private static IResult Invalid() => Results.Json(new { error = "invalid" }, statusCode: 400);
    }
}
