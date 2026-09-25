using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Paintball.Server
{
    /// <summary>
    /// Google-AdSense-Konfiguration aus der Umgebung. Aus, solange keine gültige Publisher-ID gesetzt ist:
    /// ADSENSE_CLIENT (ca-pub-16 Ziffern), ADSENSE_SLOT_LANDING/_LOBBY/_RESULTS (nur Ziffern), ADSENSE_INTERSTITIAL_EVERY (Standard 3).
    /// Die Werte sind öffentlich (stehen ohnehin im Seitenquelltext), daher GitHub-Variables statt Secrets.
    /// </summary>
    public sealed class AdsConfig
    {
        public const int DefaultInterstitialEvery = 3;

        /// <summary>Selbst gehostete Umami-Instanz (Reichweitenmessung, Teil 2 der Analyse-Spec) – unabhängig von Werbung immer in
        /// script-src (Tracker-Skript) und connect-src (Sendeaufruf an /api/e) erlaubt.</summary>
        public const string AnalyticsHost = "https://analytics.omarfourati.de";

        /// <summary>
        /// Skripte: nur die konkreten Hosts von AdSense, Consent-Nachricht (CMP) und Betrugserkennung – keine breiten Google-Wildcards,
        /// damit nicht jedes Skript unter google.com/gstatic.com auf der Seite laufen darf.
        /// </summary>
        public static readonly string[] ScriptSources =
        {
            "https://pagead2.googlesyndication.com", "https://fundingchoicesmessages.google.com", "https://www.google.com",
            "https://tpc.googlesyndication.com", "https://*.adtrafficquality.google"
        };

        /// <summary>Anzeigen-Frames, Bilder und Messaufrufe: breitere Google-Domains (ohne Doppelungen, die Wildcards decken Unterhosts ab).</summary>
        public static readonly string[] AdSources =
        {
            "https://*.googlesyndication.com", "https://*.doubleclick.net", "https://*.google.com", "https://*.gstatic.com",
            "https://*.adtrafficquality.google"
        };

        /// <summary>Zusätzlich nur für Bilder (Consent-/Mess-Pixel über die Länder-Domain).</summary>
        public static readonly string[] ImageOnlySources = { "https://www.google.de" };

        private static readonly Regex ClientPattern = new("^ca-pub-[0-9]{16}$", RegexOptions.CultureInvariant);
        private static readonly Regex SlotPattern = new("^[0-9]+$", RegexOptions.CultureInvariant);

        public static readonly AdsConfig Disabled = new(null, new Dictionary<string, string>(), DefaultInterstitialEvery);

        public string Client { get; }
        /// <summary>Nur gültige Slots: landing, lobby, results → Ziffern-ID.</summary>
        public IReadOnlyDictionary<string, string> Slots { get; }
        public int InterstitialEvery { get; }
        public bool Enabled => Client != null;
        /// <summary>Publisher-ID für ads.txt: „pub-…“ ohne „ca-“.</summary>
        public string PublisherId => Client?.Substring(3);

        private AdsConfig(string client, Dictionary<string, string> slots, int every)
        {
            Client = client;
            Slots = slots;
            InterstitialEvery = every;
        }

        public static AdsConfig FromEnvironment(Func<string, string> env)
        {
            string client = env("ADSENSE_CLIENT")?.Trim();
            if (string.IsNullOrEmpty(client) || !ClientPattern.IsMatch(client))
            {
                if (!string.IsNullOrEmpty(client)) Console.WriteLine("[Werbung] ADSENSE_CLIENT hat kein gültiges Format (ca-pub-16 Ziffern) – Werbung bleibt aus");
                return Disabled;
            }
            var slots = new Dictionary<string, string>();
            foreach (var (key, name) in new[] { ("landing", "ADSENSE_SLOT_LANDING"), ("lobby", "ADSENSE_SLOT_LOBBY"), ("results", "ADSENSE_SLOT_RESULTS") })
            {
                string v = env(name)?.Trim();
                if (!string.IsNullOrEmpty(v) && SlotPattern.IsMatch(v)) slots[key] = v;
            }
            int every = int.TryParse(env("ADSENSE_INTERSTITIAL_EVERY")?.Trim(), out int n) && n >= 1 && n <= 100 ? n : DefaultInterstitialEvery;
            return new AdsConfig(client, slots, every);
        }

        public string AdsTxt => $"google.com, {PublisherId}, DIRECT, f08c47fec0942fa0\n";

        /// <summary>AdSense prüft die Herkunft der Seite; ohne Werbung bleibt es beim strengen no-referrer.</summary>
        public string ReferrerPolicy => Enabled ? "strict-origin-when-cross-origin" : "no-referrer";

        /// <summary>CSP der Seite: unverändert ohne Werbung, sonst um die Google-Domains erweitert.</summary>
        public string ContentSecurityPolicy()
        {
            if (!Enabled)
                return $"default-src 'self'; script-src 'self' {AnalyticsHost}; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
                       $"connect-src 'self' wss: {AnalyticsHost}; font-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
            string scripts = string.Join(" ", ScriptSources);
            string g = string.Join(" ", AdSources);
            string img = string.Join(" ", AdSources.Concat(ImageOnlySources));
            return $"default-src 'self'; script-src 'self' {scripts} {AnalyticsHost}; style-src 'self' 'unsafe-inline'; img-src 'self' data: {img}; " +
                   $"connect-src 'self' wss: {g} {AnalyticsHost}; frame-src {g}; font-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
        }
    }
}
