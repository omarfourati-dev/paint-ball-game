using System;
using System.Collections.Generic;
using System.Numerics;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Wählt Spawnpunkte so, dass Spawn-Kills reduziert werden (FR-12):
    /// Es gewinnt der Punkt mit dem größten Abstand zum nächsten Gegner,
    /// mit kleiner Zufallskomponente gegen vorhersehbare Spawns (Exploit-Vermeidung).
    /// </summary>
    public static class SpawnPointSelector
    {
        /// <returns>Index des besten Spawnpunkts oder -1 bei leerer Kandidatenliste.</returns>
        public static int SelectBestSpawn(
            IReadOnlyList<Vector3> candidateSpawns,
            IReadOnlyList<Vector3> enemyPositions,
            Random random = null)
        {
            if (candidateSpawns == null || candidateSpawns.Count == 0) return -1;
            random ??= new Random();

            int bestIndex = 0;
            float bestScore = float.MinValue;

            for (int i = 0; i < candidateSpawns.Count; i++)
            {
                float nearestEnemyDistance = float.MaxValue;
                for (int e = 0; e < enemyPositions.Count; e++)
                {
                    float d = Vector3.Distance(candidateSpawns[i], enemyPositions[e]);
                    if (d < nearestEnemyDistance) nearestEnemyDistance = d;
                }

                float score = (enemyPositions.Count == 0 ? 0f : nearestEnemyDistance)
                            + (float)random.NextDouble() * 0.001f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }
    }
}
