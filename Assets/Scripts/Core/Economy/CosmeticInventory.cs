using System;
using System.Collections.Generic;

namespace Paintball.Core.Economy
{
    /// <summary>
    /// Kosmetik-Inventar (FR-41): Besitz prüfen, Finanzgüter equipen,
    /// persistente Serialisierung. Kosmetisch oder Komfort – nie spielentscheidend
    /// (NFR-14, kein Pay-to-Win). Keine Unity-Abhängigkeiten.
    /// </summary>
    public sealed class CosmeticInventory
    {
        public const char ItemSeparator = ',';
        public const char FieldSeparator = ';';

        private readonly HashSet<string> _owned = new();
        private string _equipped;

        public string Equipped => _equipped;
        public int OwnedCount => _owned.Count;

        public void Grant(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            _owned.Add(itemId);
        }

        public bool Owns(string itemId) => _owned.Contains(itemId);

        public void Equip(string itemId)
        {
            if (!Owns(itemId)) return;
            _equipped = itemId;
        }

        public void Unequip()
        {
            _equipped = null;
        }

        /// <summary>Übernimmt Besitz und Equip aus einer geladenen Instanz (Restore).</summary>
        public void RestoreFrom(CosmeticInventory source)
        {
            if (source == null) return;
            _owned.Clear();
            foreach (string item in source._owned)
                _owned.Add(item);
            _equipped = source._equipped;
        }

        public string Serialize()
        {
            var parts = new List<string>();
            var owned = new List<string>(_owned);
            owned.Sort(StringComparer.Ordinal);
            parts.Add("owned=" + string.Join(ItemSeparator, owned));
            parts.Add("equipped=" + (_equipped ?? string.Empty));
            return string.Join(FieldSeparator, parts);
        }

        public static CosmeticInventory Deserialize(string data)
        {
            var inventory = new CosmeticInventory();
            if (string.IsNullOrEmpty(data)) return inventory;

            foreach (string field in data.Split(FieldSeparator))
            {
                int eq = field.IndexOf('=');
                if (eq <= 0) continue;

                string key = field.Substring(0, eq);
                string value = field.Substring(eq + 1);

                switch (key)
                {
                    case "owned":
                        foreach (string item in value.Split(ItemSeparator))
                            if (item.Length > 0)
                                inventory._owned.Add(item);
                        break;
                    case "equipped":
                        inventory._equipped = value.Length > 0 ? value : null;
                        break;
                }
            }

            return inventory;
        }
    }
}