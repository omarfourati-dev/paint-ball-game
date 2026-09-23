using System.IO;
using Paintball.Core.Match;
using Paintball.Core.Persistence;
using Paintball.Core.Progression;
using UnityEngine;

namespace Paintball.Unity.Account
{
    /// <summary>
    /// Spieler-Profil und Account-System (FR-48).
    /// Verwendet den getesteten Core-PlayerAccount für alle Account-Daten
    /// (XP, Level, MMR, Statistik, Persistenz). Account-Persistenz liegt als
    /// Datei im persistentDataPath (Core LocalPersistence, getestet); alte
    /// PlayerPrefs-Daten werden einmalig migriert. Kosmetik (Farbe, Skin,
    /// Marker) wird als kompakter String abgelegt.
    /// </summary>
    public sealed class PlayerProfile : MonoBehaviour
    {
        private const string AccountLegacyKey = "PlayerAccount.V1";
        private const string CosmeticsSaveKey = "PlayerProfile.Cosmetics.V1";

        private static string AccountFilePath => Path.Combine(Application.persistentDataPath, "player-account.txt");

        public static PlayerProfile Instance { get; private set; }

        private PlayerAccount _account;
        private PlayerAccount Account => _account ??= PlayerAccount.CreateNew("Spieler");

        /// <summary>Der zugrunde liegende Core-Account (für die Abschluss-Pipeline).</summary>
        public PlayerAccount CoreAccount => Account;

        /// <summary>
        /// Session-Flag: Spieler hat das Match vorzeitig verlassen (FR-31) –
        /// dadurch wird die Belohnung beim Match-Ende verweigert.
        /// </summary>
        public bool IsLeaver { get; set; }

        [Header("Cosmetics")]
        [SerializeField] private Color _paintColor = Color.red;
        [SerializeField] private string _currentSkinId;
        [SerializeField] private string _currentMarkerId = "marker_default";

        public string PlayerId => Account.PlayerId;
        public string DisplayName => Account.DisplayName;
        public int Level => Account.Level;
        public int TotalXp => Account.TotalXp;
        public int Mmr => Account.Mmr;
        public Color PaintColor => _paintColor;

        public int TotalMatches => Account.TotalMatches;
        public int TotalWins => Account.TotalWins;
        public int TotalEliminations => Account.TotalEliminations;
        public int TotalDeaths => Account.TotalDeaths;
        public float TotalAccuracy => Account.Accuracy;
        public float KillDeathRatio => Account.KillDeathRatio;
        public float WinRate => Account.WinRate;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadProfile();
        }

        public void AddXp(int amount)
        {
            Account.AddXp(amount);
            SaveProfile();
        }

        public void UpdateStats(int eliminations, int deaths, float accuracy, bool won)
        {
            var stats = new PlayerMatchStats
            {
                Eliminations = eliminations,
                Deaths = deaths,
                Accuracy = accuracy,
                Won = won
            };
            Account.ApplyMatchResult(stats);
            SaveProfile();
        }

        public void UpdateMmr(int opponentMmr, float result)
        {
            Account.UpdateMmr(opponentMmr, result);
            SaveProfile();
        }

        /// <summary>
        /// Wendet das Evaluator-Ergebnis atomar an: XP + Stats + MMR in einem
        /// Schritt (FIX: MMR vorher mit sich selbst als Gegner aktualisiert).
        /// </summary>
        public void ApplyMatchOutcome(OutcomeSummary summary, int localPlayerId)
        {
            if (summary == null) return;
            summary.ApplyLocalPlayer(Account, localPlayerId);
            SaveProfile();
        }

        public void SetPaintColor(Color color)
        {
            _paintColor = color;
            SaveProfile();
        }

        public void SetDisplayName(string name)
        {
            Account.SetDisplayName(name);
            SaveProfile();
        }

        /// <summary>
        /// Setzt das Konto nach der DSGVO-Löschung (NFR-12) neu auf: neuer Spieler,
        /// neutrale Kosmetik, Datei + PlayerPrefs-Migration werden zurückgesetzt.
        /// </summary>
        public void ResetAccount()
        {
            _account = PlayerAccount.CreateNew("Spieler");
            _paintColor = Color.red;
            _currentSkinId = null;
            _currentMarkerId = "marker_default";
            LocalPersistence.Delete(AccountFilePath);
            PlayerPrefs.DeleteKey(AccountLegacyKey);
            PlayerPrefs.DeleteKey(CosmeticsSaveKey);
            PlayerPrefs.Save();
        }

        private void SaveProfile()
        {
            LocalPersistence.SaveAccount(_account, AccountFilePath);
            PlayerPrefs.SetString(AccountLegacyKey, Account.Serialize());
            PlayerPrefs.SetString(CosmeticsSaveKey, SerializeCosmetics());
            PlayerPrefs.Save();
        }

        private void LoadProfile()
        {
            var fromFile = LocalPersistence.LoadAccount(AccountFilePath);
            if (fromFile != null)
            {
                _account = fromFile;
                return;
            }

            string legacy = PlayerPrefs.GetString(AccountLegacyKey, null);
            _account = PlayerAccount.Deserialize(legacy);
            if (!string.IsNullOrEmpty(legacy))
                LocalPersistence.SaveAccount(_account, AccountFilePath);

            LoadCosmetics(PlayerPrefs.GetString(CosmeticsSaveKey, null));
        }

        private string SerializeCosmetics()
        {
            string hex = ColorUtility.ToHtmlStringRGB(_paintColor);
            return $"paint={hex};skin={_currentSkinId ?? string.Empty};marker={_currentMarkerId ?? string.Empty}";
        }

        private void LoadCosmetics(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            _currentSkinId = null;
            _currentMarkerId = "marker_default";

            foreach (string raw in data.Split(';'))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;

                string key = raw.Substring(0, eq);
                string value = raw.Substring(eq + 1);

                switch (key)
                {
                    case "paint":
                        if (ColorUtility.TryParseHtmlString("#" + value, out Color color))
                            _paintColor = color;
                        break;
                    case "skin": _currentSkinId = value; break;
                    case "marker": _currentMarkerId = value; break;
                }
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause) SaveProfile();
        }

        private void OnApplicationQuit()
        {
            SaveProfile();
        }
    }
}