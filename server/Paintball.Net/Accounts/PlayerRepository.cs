using System;
using System.Collections.Generic;

namespace Paintball.Net.Accounts
{
    /// <summary>Gespeicherter Spieler (eine Zeile players + Items/Errungenschaften).</summary>
    public sealed class PlayerRecord
    {
        public string Id;
        public string GoogleSub;
        public string Email;
        public string DisplayName;          // null = noch nicht gewählt
        public int Level = 1, Xp, Mmr = 1000, Matches, Wins, Eliminations, Deaths;
        public float Accuracy;
        public int Coins;
        public int AchKills, AchWins, AchMatches, AchObjective;   // Zähler für Errungenschaften (PlayerProfile)
        public string Paint = "paint_pink", Accent = "accent_yellow", Marker = "standard";
        public DateTime CreatedAt, LastLoginAt;
        public HashSet<string> Items = new();
        public HashSet<string> Achievements = new();
    }

    public sealed class MatchRecord
    {
        public string Mode = "tdm", Map = "warehouse";
        public bool Won;
        public int Kills, Deaths, Objective, XpGained, MmrChange;
        public DateTime PlayedAt;
    }

    public enum NameResult { Ok, Taken, Invalid }

    /// <summary>Persistenz der Spielerkonten. Alle Zeiten UTC. Namen werden vorher von AccountStore validiert.</summary>
    public interface IPlayerRepository
    {
        PlayerRecord FindBySub(string googleSub);
        PlayerRecord Get(string playerId);
        PlayerRecord Create(string googleSub, string email);
        void RecordLogin(string playerId, string email, DateTime when);
        /// <summary>Schreibt alle Fortschrittsfelder, Ausrüstung, Items und Errungenschaften (Items/Errungenschaften nur hinzufügen).</summary>
        void SaveProgress(PlayerRecord player);
        /// <summary>Setzt den (bereits validierten) Namen; Taken, wenn ein anderer Spieler ihn case-insensitiv hat.</summary>
        NameResult TrySetName(string playerId, string name);
        void AddMatch(string playerId, MatchRecord match);
        IReadOnlyList<MatchRecord> RecentMatches(string playerId, int limit);
        /// <summary>Nur Spieler mit Namen, sortiert nach Mmr desc, Wins desc.</summary>
        IReadOnlyList<PlayerRecord> TopByMmr(int limit);
        int Count();
        bool Delete(string playerId);
        void CreateSession(string tokenHash, string playerId, DateTime expiresAt);
        string PlayerIdForSession(string tokenHash, DateTime now);
        void DeleteSession(string tokenHash);
        void DeleteSessionsOf(string playerId);
        int DeleteExpiredSessions(DateTime now);
    }
}
