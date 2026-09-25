using System;
using System.Collections.Generic;
using Paintball.Net.Accounts;

namespace Paintball.Net.Tests
{
    /// <summary>
    /// Umhüllt ein <see cref="IPlayerRepository"/> für Tests: zählt <see cref="TopByMmr"/>-Aufrufe und lässt
    /// <see cref="SaveProgress"/>/<see cref="AddMatch"/> für eine bestimmte Spieler-Id auf Wunsch scheitern.
    /// </summary>
    internal sealed class WrappingRepository : IPlayerRepository
    {
        private readonly IPlayerRepository _inner;
        public int TopByMmrCalls;
        public string FailWritesFor;
        /// <summary>Verzögert jedes <see cref="Get"/> (weitet Wettläufe beim ersten Laden auf).</summary>
        public int GetDelayMs;
        private int _getCalls;
        public int GetCalls => System.Threading.Volatile.Read(ref _getCalls);

        public WrappingRepository(IPlayerRepository inner = null) { _inner = inner ?? new InMemoryPlayerRepository(); }

        private void MaybeFail(string playerId)
        {
            if (FailWritesFor != null && playerId == FailWritesFor) throw new InvalidOperationException("Datenbank nicht erreichbar (Test)");
        }

        public PlayerRecord FindBySub(string googleSub) => _inner.FindBySub(googleSub);
        public PlayerRecord Get(string playerId)
        {
            System.Threading.Interlocked.Increment(ref _getCalls);
            if (GetDelayMs > 0) System.Threading.Thread.Sleep(GetDelayMs);
            return _inner.Get(playerId);
        }
        public PlayerRecord Create(string googleSub, string email) => _inner.Create(googleSub, email);
        public void RecordLogin(string playerId, string email, DateTime when) => _inner.RecordLogin(playerId, email, when);
        public void SaveProgress(PlayerRecord player) { MaybeFail(player?.Id); _inner.SaveProgress(player); }
        public NameResult TrySetName(string playerId, string name) => _inner.TrySetName(playerId, name);
        public void AddMatch(string playerId, MatchRecord match) { MaybeFail(playerId); _inner.AddMatch(playerId, match); }
        public IReadOnlyList<MatchRecord> RecentMatches(string playerId, int limit) => _inner.RecentMatches(playerId, limit);
        public IReadOnlyList<PlayerRecord> TopByMmr(int limit) { TopByMmrCalls++; return _inner.TopByMmr(limit); }
        public int Count() => _inner.Count();
        public bool Delete(string playerId) => _inner.Delete(playerId);
        public void CreateSession(string tokenHash, string playerId, DateTime expiresAt) => _inner.CreateSession(tokenHash, playerId, expiresAt);
        public string PlayerIdForSession(string tokenHash, DateTime now) => _inner.PlayerIdForSession(tokenHash, now);
        public void DeleteSession(string tokenHash) => _inner.DeleteSession(tokenHash);
        public void DeleteSessionsOf(string playerId) => _inner.DeleteSessionsOf(playerId);
        public int DeleteExpiredSessions(DateTime now) => _inner.DeleteExpiredSessions(now);
    }

    /// <summary>
    /// Meldet jeden Repository-Aufruf, bei dem der aufrufende Thread die Sperre des <see cref="AccountStore"/> hält
    /// (<see cref="LockHeld"/> fragt das ab). So lässt sich prüfen, dass der Store kein Datenbank-I/O unter der Sperre macht.
    /// </summary>
    internal sealed class LockProbeRepository : IPlayerRepository
    {
        private readonly IPlayerRepository _inner = new InMemoryPlayerRepository();
        private readonly object _gate = new();
        private readonly List<string> _violations = new();
        public Func<bool> LockHeld = () => false;

        public List<string> Violations { get { lock (_gate) return new List<string>(_violations); } }

        private void Probe(string op)
        {
            if (LockHeld()) lock (_gate) _violations.Add(op);
        }

        public PlayerRecord FindBySub(string googleSub) { Probe("FindBySub"); return _inner.FindBySub(googleSub); }
        public PlayerRecord Get(string playerId) { Probe("Get"); return _inner.Get(playerId); }
        public PlayerRecord Create(string googleSub, string email) { Probe("Create"); return _inner.Create(googleSub, email); }
        public void RecordLogin(string playerId, string email, DateTime when) { Probe("RecordLogin"); _inner.RecordLogin(playerId, email, when); }
        public void SaveProgress(PlayerRecord player) { Probe("SaveProgress"); _inner.SaveProgress(player); }
        public NameResult TrySetName(string playerId, string name) { Probe("TrySetName"); return _inner.TrySetName(playerId, name); }
        public void AddMatch(string playerId, MatchRecord match) { Probe("AddMatch"); _inner.AddMatch(playerId, match); }
        public IReadOnlyList<MatchRecord> RecentMatches(string playerId, int limit) { Probe("RecentMatches"); return _inner.RecentMatches(playerId, limit); }
        public IReadOnlyList<PlayerRecord> TopByMmr(int limit) { Probe("TopByMmr"); return _inner.TopByMmr(limit); }
        public int Count() { Probe("Count"); return _inner.Count(); }
        public bool Delete(string playerId) { Probe("Delete"); return _inner.Delete(playerId); }
        public void CreateSession(string tokenHash, string playerId, DateTime expiresAt) { Probe("CreateSession"); _inner.CreateSession(tokenHash, playerId, expiresAt); }
        public string PlayerIdForSession(string tokenHash, DateTime now) { Probe("PlayerIdForSession"); return _inner.PlayerIdForSession(tokenHash, now); }
        public void DeleteSession(string tokenHash) { Probe("DeleteSession"); _inner.DeleteSession(tokenHash); }
        public void DeleteSessionsOf(string playerId) { Probe("DeleteSessionsOf"); _inner.DeleteSessionsOf(playerId); }
        public int DeleteExpiredSessions(DateTime now) { Probe("DeleteExpiredSessions"); return _inner.DeleteExpiredSessions(now); }
    }
}
