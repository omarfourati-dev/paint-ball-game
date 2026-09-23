using System.Collections.Generic;
using System;

namespace Paintball.Core.Economy
{
    /// <summary>
    /// Transparente Lootbox-/Gacha-Mechanik (M-06 inkl. gesetzeskonformer
    /// Wahrscheinlichkeitsangaben und klarer Limits). Keine spielentscheidenden
    /// Inhalte drin – nur Kosmetik (M-02, NFR-14).
    /// </summary>
    public sealed class CosmeticLootBox
    {
        private readonly List<string> _entries = new();
        private readonly List<float> _weights = new();
        private readonly Random _rng;
        private int _pulls = 0;

        public int TotalEntries => _entries.Count;
        public int PullsCount => _pulls;

        public CosmeticLootBox(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>Registriert einen kosmetischen Eintrag mit relativem Gewicht (&gt; 0).</summary>
        public void AddEntry(string id, float weight)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Leere Lootbox-Id.", nameof(id));
            if (weight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(weight), "Gewicht muss > 0 sein.");

            _entries.Add(id);
            _weights.Add(weight);
        }

        /// <summary>Summe der Gewichte (= Summe der Wahrscheinlichkeiten).</summary>
        public float TotalWeight()
        {
            float sum = 0f;
            foreach (float w in _weights) sum += w;
            return sum;
        }

        /// <summary>Rote Wahrscheinlichkeit (0..1) für einen Eintrag – transparent anzeigbar (M-06).</summary>
        public float DropChance(string id)
        {
            int index = _entries.IndexOf(id);
            if (index < 0) return 0f;
            float total = TotalWeight();
            return total > 0f ? _weights[index] / total : 0f;
        }

        /// <summary>Zieht einen Eintrag nach Gewicht (autoritativ).</summary>
        public string Roll()
        {
            if (_entries.Count == 0)
                throw new InvalidOperationException("Lootbox ist leer.");

            float total = TotalWeight();
            float roll = (float)_rng.NextDouble() * total;

            float cumulative = 0f;
            for (int i = 0; i < _entries.Count; i++)
            {
                cumulative += _weights[i];
                if (roll < cumulative)
                {
                    _pulls++;
                    return _entries[i];
                }
            }

            _pulls++;
            return _entries[_entries.Count - 1];
        }
    }
}