using System.IO;
using Paintball.Core.Persistence;
using Paintball.Core.Progression;
using Paintball.Unity.Economy;
using UnityEngine;

namespace Paintball.Unity.LiveOps
{
    /// <summary>
    /// Herausforderungen und Quests (FR-42): Tägliche/wöchentliche Ziele.
    /// Die Fortschrittslogik läuft im getesteten Core <see cref="ChallengeEvaluator"/>
    /// (inkl. Einmal-Claim) und wird als Datei (challenges.txt) persistiert; Claims
    /// schreiben XP auf das Profil und Coins zentral in den <see cref="WalletHost"/> –
    /// keine verstreuten PlayerPrefs mehr. Saisonale Inhalte bleiben serverseitig
    /// steuerbar (FR-47).
    /// </summary>
    public sealed class ChallengeSystem : MonoBehaviour
    {
        private const string SaveFileName = "challenges.txt";
        private const string TokenPrefix = "token";

        public static ChallengeSystem Instance { get; private set; }

        private static string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        private ChallengeEvaluator _evaluator;
        private string _lastToken = string.Empty;

        public IReadOnlyCollection<ChallengeDefinition> Challenges => _evaluator.Challenges;

        public event System.Action OnProgressChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            Load();
        }

        /// <summary>Match-Feed: registriert Fortschritt für alle passenden Ziele (Kills, Matches, …).</summary>
        public void RegisterProgress(ChallengeType type, int amount)
        {
            _evaluator.RegisterProgress(type, amount);
            Persist();
            OnProgressChanged?.Invoke();
        }

        public int GetProgress(string challengeId) => _evaluator.ProgressOf(challengeId);

        public bool IsComplete(string challengeId) => _evaluator.IsComplete(challengeId);

        /// <summary>
        /// Fordert die Belohnung eines abgeschlossenen Ziels an: XP → Profil,
        /// Coins → WalletHost, danach Einmal-Sperre im Evaluator.
        /// </summary>
        public bool ClaimReward(string challengeId)
        {
            if (!_evaluator.TryClaim(challengeId, out int xp, out int coins)) return false;

            var profile = Account.PlayerProfile.Instance;
            if (xp > 0 && profile != null) profile.AddXp(xp);
            if (coins > 0) WalletHost.Instance?.Earn(Paintball.Core.Economy.CurrencyType.Soft, coins);

            Persist();
            OnProgressChanged?.Invoke();
            return true;
        }

        private void Load()
        {
            string token = CurrentToken();
            string data = LocalPersistence.LoadText(SaveFilePath);
            _lastToken = ExtractToken(data);

            if (string.IsNullOrEmpty(_lastToken) || _lastToken != token || string.IsNullOrEmpty(_lastToken.Trim()))
            {
                Generate(token);
                return;
            }

            _evaluator = ChallengeEvaluator.Deserialize(StripToken(data));
            if (_evaluator.Challenges.Count == 0)
                Generate(token);
        }

        private void Generate(string token)
        {
            _lastToken = token;
            _evaluator = new ChallengeEvaluator();
            _evaluator.AddChallenge(ChallengeType.Eliminations, 10, 100, 50);
            _evaluator.AddChallenge(ChallengeType.MatchesPlayed, 3, 75, 30);
            _evaluator.AddChallenge(ChallengeType.Wins, 5, 500, 200);
            Persist();
        }

        private static string CurrentToken()
        {
            return System.DateTime.UtcNow.ToString("yyyy-MM-dd");
        }

        private static string ExtractToken(string data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;
            foreach (string raw in data.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith(TokenPrefix + "=")) return line.Substring(TokenPrefix.Length + 1);
            }
            return string.Empty;
        }

        private static string StripToken(string data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;
            var lines = new System.Collections.Generic.List<string>();
            foreach (string raw in data.Split('\n'))
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith(TokenPrefix + "=")) continue;
                lines.Add(trimmed);
            }
            return string.Join("\n", lines);
        }

        private void Persist()
        {
            LocalPersistence.SaveText(SaveFilePath, $"{TokenPrefix}={_lastToken}\n{_evaluator.Serialize()}");
        }
    }
}