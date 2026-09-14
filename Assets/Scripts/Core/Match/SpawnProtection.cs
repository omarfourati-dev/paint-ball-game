namespace Paintball.Core.Match
{
    /// <summary>
    /// Spawn-Schutz: Nach dem Spawn ist ein Spieler für ein Zeitfenster unverwundbar (FR-12).
    /// </summary>
    public sealed class SpawnProtection
    {
        private readonly float _durationSeconds;
        private float _spawnedAt = float.NegativeInfinity;

        public SpawnProtection(float durationSeconds = 3f)
        {
            _durationSeconds = durationSeconds;
        }

        public void NotifySpawned(float now) => _spawnedAt = now;

        public void Clear() => _spawnedAt = float.NegativeInfinity;

        public bool IsProtected(float now) => now - _spawnedAt < _durationSeconds;

        public float RemainingSeconds(float now)
            => IsProtected(now) ? _durationSeconds - (now - _spawnedAt) : 0f;
    }
}
