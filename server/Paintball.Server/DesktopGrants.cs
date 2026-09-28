using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Paintball.Server
{
    public enum RedeemStatus { Ok, NotFound, WrongVerifier, Malformed }

    public sealed class DesktopGrant
    {
        public string PlayerId;
        /// <summary>Google-Vorname als Namensvorschlag (nur bei Namensbedarf), sonst null.</summary>
        public string SuggestedName;
    }

    /// <summary>
    /// Desktop-Login nach RFC 8252 (Loopback-Rückgabe, Spec-Nachtrag „Loopback-Rückgabe“): Nach dem Google-Callback im
    /// Standardbrowser legt der Server hier einen Einmal-Code an (code → challenge/Spieler), den er per Redirect auf
    /// http://127.0.0.1:&lt;port&gt;/done an den lokalen Empfänger der App liefert. Die App löst mit code + ihrem verifier
    /// über /api/auth/desktop/redeem ein. Einmalig, 2 Minuten gültig, nach 5 Fehlversuchen gelöscht, nur im Arbeitsspeicher.
    /// Der Code – nicht die challenge – ist der Schlüssel: Eine challenge, die ein Angreifer dem Opfer unterschiebt (etwa in
    /// einem Phishing-Link), nützt ohne den Code nichts, und der Code verlässt den Rechner des Opfers nie – er geht per
    /// Server-Redirect ausschließlich an 127.0.0.1, erreicht also nur einen Prozess auf genau diesem Rechner.
    /// </summary>
    public sealed class DesktopGrantStore
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
        public const int MaxFailures = 5;

        private sealed class Entry
        {
            public string Challenge;
            public string PlayerId;
            public string SuggestedName;
            public DateTime ExpiresAt;
            public int Failures;
        }

        private readonly Dictionary<string, Entry> _grants = new(StringComparer.Ordinal);
        private readonly object _lock = new();
        private readonly Func<DateTime> _clock;
        private readonly int _maxGrants;

        /// <param name="clock">Nur für Tests.</param>
        /// <param name="maxGrants">Obergrenze gleichzeitig offener Grants (Schutz des Arbeitsspeichers).</param>
        public DesktopGrantStore(Func<DateTime> clock = null, int maxGrants = 10_000)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
            _maxGrants = maxGrants;
        }

        public int Count { get { lock (_lock) return _grants.Count; } }

        /// <summary>PKCE-Wert: genau 43 Zeichen base64url (32 Byte ohne Padding) – gilt für verifier, challenge und den Code.</summary>
        public static bool ValidPkceValue(string v) => v != null && Regex.IsMatch(v, @"\A[A-Za-z0-9\-_]{43}\z");

        private static string ChallengeOf(string verifier)
            => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string NewCode() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Dynamischer Port des lokalen RFC-8252-Empfängers der App: 1024–65535, keine führenden Nullen, kein Umgebendes.</summary>
        public static bool ValidPort(string s, out int port)
        {
            port = 0;
            if (string.IsNullOrEmpty(s) || !Regex.IsMatch(s, @"\A[1-9][0-9]{3,4}\z")) return false;
            int p = int.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);
            if (p < 1024 || p > 65535) return false;
            port = p;
            return true;
        }

        /// <summary>
        /// Legt bei gültiger challenge einen Grant an und gibt den neuen Einmal-Code zurück (null bei ungültiger challenge,
        /// fehlender playerId oder vollem Speicher). Anders als die challenge (kann vom Client kommen) ist der Code
        /// serverseitig zufällig – deshalb gibt es hier keine „erster gewinnt“-Regel mehr wie vor dem Loopback-Umbau.
        /// </summary>
        public string Add(string challenge, string playerId, string suggestedName)
        {
            if (!ValidPkceValue(challenge) || string.IsNullOrEmpty(playerId)) return null;
            DateTime now = _clock();
            lock (_lock)
            {
                SweepLocked(now);
                if (_grants.Count >= _maxGrants) return null;
                string code;
                do { code = NewCode(); } while (_grants.ContainsKey(code)); // praktisch nie mehr als ein Versuch
                _grants[code] = new Entry { Challenge = challenge, PlayerId = playerId, SuggestedName = suggestedName, ExpiresAt = now + Lifetime };
                return code;
            }
        }

        /// <summary>
        /// NotFound: kein (gültiger) Grant zu diesem Code – unbekannt, abgelaufen oder schon verbraucht; zählt nirgends als
        /// Fehlversuch. WrongVerifier: Grant da, verifier passt nicht zur challenge (zählt als Fehlversuch am Grant).
        /// Malformed: Format von Code oder verifier falsch (zählt nicht).
        /// </summary>
        public RedeemStatus TryRedeem(string code, string verifier, out DesktopGrant grant)
        {
            grant = null;
            if (!ValidPkceValue(code) || !ValidPkceValue(verifier)) return RedeemStatus.Malformed;
            DateTime now = _clock();
            lock (_lock)
            {
                if (!_grants.TryGetValue(code, out Entry e)) return RedeemStatus.NotFound;
                if (now >= e.ExpiresAt)
                {
                    _grants.Remove(code);
                    return RedeemStatus.NotFound;
                }
                byte[] expected = Encoding.ASCII.GetBytes(e.Challenge);
                byte[] actual = Encoding.ASCII.GetBytes(ChallengeOf(verifier));
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    if (++e.Failures >= MaxFailures) _grants.Remove(code);
                    return RedeemStatus.WrongVerifier;
                }
                _grants.Remove(code);
                grant = new DesktopGrant { PlayerId = e.PlayerId, SuggestedName = e.SuggestedName };
                return RedeemStatus.Ok;
            }
        }

        private void SweepLocked(DateTime now)
        {
            List<string> expired = null;
            foreach (KeyValuePair<string, Entry> kv in _grants)
                if (now >= kv.Value.ExpiresAt) (expired ??= new List<string>()).Add(kv.Key);
            if (expired != null)
                foreach (string k in expired) _grants.Remove(k);
        }
    }
}
