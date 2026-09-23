using System.Collections.Generic;
using Paintball.Core.Match;
using Paintball.Unity.Data;
using UnityEngine;

namespace Paintball.Unity.Spawn
{
    /// <summary>
    /// Verwaltet Spawn- und Respawn-Logik (FR-12): Schutzfenster, Anti-Spawn-Kill-Auswahl.
    /// </summary>
    public sealed class SpawnManager : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private GameConfig _config;

        [Header("Spawn Points")]
        [SerializeField] private List<Transform> _team0Spawns = new();
        [SerializeField] private List<Transform> _team1Spawns = new();

        private readonly Dictionary<int, SpawnProtection> _protections = new();

        public void RegisterPlayer(int playerId)
        {
            float seconds = _config != null ? _config.SpawnProtectionSeconds : 3f;
            _protections[playerId] = new SpawnProtection(seconds);
        }

        public Vector3 GetSpawnPosition(int teamId, List<Vector3> enemyPositions)
        {
            List<Transform> spawns = teamId == 0 ? _team0Spawns : _team1Spawns;
            if (spawns.Count == 0) return Vector3.zero;

            var spawnVectors = new List<Vector3>();
            foreach (var s in spawns)
                if (s != null) spawnVectors.Add(s.position);

            if (spawnVectors.Count == 0) return Vector3.zero;

            int bestIndex = SpawnPointSelector.SelectBestSpawn(spawnVectors, enemyPositions, new System.Random());
            return bestIndex >= 0 ? spawnVectors[bestIndex] : spawnVectors[0];
        }

        public void NotifySpawned(int playerId, float time)
        {
            if (_protections.TryGetValue(playerId, out var prot))
                prot.NotifySpawned(time);
        }

        public bool IsProtected(int playerId, float time)
        {
            return _protections.TryGetValue(playerId, out var prot) && prot.IsProtected(time);
        }

        public float GetProtectionRemaining(int playerId, float time)
        {
            if (_protections.TryGetValue(playerId, out var prot))
                return prot.RemainingSeconds(time);
            return 0f;
        }
    }
}
