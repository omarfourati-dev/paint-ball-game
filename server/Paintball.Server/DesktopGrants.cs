using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Paintball.Server
{
    public enum RedeemStatus { Ok, Pending, WrongVerifier, Malformed }

    public sealed class DesktopGrant
    {
        public string PlayerId;
        /// <summary>Google-Vorname als Namensvorschlag (nur bei Namensbedarf), sonst null.</summary>
        public string SuggestedName;
    }

    /// <summary>
    /// Desktop-Login (Spec §2): Nach dem Google-Callback im Standardbrowser liegt hier challenge → Spieler, bis die App mit dem
    /// passenden verifier einlöst. Einmalig, 2 Minuten gültig, nach 5 Fehlversuchen gelöscht, nur im Arbeitsspeicher.
    /// </summary>
    public sealed class DesktopGrantStore
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
        public const int MaxFailures = 5;

        private sealed class Entry
        {
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

        /// <summary>PKCE-Wert: genau 43 Zeichen base64url (32 Byte ohne Padding) – gilt für verifier und challenge.</summary>
        public static bool ValidPkceValue(string v) => v != null && Regex.IsMatch(v, @"\A[A-Za-z0-9\-_]{43}\z");

        public static string ChallengeOf(string verifier)
            => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>false bei ungültiger challenge, wenn für sie schon ein Grant liegt (der erste gewinnt) oder der Speicher voll ist.</summary>
        public bool Add(string challenge, string playerId, string suggestedName)
        {
            if (!ValidPkceValue(challenge) || string.IsNullOrEmpty(playerId)) return false;
            DateTime now = _clock();
            lock (_lock)
            {
                SweepLocked(now);
                if (_grants.ContainsKey(challenge) || _grants.Count >= _maxGrants) return false;
                _grants[challenge] = new Entry { PlayerId = playerId, SuggestedName = suggestedName, ExpiresAt = now + Lifetime };
                return true;
            }
        }

        /// <summary>
        /// Pending: kein (gültiger) Grant zur challenge – noch nicht angemeldet, abgelaufen oder verbraucht.
        /// WrongVerifier: Grant da, verifier passt nicht (zählt als Fehlversuch). Malformed: Format falsch (zählt nicht).
        /// </summary>
        public RedeemStatus TryRedeem(string challenge, string verifier, out DesktopGrant grant)
        {
            grant = null;
            if (!ValidPkceValue(challenge) || !ValidPkceValue(verifier)) return RedeemStatus.Malformed;
            byte[] expected = Encoding.ASCII.GetBytes(challenge);
            byte[] actual = Encoding.ASCII.GetBytes(ChallengeOf(verifier));
            DateTime now = _clock();
            lock (_lock)
            {
                if (!_grants.TryGetValue(challenge, out Entry e)) return RedeemStatus.Pending;
                if (now >= e.ExpiresAt)
                {
                    _grants.Remove(challenge);
                    return RedeemStatus.Pending;
                }
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    if (++e.Failures >= MaxFailures) _grants.Remove(challenge);
                    return RedeemStatus.WrongVerifier;
                }
                _grants.Remove(challenge);
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
