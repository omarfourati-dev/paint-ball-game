using System.Collections.Generic;

namespace Paintball.Core.Progression
{
    /// <summary>
    /// Ausrüstungs-Slot-Kategorien (FR-35): Marker, Ausweichgadget, Verbrauchsgegenstand.
    /// </summary>
    public enum EquipmentSlot
    {
        Marker,
        DodgeGadget,
        Consumable
    }

    /// <summary>
    /// Reine Ausrüstungs-Logik (FR-35): 3 Slots (Marker, Ausweichgadget, Verbrauchsgegenstand),
    /// Inventar-Verwaltung und Gadget/Verbrauch-Cooldowns. Keine Unity-Abhängigkeit.
    /// </summary>
    public sealed class EquipmentGarage
    {
        private readonly Dictionary<EquipmentSlot, string> _equipped = new()
        {
            [EquipmentSlot.Marker] = "marker_default",
            [EquipmentSlot.DodgeGadget] = "",
            [EquipmentSlot.Consumable] = ""
        };

        private readonly Dictionary<string, bool> _ownedMarkers = new();
        private readonly Dictionary<string, int> _dodgeGadgetUses = new();
        private readonly Dictionary<string, int> _consumables = new();

        private readonly Dictionary<string, float> _dodgeCooldownUntil = new();

        public EquipmentGarage()
        {
            _ownedMarkers["marker_default"] = true;
        }

        public string EquippedMarker => _equipped[EquipmentSlot.Marker];
        public string EquippedDodgeGadget => _equipped[EquipmentSlot.DodgeGadget];
        public string EquippedConsumable => _equipped[EquipmentSlot.Consumable];

        public const int DodgeGadgetMaxUses = 3;
        public const float DodgeGadgetCooldownSeconds = 4f;

        public void SetOwnedMarker(string markerId, bool owned)
        {
            if (string.IsNullOrEmpty(markerId)) return;
            _ownedMarkers[markerId] = owned;
        }

        public bool OwnsMarker(string markerId)
        {
            return _ownedMarkers.TryGetValue(markerId, out bool owned) && owned;
        }

        public bool TryEquipMarker(string markerId)
        {
            if (!OwnsMarker(markerId)) return false;
            _equipped[EquipmentSlot.Marker] = markerId;
            return true;
        }

        public bool TryEquipDodgeGadget(string gadgetId, int uses)
        {
            if (uses <= 0) return false;
            _equipped[EquipmentSlot.DodgeGadget] = gadgetId;
            _dodgeGadgetUses[gadgetId] = uses;
            return true;
        }

        public bool TryEquipConsumable(string consumableId, int amount)
        {
            if (amount <= 0) return false;
            _equipped[EquipmentSlot.Consumable] = consumableId;
            _consumables[consumableId] = amount;
            return true;
        }

        public int DodgeGadgetUsesAvailable(string gadgetId)
        {
            return _dodgeGadgetUses.TryGetValue(gadgetId, out int uses) ? uses : 0;
        }

        public float DodgeGadgetCooldownRemaining(string gadgetId, float now)
        {
            if (!_dodgeCooldownUntil.TryGetValue(gadgetId, out float end))
                return 0f;
            return System.Math.Max(0f, end - now);
        }

        /// <summary>
        /// Versucht ein Ausweichen (Dodge). Benötigt einen ausgerüsteten Gadget-Slot mit
        /// verbleibenden Nutzungen und abgelaufenem Cooldown (FR-35).
        /// Gibt true zurück, wenn das Ausweichen gestartet wurde.
        /// </summary>
        public bool TryDodge(float now)
        {
            string gadget = _equipped[EquipmentSlot.DodgeGadget];
            if (string.IsNullOrEmpty(gadget)) return false;
            if (DodgeGadgetUsesAvailable(gadget) <= 0) return false;
            if (DodgeGadgetCooldownRemaining(gadget, now) > 0f) return false;

            _dodgeGadgetUses[gadget]--;
            _dodgeCooldownUntil[gadget] = now + DodgeGadgetCooldownSeconds;
            return true;
        }

        /// <summary>
        /// Verbraucht einen Gegenstand im Verbrauchs-Slot (z.B. Munitionspack).
        /// Gibt true zurück, wenn verfügbar und Verbrauch erfolgreich war.
        /// </summary>
        public bool TryConsume(string consumableId)
        {
            string equipped = _equipped[EquipmentSlot.Consumable];
            if (equipped != consumableId) return false;
            if (!_consumables.TryGetValue(consumableId, out int amount) || amount <= 0) return false;

            _consumables[consumableId]--;
            return true;
        }

        public int ConsumableAmount(string consumableId)
        {
            return _consumables.TryGetValue(consumableId, out int amount) ? amount : 0;
        }
    }
}