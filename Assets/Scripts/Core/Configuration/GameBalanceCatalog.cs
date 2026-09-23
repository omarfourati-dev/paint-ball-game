using System;

namespace Paintball.Core.Configuration
{
    /// <summary>
    /// Datengetriebene Spielparameter (NFR-17): Balance-Werte ohne Neu-Build
    /// anpassbar (Remote Config / LocalPersistence). Pure Core-Logik. Werte
    /// werden auf gültige Bereiche geklemmt; Defaults bei Nichtvorhandensein.
    /// </summary>
    public sealed class GameBalanceCatalog
    {
        public const int DefaultMmrKFactor = 32;
        public const float DefaultDamageBodyMultiplier = 1f;
        public const float DefaultRespawnDelaySeconds = 3f;
        public const float DefaultSpawnProtectionSeconds = 3f;
        public const float DefaultCoverDamageMultiplier = 0.3f;

        public int MmrKFactor { get; private set; } = DefaultMmrKFactor;
        public float DamageBodyMultiplier { get; private set; } = DefaultDamageBodyMultiplier;
        public float RespawnDelaySeconds { get; private set; } = DefaultRespawnDelaySeconds;
        public float SpawnProtectionSeconds { get; private set; } = DefaultSpawnProtectionSeconds;
        public float CoverDamageMultiplier { get; private set; } = DefaultCoverDamageMultiplier;

        public void SetMmrKFactor(int k)
            => MmrKFactor = Math.Clamp(k, 8, 64);

        public void SetDamageBodyMultiplier(float v)
            => DamageBodyMultiplier = Math.Clamp(v, 0.1f, 5f);

        public void SetRespawnDelay(float seconds)
            => RespawnDelaySeconds = Math.Clamp(seconds, 0.5f, 30f);

        public void SetSpawnProtection(float seconds)
            => SpawnProtectionSeconds = Math.Clamp(seconds, 0f, 15f);

        public void SetCoverDamageMultiplier(float v)
            => CoverDamageMultiplier = Math.Clamp(v, 0f, 1f);

        public string Serialize()
        {
            return string.Join("\n",
                $"mmrK={MmrKFactor}",
                $"damageBody={DamageBodyMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}",
                $"respawnDelay={RespawnDelaySeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}",
                $"spawnProtection={SpawnProtectionSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}",
                $"coverDamage={CoverDamageMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        public static GameBalanceCatalog Deserialize(string data)
        {
            var cat = new GameBalanceCatalog();
            if (string.IsNullOrEmpty(data)) return cat;

            foreach (string raw in data.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq);
                string value = line.Substring(eq + 1);

                switch (key)
                {
                    case "mmrK":
                        if (int.TryParse(value, out int k)) cat.SetMmrKFactor(k);
                        break;
                    case "damageBody":
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float db))
                            cat.SetDamageBodyMultiplier(db);
                        break;
                    case "respawnDelay":
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float rd))
                            cat.SetRespawnDelay(rd);
                        break;
                    case "spawnProtection":
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float sp))
                            cat.SetSpawnProtection(sp);
                        break;
                    case "coverDamage":
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float cd))
                            cat.SetCoverDamageMultiplier(cd);
                        break;
                }
            }

            return cat;
        }
    }
}