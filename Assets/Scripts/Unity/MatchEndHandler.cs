using Paintball.Core.Match;
using Paintball.Core.Progression;
using Paintball.Unity.LiveOps;
using Paintball.Unity.Match;
using Paintball.Unity.UI;
using UnityEngine;

namespace Paintball.Unity
{
    /// <summary>
    /// Überwacht das Match-Ende (FR-32) und zeigt den Ergebnisbildschirm.
    /// Vergibt XP und MMR nur nach validiertem Abschluss und aus echten
    /// Match-Statistiken (FR-44, FR-40, NFR-10). Baut das getestete
    /// GameResult auf und überträgt es per statischem Transfer an den
    /// ResultScreen (Szene-Wechsel) – kein Debug.Log-Fake.
    /// </summary>
    public sealed class MatchEndHandler : MonoBehaviour
    {
        private bool _hasShownResults;

        public bool HasShownResults => _hasShownResults;

        private void Update()
        {
            var modeManager = GameModeManager.Instance;
            if (modeManager == null || _hasShownResults) return;

            switch (modeManager.ActiveGameMode)
            {
                case GameModeManager.GameMode.TeamDeathmatch:
                    CheckTdmFinished(modeManager.TdmRules);
                    break;
                case GameModeManager.GameMode.Deathmatch:
                    CheckDeathmatchFinished(modeManager.DeathmatchRules);
                    break;
            }
        }

        private void CheckTdmFinished(TeamDeathmatchRules rules)
        {
            if (rules == null || rules.Phase != MatchPhase.Finished) return;
            _hasShownResults = true;

            int winnerTeamId = rules.WinnerTeamId ?? -1;
            AwardMatchProgress(
                modeManager: GameModeManager.Instance,
                winningTeamId: winnerTeamId);
        }

        private void CheckDeathmatchFinished(DeathmatchRules rules)
        {
            if (rules == null || rules.Phase != MatchPhase.Finished) return;
            _hasShownResults = true;

            int winner = rules.WinnerPlayerId ?? -1;
            int localTeam = LobbyConfig.LocalTeamId;
            AwardMatchProgress(
                modeManager: GameModeManager.Instance,
                winningTeamId: winner == 0 ? localTeam : 1 - localTeam);
        }

        private void AwardMatchProgress(GameModeManager modeManager, int winningTeamId)
        {
            var profile = Account.PlayerProfile.Instance;
            if (profile == null) return;

            int localTeam = LobbyConfig.LocalTeamId;
            float duration = MatchManager.Instance != null
                ? MatchManager.Instance.MatchTimeElapsed
                : Time.timeSinceLevelLoad;

            var completion = new MatchCompletionService(
                    tracker: modeManager.Stats,
                    account: profile.CoreAccount,
                    winningTeamId: winningTeamId,
                    localPlayerId: 0,
                    localTeamId: localTeam)
                .Complete(matchDurationMinutes: duration / 60d,
                    abandoned: profile.IsLeaver);

            if (!completion.RewardsGranted)
            {
                Debug.LogWarning($"[MatchEnd] Keine Belohnung: {completion.Reason}");
                profile.IsLeaver = false;
            }

            FeedLiveOps(modeManager, completion);

            GameResult result = BuildGameResult(winningTeamId);
            ResultScreen.LastResult = result;
            ResultScreen.LastSummary = completion.Summary;
            ResultScreen.LastStats = modeManager.Stats;
            ResultScreen.LastMmrChange = completion.RewardsGranted ? completion.MmrChange : 0;
            ResultScreen.LastXpGained = completion.RewardsGranted ? completion.XpGained : 0;

            SessionHost.Instance?.EndMatch(Time.time);

            LoadResultsScene();
        }

        /// <summary>Freigeschaltete Belohnungen in Live-Ops Systeme überführen (M-03, FR-42).</summary>
        private static void FeedLiveOps(GameModeManager modeManager, MatchCompletionResult completion)
        {
            var local = modeManager.Stats.GetStats(0);

            var challenges = ChallengeSystem.Instance;
            if (challenges != null)
            {
                challenges.RegisterProgress(ChallengeType.Eliminations, local.Eliminations);
                challenges.RegisterProgress(ChallengeType.ObjectiveScore, local.ObjectiveScore);
                challenges.RegisterProgress(ChallengeType.Wins, completion.Won ? 1 : 0);
                challenges.RegisterProgress(ChallengeType.MatchesPlayed, 1);
                challenges.RegisterProgress(ChallengeType.Accuracy, Mathf.RoundToInt(local.Accuracy * 100f));
            }

            if (completion.RewardsGranted && completion.XpGained > 0)
                BattlePass.Instance?.AddXp(completion.XpGained);
        }

        private static GameResult BuildGameResult(int winningTeamId)
        {
            var result = new GameResult();
            int localTeam = LobbyConfig.LocalTeamId;
            result.RecordPlayer(0, localTeam);
            result.RecordPlayer(1, 1 - localTeam);
            result.RecordPlayer(2, 1 - localTeam);

            float duration = MatchManager.Instance != null
                ? MatchManager.Instance.MatchTimeElapsed
                : Time.timeSinceLevelLoad;

            result.Finish(winningTeamId, duration);
            return result;
        }

        private static void LoadResultsScene()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("Result");
        }
    }
}