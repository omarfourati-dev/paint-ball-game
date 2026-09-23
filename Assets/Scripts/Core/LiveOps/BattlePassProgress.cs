using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Paintball.Core.LiveOps
{
    /// <summary>Eine Stufe des Battle Pass (M-03).</summary>
    public struct BattlePassTier
    {
        public int Tier;
        public int XpRequired;
        public string RewardName;
        public bool IsPremiumReward;
    }

    /// <summary>
    /// Battle-Pass-Fortschritt als pure Core-Logik (M-03, FR-47): Stufen-Tabelle,
    /// XP-Sammeln mit Level-Ups, Premium-Freischaltung. Nur Kosmetik – kein Pay-to-Win
    /// (NFR-14). Portables Serialize/Deserialize für die Datei-Persistenz.
    /// </summary>
    public sealed class BattlePassProgress
    {
        private readonly List<BattlePassTier> _tiers = new();
        private int _currentXp;

        public bool HasPremiumPass { get; private set; }
        public int CurrentTier { get; private set; }
        public int CurrentXp => _currentXp;
        public IReadOnlyList<BattlePassTier> Tiers => _tiers;

        /// <summary>Wird bei jedem Level-Up mit der neuen Tier-Nummer ausgelöst.</summary>
        public event Action<int> OnTierUp;

        public void SetTiers(IReadOnlyList<BattlePassTier> tiers)
        {
            _tiers.Clear();
            if (tiers != null) _tiers.AddRange(tiers);
            CurrentTier = Math.Clamp(CurrentTier, 0, _tiers.Count);
        }

        public void SetPremium(bool premium) => HasPremiumPass = premium;

        /// <summary>Stellt XP aus einer Datei wieder her (Restwert ohne Level-Ups).</summary>
        public void RestoreXp(int xp) => _currentXp = Math.Max(0, xp);

        public void SetTier(int tier) => CurrentTier = Math.Clamp(tier, 0, _tiers.Count);

        public bool HasNextTier() => CurrentTier < _tiers.Count;

        public int GetNextTierXpRequired()
        {
            if (!HasNextTier()) return 0;
            return _tiers[CurrentTier].XpRequired;
        }

        /// <summary>Addiert XP und stufet auf. Gibt die Anzahl der Level-Ups zurück.</summary>
        public int AddXp(int amount)
        {
            if (amount <= 0) return 0;

            int levelUps = 0;
            _currentXp += amount;
            while (HasNextTier() && _currentXp >= GetNextTierXpRequired())
            {
                _currentXp -= GetNextTierXpRequired();
                CurrentTier++;
                levelUps++;
                OnTierUp?.Invoke(CurrentTier);
            }
            return levelUps;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("tier=").Append(CurrentTier).Append('\n');
            sb.Append("xp=").Append(CurrentXp).Append('\n');
            sb.Append("premium=").Append(HasPremiumPass);
            return sb.ToString();
        }

        public static BattlePassProgress Deserialize(string data)
        {
            var pass = new BattlePassProgress();
            if (string.IsNullOrEmpty(data)) return pass;

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
                    case "tier":
                        if (int.TryParse(value, out int tier))
                            pass.CurrentTier = Math.Max(0, tier);
                        break;
                    case "xp":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int xp))
                            pass._currentXp = Math.Max(0, xp);
                        break;
                    case "premium":
                        if (bool.TryParse(value, out bool premium)) pass.HasPremiumPass = premium;
                        break;
                }
            }

            return pass;
        }
    }
}