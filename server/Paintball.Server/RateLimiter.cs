using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Paintball.Server
{
    /// <summary>
    /// Festes Zeitfenster pro IP (Standard: 20 Anfragen pro Minute). Eine Instanz pro Server-Host,
    /// damit sich mehrere Hosts (z. B. Test-Harnesses) nicht gegenseitig begrenzen.
    /// Abgelaufene Einträge werden zeitbasiert entfernt, höchstens einmal pro Fenster.
    /// </summary>
    public sealed class RateLimiter
    {
        private readonly int _limit;
        private readonly TimeSpan _window;
        private readonly Func<DateTime> _clock;
        private readonly ConcurrentDictionary<string, (DateTime Start, int Count)> _hits = new();
        private readonly object _sweepLock = new();
        private DateTime _lastSweep;

        public RateLimiter(int limit = 20, TimeSpan? window = null, Func<DateTime> clock = null)
        {
            _limit = limit;
            _window = window ?? TimeSpan.FromMinutes(1);
            _clock = clock ?? (() => DateTime.UtcNow);
            _lastSweep = _clock();
        }

        /// <summary>Anzahl der gemerkten IPs (für Tests und Diagnose).</summary>
        public int Tracked => _hits.Count;

        /// <summary>Zählt eine Anfrage; true, wenn sie das Limit überschreitet.</summary>
        public bool Exceeded(string key)
        {
            DateTime now = _clock();
            Sweep(now);
            var entry = _hits.AddOrUpdate(key ?? "?", _ => (now, 1),
                (_, e) => now - e.Start >= _window ? (now, 1) : (e.Start, e.Count + 1));
            return entry.Count > _limit;
        }

        public bool Exceeded(HttpContext ctx) => Exceeded(ctx.Connection.RemoteIpAddress?.ToString());

        private void Sweep(DateTime now)
        {
            if (now - _lastSweep < _window) return;
            lock (_sweepLock)
            {
                if (now - _lastSweep < _window) return;
                _lastSweep = now;
                foreach (KeyValuePair<string, (DateTime Start, int Count)> kv in _hits)
                    if (now - kv.Value.Start >= _window) _hits.TryRemove(kv);
            }
        }
    }
}
