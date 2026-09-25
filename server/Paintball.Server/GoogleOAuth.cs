using System.Threading;
using System.Threading.Tasks;

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
        Task<GoogleUser> ExchangeAsync(string code, string redirectUri, CancellationToken ct);
    }

    public static class GoogleAuthApi
    {
        /// <summary>Raumcode für die Rückleitung nach /play?join=…; \z statt $, damit ein angehängtes "\n" nicht durchrutscht.</summary>
        public static string ValidJoin(string join)
            => !string.IsNullOrEmpty(join) && System.Text.RegularExpressions.Regex.IsMatch(join, @"\A[A-Z0-9]{1,12}\z") ? join : null;
    }
}
