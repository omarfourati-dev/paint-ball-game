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

        public WrappingRepository(IPlayerRepository inner = null) { _inner = inner ?? new InMemoryPlayerRepository(); }

        private void MaybeFail(string playerId)
        {
            if (FailWritesFor != null && playerId == FailWritesFor) throw new InvalidOperationException("Datenbank nicht erreichbar (Test)");
        }

        public PlayerRecord FindBySub(string googleSub) => _inner.FindBySub(googleSub);
        public PlayerRecord Get(string playerId) => _inner.Get(playerId);
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
}
