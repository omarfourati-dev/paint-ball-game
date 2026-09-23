using System.IO;
using Paintball.Core.Economy;
using Paintball.Core.Persistence;
using UnityEngine;

namespace Paintball.Unity.Economy
{
    /// <summary>
    /// Multi-Szenen-Host für die autoritative Core-<see cref="PlayerWallet"/> (M-01..M-03):
    /// persistiert die Brieftasche als Datei (wallet.txt) statt verstreuter PlayerPrefs
    /// und migriert alte „SoftCurrency"/„PremiumCurrency"-Keys einmalig. Alle Einnahmen
    /// (Challenges, Matches) und Ausgaben (Shop) laufen zentral über diesen Host.
    /// </summary>
    public sealed class WalletHost : MonoBehaviour
    {
        private const string SaveFileName = "wallet.txt";
        private const string LegacySoftKey = "SoftCurrency";
        private const string LegacyPremiumKey = "PremiumCurrency";

        public static WalletHost Instance { get; private set; }

        private static string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        private PlayerWallet _wallet;

        public PlayerWallet Wallet => _wallet;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _wallet = PlayerWallet.Deserialize(LocalPersistence.LoadText(SaveFilePath));
            if (_wallet.SoftBalance == 0)
            {
                int legacySoft = PlayerPrefs.GetInt(LegacySoftKey, 0);
                int legacyPremium = PlayerPrefs.GetInt(LegacyPremiumKey, 0);
                if (legacySoft > 0 || legacyPremium > 0)
                {
                    _wallet.Earn(CurrencyType.Soft, legacySoft);
                    _wallet.Earn(CurrencyType.Premium, legacyPremium);
                    PlayerPrefs.DeleteKey(LegacySoftKey);
                    PlayerPrefs.DeleteKey(LegacyPremiumKey);
                    PlayerPrefs.Save();
                }
            }
        }

        /// <summary>Gutschrift auf das Konto mit sofortiger Persistenz.</summary>
        public int Earn(CurrencyType type, int amount)
        {
            int balance = _wallet.Earn(type, amount);
            Persist();
            return balance;
        }

        /// <summary>Belastung mit sofortiger Persistenz. true = gebucht.</summary>
        public bool TrySpend(CurrencyType type, int amount)
        {
            bool success = _wallet.TrySpend(type, amount);
            if (success) Persist();
            return success;
        }

        public void Persist()
        {
            LocalPersistence.SaveText(SaveFilePath, _wallet.Serialize());
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}