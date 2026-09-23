using System.Collections.Generic;

namespace Paintball.Core.Economy
{
    /// <summary>Kategorien der käuflichen Inhalte (M-02, M-04).</summary>
    public enum ShopItemKind
    {
        Skin,
        PaintColor,
        Emote,
        BattlePassLevel,
        ConsumableCosmetic
    }

    /// <summary>Eintrag im Shop (M-02 bis M-06).</summary>
    public readonly struct ShopItem
    {
        public string Id { get; }
        public string DisplayName { get; }
        public ShopItemKind Kind { get; }
        public CurrencyType Currency { get; }
        public int Price { get; }
        public bool IsLimited { get; }

        /// <summary>
        /// Kosmetikhinweis (M-04, NFR-14): Ein Posten, der reine Kosmetik ist,
        /// darf keine Gameplay-Werte beeinflussen – im Shop transparent gekennzeichnet.
        /// </summary>
        public bool IsCosmeticOnly => Kind switch
        {
            ShopItemKind.Skin or ShopItemKind.PaintColor or ShopItemKind.Emote => true,
            _ => false
        };

        public ShopItem(string id, string displayName, ShopItemKind kind,
            CurrencyType currency, int price, bool isLimited = false)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            Currency = currency;
            Price = price;
            IsLimited = isLimited;
        }
    }

    /// <summary>
    /// Shop-Katalog (M-01 bis M-06): Transparenter, fairer Kosmetik-Shop.
    /// Verhindert Pay-to-Win, indem Gameplay-relevante Artikel (Marker-Werte, Gadgets)
    /// nicht über Währung gekauft werden können (NFR-14). Preis-Transparenz (M-06).
    /// </summary>
    public sealed class ShopCatalog
    {
        private readonly Dictionary<string, ShopItem> _items = new();

        public IReadOnlyCollection<ShopItem> AllItems => _items.Values;

        public void Add(ShopItem item)
        {
            if (string.IsNullOrEmpty(item.Id) || item.Price < 0)
                throw new System.ArgumentException("Ungültiger Shop-Posten", nameof(item));

            _items[item.Id] = item;
        }

        public bool TryGet(string id, out ShopItem item)
        {
            return _items.TryGetValue(id, out item);
        }

        public bool CanPurchase(string id, PlayerWallet wallet)
        {
            if (wallet == null || !TryGet(id, out ShopItem item))
                return false;
            return wallet.Balance(item.Currency) >= item.Price;
        }

        /// <summary>
        /// Führt den Kauf autoritativ aus (M-04): validiert Posten, Guthaben und
        /// verhindert Gameplay-Beeinflussung (nur Kosmetik per Definition kaufbar).
        /// Gibt true bei erfolgreichem Kauf zurück (Ausführung atomar).
        /// </summary>
        public bool TryPurchase(string id, PlayerWallet wallet)
        {
            if (wallet == null || !TryGet(id, out ShopItem item))
                return false;

            if (!item.IsCosmeticOnly && item.Kind != ShopItemKind.BattlePassLevel)
                throw new System.InvalidOperationException(
                    $"Pay-to-Win-Verhinderung (NFR-14): {id} ist kein Kosmetik-Posten.");

            if (!wallet.TrySpend(item.Currency, item.Price))
                return false;

            return true;
        }

        public ShopItem[] GetItemsFor(CurrencyType currency)
        {
            var result = new List<ShopItem>();
            foreach (var item in _items.Values)
            {
                if (item.Currency == currency)
                    result.Add(item);
            }
            return result.ToArray();
        }
    }
}