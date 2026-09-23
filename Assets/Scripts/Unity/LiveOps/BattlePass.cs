using System;
using System.Collections.Generic;
using Paintball.Core.LiveOps;
using Paintball.Core.Persistence;
using UnityEngine;

namespace Paintball.Unity.LiveOps
{
    /// <summary>
    /// Battle Pass (M-03): Kosmetische Belohnungen, serverseitig steuerbar (FR-47).
    /// Die Fortschrittslogik läuft im Core (<see cref="BattlePassProgress"/>) und wird
    /// als Datei (battle-pass.txt) statt PlayerPrefs persistiert. Kein Pay-to-Win –
    /// nur Kosmetik (NFR-14).
    /// </summary>
    public sealed class BattlePass : MonoBehaviour
    {
        private const string SaveFilePath = "battle-pass.txt";

        public static BattlePass Instance { get; private set; }

        private readonly BattlePassProgress _progress = new();

        public bool HasPremiumPass => _progress.HasPremiumPass;
        public int CurrentTier => _progress.CurrentTier;
        public int CurrentXp => _progress.CurrentXp;
        public IReadOnlyList<BattlePassTier> Tiers => _progress.Tiers;

        public event Action<int> OnTierUp;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _progress.OnTierUp += tier => OnTierUp?.Invoke(tier);

            if (Tiers.Count == 0)
                GenerateDefaultTiers();
            LoadState();
        }

        public void AddXp(int amount)
        {
            int levelUps = _progress.AddXp(amount);
            if (levelUps > 0)
                SaveState();
        }

        public bool HasNextTier() => _progress.HasNextTier();

        public int GetNextTierXpRequired() => _progress.GetNextTierXpRequired();

        public void PurchasePremiumPass()
        {
            _progress.SetPremium(true);
            SaveState();
        }

        private void GenerateDefaultTiers()
        {
            string[] rewards = { "Skin: Bronze", "Paint: Grün", "Skin: Silber", "Paint: Lila", "Skin: Gold", "Emote: Jubel", "Skin: Platin", "Paint: Neon", "Skin: Diamant", "Exclusiv-Skin" };
            var tiers = new List<BattlePassTier>();
            for (int i = 0; i < 10; i++)
            {
                tiers.Add(new BattlePassTier
                {
                    Tier = i + 1,
                    XpRequired = 1000 + i * 250,
                    RewardName = rewards[i],
                    IsPremiumReward = i % 3 == 2
                });
            }
            _progress.SetTiers(tiers);
        }

        private void LoadState()
        {
            string filePath = GetFilePath();
            if (LocalPersistence.Exists(filePath))
            {
                var restored = BattlePassProgress.Deserialize(LocalPersistence.LoadText(filePath));
                _progress.SetTier(restored.CurrentTier);
                _progress.RestoreXp(restored.CurrentXp);
                _progress.SetPremium(restored.HasPremiumPass);
                return;
            }

            MigrateLegacyPlayerPrefs();
        }

        private void MigrateLegacyPlayerPrefs()
        {
            int legacyTier = PlayerPrefs.GetInt("BattlePassTier", 0);
            int legacyXp = PlayerPrefs.GetInt("BattlePassXp", 0);
            bool legacyPremium = PlayerPrefs.GetInt("BattlePassPremium", 0) == 1;

            if (legacyTier > 0 || legacyXp > 0 || legacyPremium)
            {
                _progress.SetTier(legacyTier);
                _progress.SetPremium(legacyPremium);
                _progress.RestoreXp(legacyXp);
                PlayerPrefs.DeleteKey("BattlePassTier");
                PlayerPrefs.DeleteKey("BattlePassXp");
                PlayerPrefs.DeleteKey("BattlePassPremium");
                PlayerPrefs.Save();
            }
        }

        private void SaveState()
        {
            LocalPersistence.SaveText(GetFilePath(), _progress.Serialize());
        }

        private static string GetFilePath()
        {
            return System.IO.Path.Combine(Application.persistentDataPath, SaveFilePath);
        }
    }
}