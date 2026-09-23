using System;
using System.Collections.Generic;

namespace Paintball.Core.Progression
{
    /// <summary>Auslöser-Kategorie für Errungenschaften (FR-45).</summary>
    public enum AchievementType
    {
        CombinedEliminations,
        CombinedWins,
        MatchesPlayed,
        SeasonRankMinimum,
        ObjectiveScore
    }

    /// <summary>Definition einer Errungenschaft (FR-45).</summary>
    public sealed class AchievementDef
    {
        public string Id;
        public string Title;
        public AchievementType Type;
        public int Target;
        public int RewardXp;
    }

    /// <summary>Fortschritt eines Spielers in einer Errungenschaft.</summary>
    public sealed class AchievementProgress
    {
        public string Id;
        public int Progress;
        public bool Unlocked;
    }

    /// <summary>
    /// Errungenschaften & Meilensteine (FR-45): spielstil-, teamplay- und
    /// langzeitbasierte Ziele mit Freischalt-Schwellen. Pure Core-Logik
    /// (Unity-frei, keine Spiel-Entscheidung durch Käufe).
    /// </summary>
    public sealed class AchievementsCatalog
    {
        private readonly List<AchievementDef> _defs = new();
        private readonly Dictionary<string, AchievementProgress> _progress = new();

        public IReadOnlyList<AchievementDef> Definitions => _defs;
        public int UnlockedCount { get; private set; }

        public void Register(AchievementDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) return;
            _defs.Add(def);
            _progress[def.Id] = new AchievementProgress { Id = def.Id };
        }

        public IReadOnlyList<AchievementProgress> AllProgress()
        {
            return new List<AchievementProgress>(_progress.Values);
        }

        /// <summary>Verarbeitet einen Wert pro Errungenschaften-Typ pro Match.</summary>
        public void Report(AchievementType type, int value)
        {
            foreach (var def in _defs)
            {
                if (def.Type != type || !_progress.TryGetValue(def.Id, out var p)) continue;
                if (p.Unlocked) continue;

                p.Progress = Math.Clamp(p.Progress + value, 0, def.Target);
                if (p.Progress >= def.Target)
                {
                    p.Unlocked = true;
                    UnlockedCount++;
                }
            }
        }

        public bool IsUnlocked(string id)
        {
            if (!_progress.TryGetValue(id, out var p)) return false;
            return p.Unlocked;
        }

        public int ProgressOf(string id)
        {
            if (!_progress.TryGetValue(id, out var p)) return 0;
            return p.Progress;
        }
    }
}