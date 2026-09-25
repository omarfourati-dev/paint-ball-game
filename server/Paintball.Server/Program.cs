using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace Paintball.Server
{
    /// <summary>
    /// Startpunkt des WSS-Gameservers.
    ///   dotnet run --project server/Paintball.Server
    ///   → https://localhost:5443  (Browser-Client, wss://localhost:5443/ws)
    /// Optionen: --port 5443 --http-port 5080 --public --origin https://example.com
    ///   --behind-proxy  nur HTTP auf --http-port, TLS terminiert ein Reverse-Proxy (Produktion hinter Caddy)
    ///   --dev-login     aktiviert /api/auth/dev (nur lokale Entwicklung, nie in Produktion)
    /// Umgebung: DATABASE_URL, PUBLIC_URL, GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            var options = new ServerHostOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "--port": options.HttpsPort = int.Parse(next); i++; break;
                    case "--http-port": options.HttpPort = int.Parse(next); i++; break;
                    case "--data": i++; Console.WriteLine("[Start] --data ist veraltet und ohne Wirkung (Konten liegen in Postgres)"); break;
                    case "--web": options.WebRoot = next; i++; break;
                    case "--origin": options.AllowedOrigins.Add(next); i++; break;
                    case "--public": options.ListenAnyIp = true; break;
                    case "--behind-proxy": options.BehindProxy = true; break;
                    case "--dev-login": options.DevLogin = true; break;
                }
            }

            options.DatabaseUrl ??= Environment.GetEnvironmentVariable("DATABASE_URL");
            options.PublicUrl ??= Environment.GetEnvironmentVariable("PUBLIC_URL");
            options.GoogleClientId ??= Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
            options.GoogleClientSecret ??= Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");

            WebApplication app = ServerHost.Build(args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray(), options);
            Console.WriteLine(options.BehindProxy
                ? $"Paint-Ball Server {ServerHost.Version} – http://0.0.0.0:{options.HttpPort} hinter Reverse-Proxy (WS: /ws)"
                : $"Paint-Ball Server {ServerHost.Version} – https://localhost:{options.HttpsPort} (WSS: /ws)");
            app.Run();
        }
    }
}
