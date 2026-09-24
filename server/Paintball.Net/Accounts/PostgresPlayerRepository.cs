using System;
using System.Collections.Generic;
using System.Threading;
using Npgsql;

namespace Paintball.Net.Accounts
{
    /// <summary>Postgres-Persistenz (Datenbank "paintball" in zentrades-postgres). Schema wird beim Start angelegt.</summary>
    public sealed class PostgresPlayerRepository : IPlayerRepository
    {
        public const int SchemaVersion = 1;
        private readonly NpgsqlDataSource _db;

        public PostgresPlayerRepository(string databaseUrl)
        {
            _db = NpgsqlDataSource.Create(ToConnectionString(databaseUrl));
        }

        public static string ToConnectionString(string databaseUrl)
        {
            if (!databaseUrl.StartsWith("postgres://", StringComparison.Ordinal) && !databaseUrl.StartsWith("postgresql://", StringComparison.Ordinal))
                return databaseUrl;
            var uri = new Uri(databaseUrl);
            string[] userInfo = uri.UserInfo.Split(':', 2);
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
                Database = uri.AbsolutePath.TrimStart('/')
            };
            return b.ConnectionString;
        }

        private const string Schema = @"
CREATE TABLE IF NOT EXISTS schema_version (version int NOT NULL);
CREATE TABLE IF NOT EXISTS players (
  id uuid PRIMARY KEY,
  google_sub text NOT NULL UNIQUE,
  email text NOT NULL,
  display_name text NULL,
  display_name_lower text NULL UNIQUE,
  level int NOT NULL DEFAULT 1, xp int NOT NULL DEFAULT 0, mmr int NOT NULL DEFAULT 1000,
  matches int NOT NULL DEFAULT 0, wins int NOT NULL DEFAULT 0, eliminations int NOT NULL DEFAULT 0, deaths int NOT NULL DEFAULT 0,
  accuracy real NOT NULL DEFAULT 0, coins int NOT NULL DEFAULT 0,
  ach_kills int NOT NULL DEFAULT 0, ach_wins int NOT NULL DEFAULT 0, ach_matches int NOT NULL DEFAULT 0, ach_objective int NOT NULL DEFAULT 0,
  paint text NOT NULL DEFAULT 'paint_pink', accent text NOT NULL DEFAULT 'accent_yellow', marker text NOT NULL DEFAULT 'standard',
  created_at timestamptz NOT NULL, last_login_at timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS player_items (
  player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE, item_id text NOT NULL, PRIMARY KEY (player_id, item_id));
CREATE TABLE IF NOT EXISTS player_achievements (
  player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE, achievement_id text NOT NULL,
  unlocked_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY (player_id, achievement_id));
CREATE TABLE IF NOT EXISTS match_history (
  id bigserial PRIMARY KEY, player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  mode text NOT NULL, map text NOT NULL, won boolean NOT NULL, kills int NOT NULL, deaths int NOT NULL, objective int NOT NULL,
  xp_gained int NOT NULL, mmr_change int NOT NULL, played_at timestamptz NOT NULL);
CREATE INDEX IF NOT EXISTS match_history_player_time ON match_history (player_id, played_at DESC);
CREATE TABLE IF NOT EXISTS sessions (
  token_hash text PRIMARY KEY, player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  created_at timestamptz NOT NULL DEFAULT now(), expires_at timestamptz NOT NULL);
CREATE INDEX IF NOT EXISTS sessions_expires ON sessions (expires_at);
INSERT INTO schema_version (version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM schema_version);";

        /// <summary>Legt das Schema an; wiederholt bei nicht erreichbarer Datenbank (Container-Startreihenfolge).</summary>
        public void EnsureSchema(int attempts = 10, int delayMs = 2000)
        {
            for (int i = 1; ; i++)
            {
                try
                {
                    using NpgsqlCommand cmd = _db.CreateCommand(Schema);
                    cmd.ExecuteNonQuery();
                    return;
                }
                catch (NpgsqlException) when (i < attempts)
                {
                    Console.Error.WriteLine($"[DB] Datenbank nicht erreichbar, Versuch {i}/{attempts} – neuer Versuch in {delayMs} ms");
                    Thread.Sleep(delayMs);
                }
            }
        }

        public bool Ping()
        {
            try { using NpgsqlCommand c = _db.CreateCommand("SELECT 1"); c.ExecuteScalar(); return true; }
            catch (NpgsqlException) { return false; }
        }

        private const string PlayerColumns = "id, google_sub, email, display_name, level, xp, mmr, matches, wins, eliminations, deaths, accuracy, coins, ach_kills, ach_wins, ach_matches, ach_objective, paint, accent, marker, created_at, last_login_at";

        private static PlayerRecord ReadPlayer(NpgsqlDataReader r) => new PlayerRecord
        {
            Id = r.GetGuid(0).ToString(), GoogleSub = r.GetString(1), Email = r.GetString(2), DisplayName = r.IsDBNull(3) ? null : r.GetString(3),
            Level = r.GetInt32(4), Xp = r.GetInt32(5), Mmr = r.GetInt32(6), Matches = r.GetInt32(7), Wins = r.GetInt32(8),
            Eliminations = r.GetInt32(9), Deaths = r.GetInt32(10), Accuracy = r.GetFloat(11), Coins = r.GetInt32(12),
            AchKills = r.GetInt32(13), AchWins = r.GetInt32(14), AchMatches = r.GetInt32(15), AchObjective = r.GetInt32(16),
            Paint = r.GetString(17), Accent = r.GetString(18), Marker = r.GetString(19),
            CreatedAt = r.GetDateTime(20), LastLoginAt = r.GetDateTime(21)
        };

        private PlayerRecord QueryPlayer(string where, Action<NpgsqlCommand> bind)
        {
            PlayerRecord p;
            using (NpgsqlCommand cmd = _db.CreateCommand($"SELECT {PlayerColumns} FROM players WHERE {where}"))
            {
                bind(cmd);
                using NpgsqlDataReader r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                p = ReadPlayer(r);
            }
            LoadSets(p);
            return p;
        }

        private void LoadSets(PlayerRecord p)
        {
            using (NpgsqlCommand c = _db.CreateCommand("SELECT item_id FROM player_items WHERE player_id = $1"))
            {
                c.Parameters.AddWithValue(Guid.Parse(p.Id));
                using NpgsqlDataReader r = c.ExecuteReader();
                while (r.Read()) p.Items.Add(r.GetString(0));
            }
            using (NpgsqlCommand c = _db.CreateCommand("SELECT achievement_id FROM player_achievements WHERE player_id = $1"))
            {
                c.Parameters.AddWithValue(Guid.Parse(p.Id));
                using NpgsqlDataReader r = c.ExecuteReader();
                while (r.Read()) p.Achievements.Add(r.GetString(0));
            }
        }

        private static bool TryGuid(string id, out Guid g) => Guid.TryParse(id, out g);

        public PlayerRecord FindBySub(string googleSub)
            => QueryPlayer("google_sub = $1", c => c.Parameters.AddWithValue(googleSub ?? string.Empty));

        public PlayerRecord Get(string playerId)
            => TryGuid(playerId, out Guid g) ? QueryPlayer("id = $1", c => c.Parameters.AddWithValue(g)) : null;

        public PlayerRecord Create(string googleSub, string email)
        {
            var id = Guid.NewGuid();
            DateTime now = DateTime.UtcNow;
            using (NpgsqlCommand c = _db.CreateCommand("INSERT INTO players (id, google_sub, email, created_at, last_login_at) VALUES ($1, $2, $3, $4, $4)"))
            {
                c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(googleSub); c.Parameters.AddWithValue(email ?? string.Empty); c.Parameters.AddWithValue(now);
                c.ExecuteNonQuery();
            }
            return Get(id.ToString());
        }

        public void RecordLogin(string playerId, string email, DateTime when)
        {
            if (!TryGuid(playerId, out Guid g)) return;
            using NpgsqlCommand c = _db.CreateCommand("UPDATE players SET email = $2, last_login_at = $3 WHERE id = $1");
            c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(email ?? string.Empty); c.Parameters.AddWithValue(when);
            c.ExecuteNonQuery();
        }

        public void SaveProgress(PlayerRecord p)
        {
            if (!TryGuid(p.Id, out Guid g)) return;
            using NpgsqlConnection conn = _db.OpenConnection();
            using NpgsqlTransaction tx = conn.BeginTransaction();
            using (var c = new NpgsqlCommand(@"UPDATE players SET level=$2, xp=$3, mmr=$4, matches=$5, wins=$6, eliminations=$7, deaths=$8,
                accuracy=$9, coins=$10, ach_kills=$11, ach_wins=$12, ach_matches=$13, ach_objective=$14, paint=$15, accent=$16, marker=$17 WHERE id=$1", conn, tx))
            {
                c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(p.Level); c.Parameters.AddWithValue(p.Xp); c.Parameters.AddWithValue(p.Mmr);
                c.Parameters.AddWithValue(p.Matches); c.Parameters.AddWithValue(p.Wins); c.Parameters.AddWithValue(p.Eliminations); c.Parameters.AddWithValue(p.Deaths);
                c.Parameters.AddWithValue(p.Accuracy); c.Parameters.AddWithValue(p.Coins); c.Parameters.AddWithValue(p.AchKills); c.Parameters.AddWithValue(p.AchWins);
                c.Parameters.AddWithValue(p.AchMatches); c.Parameters.AddWithValue(p.AchObjective); c.Parameters.AddWithValue(p.Paint);
                c.Parameters.AddWithValue(p.Accent); c.Parameters.AddWithValue(p.Marker);
                c.ExecuteNonQuery();
            }
            foreach (string item in p.Items)
                using (var c = new NpgsqlCommand("INSERT INTO player_items (player_id, item_id) VALUES ($1, $2) ON CONFLICT DO NOTHING", conn, tx))
                { c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(item); c.ExecuteNonQuery(); }
            foreach (string a in p.Achievements)
                using (var c = new NpgsqlCommand("INSERT INTO player_achievements (player_id, achievement_id) VALUES ($1, $2) ON CONFLICT DO NOTHING", conn, tx))
                { c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(a); c.ExecuteNonQuery(); }
            tx.Commit();
        }

        public NameResult TrySetName(string playerId, string name)
        {
            if (!TryGuid(playerId, out Guid g)) return NameResult.Invalid;
            try
            {
                using NpgsqlCommand c = _db.CreateCommand("UPDATE players SET display_name = $2, display_name_lower = $3 WHERE id = $1");
                c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(name); c.Parameters.AddWithValue(name.ToLowerInvariant());
                return c.ExecuteNonQuery() == 1 ? NameResult.Ok : NameResult.Invalid;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return NameResult.Taken;
            }
        }

        public void AddMatch(string playerId, MatchRecord m)
        {
            if (!TryGuid(playerId, out Guid g)) return;
            using NpgsqlCommand c = _db.CreateCommand(@"INSERT INTO match_history (player_id, mode, map, won, kills, deaths, objective, xp_gained, mmr_change, played_at)
                SELECT $1, $2, $3, $4, $5, $6, $7, $8, $9, $10 WHERE EXISTS (SELECT 1 FROM players WHERE id = $1)");
            c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(m.Mode); c.Parameters.AddWithValue(m.Map); c.Parameters.AddWithValue(m.Won);
            c.Parameters.AddWithValue(m.Kills); c.Parameters.AddWithValue(m.Deaths); c.Parameters.AddWithValue(m.Objective);
            c.Parameters.AddWithValue(m.XpGained); c.Parameters.AddWithValue(m.MmrChange); c.Parameters.AddWithValue(m.PlayedAt);
            c.ExecuteNonQuery();
        }

        public IReadOnlyList<MatchRecord> RecentMatches(string playerId, int limit)
        {
            var list = new List<MatchRecord>();
            if (!TryGuid(playerId, out Guid g)) return list;
            using NpgsqlCommand c = _db.CreateCommand(@"SELECT mode, map, won, kills, deaths, objective, xp_gained, mmr_change, played_at
                FROM match_history WHERE player_id = $1 ORDER BY played_at DESC, id DESC LIMIT $2");
            c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(limit);
            using NpgsqlDataReader r = c.ExecuteReader();
            while (r.Read())
                list.Add(new MatchRecord
                {
                    Mode = r.GetString(0), Map = r.GetString(1), Won = r.GetBoolean(2), Kills = r.GetInt32(3), Deaths = r.GetInt32(4),
                    Objective = r.GetInt32(5), XpGained = r.GetInt32(6), MmrChange = r.GetInt32(7), PlayedAt = r.GetDateTime(8)
                });
            return list;
        }

        public IReadOnlyList<PlayerRecord> TopByMmr(int limit)
        {
            var list = new List<PlayerRecord>();
            using NpgsqlCommand c = _db.CreateCommand($"SELECT {PlayerColumns} FROM players WHERE display_name IS NOT NULL ORDER BY mmr DESC, wins DESC LIMIT $1");
            c.Parameters.AddWithValue(limit);
            using NpgsqlDataReader r = c.ExecuteReader();
            while (r.Read()) list.Add(ReadPlayer(r));
            return list;
        }

        public int Count()
        {
            using NpgsqlCommand c = _db.CreateCommand("SELECT count(*) FROM players");
            return Convert.ToInt32(c.ExecuteScalar());
        }

        public bool Delete(string playerId)
        {
            if (!TryGuid(playerId, out Guid g)) return false;
            using NpgsqlCommand c = _db.CreateCommand("DELETE FROM players WHERE id = $1");
            c.Parameters.AddWithValue(g);
            return c.ExecuteNonQuery() == 1;
        }

        public void CreateSession(string tokenHash, string playerId, DateTime expiresAt)
        {
            using NpgsqlCommand c = _db.CreateCommand("INSERT INTO sessions (token_hash, player_id, expires_at) VALUES ($1, $2, $3)");
            c.Parameters.AddWithValue(tokenHash); c.Parameters.AddWithValue(Guid.Parse(playerId)); c.Parameters.AddWithValue(expiresAt);
            c.ExecuteNonQuery();
        }

        public string PlayerIdForSession(string tokenHash, DateTime now)
        {
            if (string.IsNullOrEmpty(tokenHash)) return null;
            using NpgsqlCommand c = _db.CreateCommand("SELECT player_id FROM sessions WHERE token_hash = $1 AND expires_at > $2");
            c.Parameters.AddWithValue(tokenHash); c.Parameters.AddWithValue(now);
            object v = c.ExecuteScalar();
            return v is Guid g ? g.ToString() : null;
        }

        public void DeleteSession(string tokenHash)
        {
            using NpgsqlCommand c = _db.CreateCommand("DELETE FROM sessions WHERE token_hash = $1");
            c.Parameters.AddWithValue(tokenHash ?? string.Empty);
            c.ExecuteNonQuery();
        }

        public void DeleteSessionsOf(string playerId)
        {
            if (!TryGuid(playerId, out Guid g)) return;
            using NpgsqlCommand c = _db.CreateCommand("DELETE FROM sessions WHERE player_id = $1");
            c.Parameters.AddWithValue(g);
            c.ExecuteNonQuery();
        }

        public int DeleteExpiredSessions(DateTime now)
        {
            using NpgsqlCommand c = _db.CreateCommand("DELETE FROM sessions WHERE expires_at <= $1");
            c.Parameters.AddWithValue(now);
            return c.ExecuteNonQuery();
        }
    }
}
