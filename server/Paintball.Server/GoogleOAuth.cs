using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Paintball.Net.Accounts;

namespace Paintball.Server
{
    public sealed class GoogleUser
    {
        public string Sub;
        public string Email;
        public string GivenName;
    }

    /// <summary>Tauscht einen OAuth-Code gegen die Google-Nutzerdaten (Fake in Tests).</summary>
    public interface IGoogleOAuthClient
    {
        Task<GoogleUser> ExchangeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct);
    }

    /// <summary>Echter Google-Client: Code → Token (oauth2.googleapis.com, mit PKCE-Verifier), dann userinfo (OpenID).</summary>
    public sealed class GoogleOAuthClient : IGoogleOAuthClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private readonly string _clientId, _clientSecret;

        public GoogleOAuthClient(string clientId, string clientSecret) { _clientId = clientId; _clientSecret = clientSecret; }

        public async Task<GoogleUser> ExchangeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct)
        {
            using var tokenRes = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code, ["client_id"] = _clientId, ["client_secret"] = _clientSecret,
                ["redirect_uri"] = redirectUri, ["grant_type"] = "authorization_code", ["code_verifier"] = codeVerifier
            }), ct);
            if (!tokenRes.IsSuccessStatusCode) throw new InvalidOperationException($"Google-Token-Tausch fehlgeschlagen ({(int)tokenRes.StatusCode})");
            using JsonDocument tokenDoc = JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync(ct));
            string accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

            using var req = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var infoRes = await Http.SendAsync(req, ct);
            if (!infoRes.IsSuccessStatusCode) throw new InvalidOperationException($"Google-userinfo fehlgeschlagen ({(int)infoRes.StatusCode})");
            using JsonDocument infoDoc = JsonDocument.Parse(await infoRes.Content.ReadAsStringAsync(ct));
            JsonElement u = infoDoc.RootElement;
            string sub = u.TryGetProperty("sub", out JsonElement s) ? s.GetString() : null;
            string email = u.TryGetProperty("email", out JsonElement e) ? e.GetString() : null;
            if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email)) throw new InvalidOperationException("Google-Antwort ohne sub/email");
            return new GoogleUser { Sub = sub, Email = email, GivenName = u.TryGetProperty("given_name", out JsonElement g) ? g.GetString() : null };
        }
    }

    /// <summary>Google-Login (Authorization-Code-Flow): /api/auth/google → Google → /api/auth/google/callback.</summary>
    public static class GoogleAuthApi
    {
        private const string OAuthCookie = "pb_oauth";

        /// <summary>Raumcode für die Rückleitung nach /play?join=…; \z statt $, damit ein angehängtes "\n" nicht durchrutscht.</summary>
        public static string ValidJoin(string join)
            => !string.IsNullOrEmpty(join) && Regex.IsMatch(join, @"\A[A-Z0-9]{1,12}\z") ? join : null;

        /// <summary>Aus PUBLIC_URL, falls gesetzt (hinter dem Proxy), sonst aus dem Request.</summary>
        public static string RedirectUri(ServerHostOptions o, HttpRequest req)
            => (string.IsNullOrWhiteSpace(o.PublicUrl) ? $"{req.Scheme}://{req.Host}" : o.PublicUrl.TrimEnd('/')) + "/api/auth/google/callback";

        private static bool Configured(ServerHostOptions o) => !string.IsNullOrEmpty(o.GoogleClientId) && !string.IsNullOrEmpty(o.GoogleClientSecret);

        private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static bool ValidVerifier(string v) => v != null && v.Length == 43 && Regex.IsMatch(v, @"\A[A-Za-z0-9\-_]{43}\z");

        /// <param name="limiter">Dieselbe Instanz wie in AuthApi.Map (20/min/IP pro Host).</param>
        public static void Map(WebApplication app, AccountStore accounts, ServerHostOptions options, RateLimiter limiter)
        {
            IGoogleOAuthClient google = options.Google ?? (Configured(options) ? new GoogleOAuthClient(options.GoogleClientId, options.GoogleClientSecret) : null);
            var cookieOpts = new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/api/auth", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true };

            app.MapGet("/api/auth/google", (HttpContext ctx) =>
            {
                ctx.Response.Headers.CacheControl = "no-store"; // gilt auch für 429 (kein Zwischenspeichern von Auth-Antworten)
                if (limiter.Exceeded(ctx)) return Results.StatusCode(429);
                if (!Configured(options) || google == null) return Results.Redirect("/play?auth_error=not_configured");
                string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                string join = ValidJoin(ctx.Request.Query["join"].ToString()) ?? string.Empty;
                string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
                string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
                ctx.Response.Cookies.Append(OAuthCookie, state + "." + join + "." + verifier, cookieOpts);
                string url = "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&",
                    "client_id=" + Uri.EscapeDataString(options.GoogleClientId),
                    "redirect_uri=" + Uri.EscapeDataString(RedirectUri(options, ctx.Request)),
                    "response_type=code",
                    "scope=" + Uri.EscapeDataString("openid email profile"),
                    "state=" + state,
                    "code_challenge=" + challenge,
                    "code_challenge_method=S256",
                    "prompt=select_account");
                return Results.Redirect(url);
            });

            app.MapGet("/api/auth/google/callback", async (HttpContext ctx) =>
            {
                ctx.Response.Headers.CacheControl = "no-store"; // gilt auch für 429 (kein Zwischenspeichern von Auth-Antworten)
                if (limiter.Exceeded(ctx)) return Results.StatusCode(429);
                string stored = ctx.Request.Cookies.TryGetValue(OAuthCookie, out string v) ? v : null;
                ctx.Response.Cookies.Delete(OAuthCookie, cookieOpts); // state gilt genau einmal
                string state = ctx.Request.Query["state"].ToString(), code = ctx.Request.Query["code"].ToString();
                string[] parts = stored?.Split('.');
                string storedState = parts?.Length == 3 ? parts[0] : null;
                string join = parts?.Length == 3 ? ValidJoin(parts[1]) : null;
                string verifier = parts?.Length == 3 ? parts[2] : null;
                if (storedState == null || !ValidVerifier(verifier) || string.IsNullOrEmpty(state) ||
                    !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(storedState), Encoding.ASCII.GetBytes(state)))
                    return Results.Redirect("/play?auth_error=invalid_state");
                if (ctx.Request.Query["error"].ToString() == "access_denied") return Results.Redirect("/play?auth_error=cancelled");
                if (string.IsNullOrEmpty(code) || google == null) return Results.Redirect("/play?auth_error=oauth_failed");
                try
                {
                    GoogleUser user = await google.ExchangeAsync(code, RedirectUri(options, ctx.Request), verifier, ctx.RequestAborted);
                    SignInResult s = accounts.SignIn(user.Sub, user.Email);
                    accounts.EndSession(AuthApi.SessionToken(ctx)); // Re-Login: altes Token nicht gültig lassen
                    AuthApi.SetSession(ctx, accounts.CreateSession(s.PlayerId));
                    if (s.NeedsName && !string.IsNullOrEmpty(user.GivenName))
                        ctx.Response.Cookies.Append("pb_suggest", user.GivenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromMinutes(30) });
                    else
                        ctx.Response.Cookies.Delete("pb_suggest", new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" });
                    return Results.Redirect(join != null ? "/play?join=" + join : "/play");
                }
                catch (Exception ex)
                {
                    // Nur der Typname: Message könnte Antwortinhalte von Google enthalten.
                    Console.Error.WriteLine("[Auth] Google-Anmeldung fehlgeschlagen: " + ex.GetType().Name);
                    return Results.Redirect("/play?auth_error=oauth_failed");
                }
            });
        }
    }
}
