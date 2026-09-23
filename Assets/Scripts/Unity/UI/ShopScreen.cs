using Paintball.Core.Economy;
using Paintball.Unity.Economy;
using Paintball.Unity.LiveOps;
using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Shop-Screen (UI-09): Kosmetik, Battle Pass, Währungen.
    /// Transparent, kein Pay-to-Win (NFR-14, M-01 bis M-06).
    /// Nutzt die autoritative Pure-C# Economy (PlayerWallet über den zentralen
    /// WalletHost, dateibasiert) und den Battle Pass über BattlePass.Instance.
    /// </summary>
    public sealed class ShopScreen : MonoBehaviour
    {
        private readonly ShopCatalog _catalog = new();

        [Header("Currency Display")]
        [SerializeField] private TMPro.TextMeshProUGUI _softCurrencyText;
        [SerializeField] private TMPro.TextMeshProUGUI _premiumCurrencyText;

        [Header("Battle Pass")]
        [SerializeField] private Slider _battlePassProgressBar;
        [SerializeField] private TMPro.TextMeshProUGUI _battlePassTierText;

        [Header("Shop Tabs")]
        [SerializeField] private GameObject _skinsTab;
        [SerializeField] private GameObject _markersTab;
        [SerializeField] private GameObject _emotesTab;
        [SerializeField] private GameObject _battlePassTab;

        [Header("Items")]
        [SerializeField] private Transform _shopItemListParent;
        [SerializeField] private GameObject _shopItemPrefab;

        [Header("Buttons")]
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _purchaseButton;

        private string _selectedItemId;

        public PlayerWallet Wallet => WalletHost.Instance != null ? WalletHost.Instance.Wallet : null;
        public ShopCatalog Catalog => _catalog;

        private void Start()
        {
            if (_selectedItemId == null)
            {
                LoadCatalog();
            }

            if (_backButton != null) _backButton.onClick.AddListener(OnBack);
            if (_purchaseButton != null) _purchaseButton.onClick.AddListener(OnPurchase);

            UpdateCurrencyDisplay();
            UpdateBattlePassDisplay();
            PopulateShopItems();
        }

        private void LoadCatalog()
        {
            _catalog.Add(new ShopItem("skin_neon", "Skin: Neon-Gelb", ShopItemKind.Skin, CurrencyType.Soft, 250));
            _catalog.Add(new ShopItem("skin_camo", "Skin: Camo-Grün", ShopItemKind.Skin, CurrencyType.Soft, 300));
            _catalog.Add(new ShopItem("paint_neonpink", "Paint: Neon-Pink", ShopItemKind.PaintColor, CurrencyType.Soft, 150));
            _catalog.Add(new ShopItem("emote_dab", "Emote: Dab", ShopItemKind.Emote, CurrencyType.Premium, 120));
            _catalog.Add(new ShopItem("skin_gold", "Skin: Gold", ShopItemKind.Skin, CurrencyType.Premium, 500));
            _catalog.Add(new ShopItem("bp_level", "Battle Pass Level", ShopItemKind.BattlePassLevel, CurrencyType.Premium, 150));

            _selectedItemId = string.Empty;
        }

        private void UpdateCurrencyDisplay()
        {
            var wallet = Wallet;
            if (wallet == null) return;

            if (_softCurrencyText != null)
                _softCurrencyText.text = wallet.SoftBalance.ToString();
            if (_premiumCurrencyText != null)
                _premiumCurrencyText.text = wallet.PremiumBalance.ToString();
        }

        private void UpdateBattlePassDisplay()
        {
            var battlePass = BattlePass.Instance;
            if (battlePass == null) return;

            int tier = battlePass.CurrentTier;
            int xpInto = battlePass.CurrentXp;

            if (_battlePassProgressBar != null)
                _battlePassProgressBar.value = (float)xpInto / Mathf.Max(1, battlePass.GetNextTierXpRequired());
            if (_battlePassTierText != null)
                _battlePassTierText.text = $"Tier {tier}";
        }

        private void PopulateShopItems()
        {
            if (_shopItemListParent == null || _shopItemPrefab == null) return;

            foreach (var item in _catalog.AllItems)
            {
                GameObject itemObj = Instantiate(_shopItemPrefab, _shopItemListParent);
                var text = itemObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (text != null)
                    text.text = item.IsCosmeticOnly
                        ? $"{item.DisplayName} [{item.Price} {item.Currency}]"
                        : $"{item.DisplayName} [{item.Price} {item.Currency}]*";

                var button = itemObj.GetComponent<Button>();
                if (button != null)
                {
                    string id = item.Id;
                    button.onClick.AddListener(() => OnItemSelected(id));
                }
            }
        }

        private void OnItemSelected(string itemId)
        {
            _selectedItemId = itemId;
            Debug.Log($"[Shop] Item ausgewählt: {itemId}");
        }

        private void OnPurchase()
        {
            var wallet = Wallet;
            if (_selectedItemId == null || wallet == null || !_catalog.TryGet(_selectedItemId, out ShopItem item))
                return;

            if (_catalog.TryPurchase(item.Id, wallet))
            {
                WalletHost.Instance?.Persist();
                Debug.Log($"[Shop] Gekauft: {item.DisplayName} für {item.Price} {item.Currency}");
                UpdateCurrencyDisplay();
            }
            else
            {
                Debug.Log("[Shop] Nicht genug Währung");
            }
        }

        private void OnBack()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}