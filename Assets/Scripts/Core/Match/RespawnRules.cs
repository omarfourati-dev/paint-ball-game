using System.Collections.Generic;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Server-autoritative Respawn- und Schutzlogik (FR-12, NFR-05):
    /// verzögerter Respawn, Spawn-Schutzfenster und Spawn-Kill-Reduktion,
    /// damit Respawns nicht ausgenutzt werden können (Exploit-Verhinderung).
    /// Keine Unity-Abhängigkeit.
    /// </summary>
    public sealed class RespawnRules
    {
        /// <summary>Standard-Respawn-Verzögerung in Sekunden.</summary>
        public float RespawnDelaySeconds { get; }

        /// <summary>Schutzfenster nach Respawn in Sekunden (Schaden wird blockiert, FR-12).</summary>
        public float ProtectionDuration { get; }

        /// <summary>Zusätzliche Verzögerung pro weiterem schnellen Tod nach dem 2. (Anti-Spawn-Kill).</summary>
        public float SpawnKillPenaltySeconds { get; }

        /// <summary>Zeitfenster in dem aufeinanderfolgende Tode als "Spawn-Kill-Serie" gelten.</summary>
        public float SpawnKillWindowSeconds { get; }

        private readonly Dictionary<int, List<float>> _recentDeaths = new();
        private readonly Dictionary<int, float> _protectedUntil = new();

        public RespawnRules(
            float respawnDelaySeconds = 3f,
            float protectionDuration = 2.5f,
            float spawnKillPenaltySeconds = 2f,
            float spawnKillWindowSeconds = 10f)
        {
            RespawnDelaySeconds = respawnDelaySeconds;
            ProtectionDuration = protectionDuration;
            SpawnKillPenaltySeconds = spawnKillPenaltySeconds;
            SpawnKillWindowSeconds = spawnKillWindowSeconds;
        }

        /// <summary>Registriert den Tod eines Spielers zu einem Zeitpunkt (Server autoritativ).</summary>
        public void RegisterDeath(int playerId, float now)
        {
            if (!_recentDeaths.TryGetValue(playerId, out List<float> deaths))
            {
                deaths = new List<float>();
                _recentDeaths[playerId] = deaths;
            }

            deaths.Add(now);
            deaths.RemoveAll(d => now - d > SpawnKillWindowSeconds);
        }

        /// <summary>Anzahl der Tode innerhalb des Spawn-Kill-Fensters.</summary>
        public int RapidDeathCount(int playerId, float now)
        {
            if (!_recentDeaths.TryGetValue(playerId, out List<float> deaths))
                return 0;
            return deaths.Count;
        }

        /// <summary>Gibt zurück, ab welchem Zeitpunkt der Respawn erlaubt ist (inkl. Spawn-Kill-Penalty).</summary>
        public float GetRespawnTime(int playerId, float now)
        {
            return now + RespawnDelaySeconds + CalculateSpawnKillPenalty(playerId, now);
        }

        /// <summary>
        /// Anteil der Schutzzeit, der bei wiederholtem schnellen Tod übrig bleibt:
        /// Weniger Schutz bei Spawn-Kill-Serien (Exploit-Dämpfer).
        /// </summary>
        public float GetProtectionDuration(int playerId, float now)
        {
            if (RapidDeathCount(playerId, now) > 2)
                return ProtectionDuration * 0.8f;
            return ProtectionDuration;
        }

        /// <summary>Bestätigt den Respawn und setzt das Schutzfenster.</summary>
        public float ConfirmRespawn(int playerId, float now)
        {
            float protection = GetProtectionDuration(playerId, now);
            _protectedUntil[playerId] = now + protection;
            if (_recentDeaths.TryGetValue(playerId, out List<float> deaths))
                deaths.Clear();
            return protection;
        }

        /// <summary>True, solange der Spieler nach Respawn geschützt ist (Schaden blockiert).</summary>
        public bool IsProtected(int playerId, float now)
        {
            return _protectedUntil.TryGetValue(playerId, out float end) && now < end;
        }

        /// <summary>Gibt den Schadensmultiplikator an (0 = geschützt, 1 = voller Schaden).</summary>
        public float DamageTakenMultiplier(int playerId, float now)
        {
            return IsProtected(playerId, now) ? 0f : 1f;
        }

        private float CalculateSpawnKillPenalty(int playerId, float now)
        {
            int rapid = RapidDeathCount(playerId, now);

            // Ab dem 3. schnellen Tod wird jede weitere Serie bestraft,
            // damit wiederholte Spawn-Kills keine dauerhaften Respawns erzwingen.
            if (rapid > 2)
                return SpawnKillPenaltySeconds;
            return 0f;
        }
    }
}