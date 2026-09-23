using System;
using System.Collections;
using System.Collections.Generic;
using Paintball.Core.Match;
using Paintball.Unity.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Lobby-Screen (FR-24, UI-04): Modus- und Kartenwahl (FR-20/21), Einladungscode,
    /// Spielerliste, Bereitschaft, Countdown. Die Einstellungen fließen über
    /// LobbyConfig in die CustomGameRules und werden im Match angewendet.
    /// </summary>
    public sealed class LobbyScreen : MonoBehaviour
    {
        private static readonly string[] MapNames = { "Lagerhaus", "Wald", "Arena" };

        [Header("Lobby Info")]
        [SerializeField] private TMPro.TextMeshProUGUI _lobbyNameText;
        [SerializeField] private TMPro.TextMeshProUGUI _inviteCodeText;
        [SerializeField] private TMPro.TextMeshProUGUI _regionText;
        [SerializeField] private TMPro.TextMeshProUGUI _playerCountText;

        [Header("Match Settings")]
        [SerializeField] private TMPro.TMP_Dropdown _modeDropdown;
        [SerializeField] private TMPro.TMP_Dropdown _mapDropdown;
        [SerializeField] private TMPro.TMP_Dropdown _teamDropdown;

        [Header("Player List")]
        [SerializeField] private Transform _playerListParent;
        [SerializeField] private GameObject _playerEntryPrefab;

        [Header("Buttons")]
        [SerializeField] private Button _readyButton;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private Button _startButton;

        [Header("Countdown")]
        [SerializeField] private TMPro.TextMeshProUGUI _countdownText;

        private CustomGameRules Rules => LobbyConfig.Rules;
        private bool _isReady;
        private bool _isStarting;

        private void Start()
        {
            if (_readyButton != null) _readyButton.onClick.AddListener(OnReadyPressed);
            if (_leaveButton != null) _leaveButton.onClick.AddListener(OnLeave);
            if (_startButton != null)
            {
                _startButton.onClick.AddListener(OnStart);
                _startButton.interactable = false;
            }

            InitModeDropdown();
            InitMapDropdown();
            InitTeamDropdown();
            RefreshLobbyInfo();
            UpdatePlayerList();
        }

        private void InitModeDropdown()
        {
            if (_modeDropdown == null) return;

            var modes = Enum.GetNames(typeof(CustomMatchMode));
            _modeDropdown.ClearOptions();
            _modeDropdown.AddOptions(new List<string>(modes));
            _modeDropdown.value = Mathf.Clamp((int)Rules.Mode, 0, modes.Length - 1);
            _modeDropdown.onValueChanged.AddListener(i => Rules.SetMode((CustomMatchMode)i));
        }

        private void InitMapDropdown()
        {
            if (_mapDropdown == null) return;

            _mapDropdown.ClearOptions();
            _mapDropdown.AddOptions(new List<string>(MapNames));
            _mapDropdown.value = Mathf.Clamp(Rules.MapIndex, 0, MapNames.Length - 1);
            _mapDropdown.onValueChanged.AddListener(i => Rules.SetMapIndex(i));
        }

        private void InitTeamDropdown()
        {
            if (_teamDropdown == null) return;

            _teamDropdown.ClearOptions();
            _teamDropdown.AddOptions(new List<string> { "Auto", "Team 1", "Team 2" });

            var party = Social.PartyManager.Instance;
            int current = party != null ? party.LocalTeamId : LobbyConfig.LocalTeamId;
            _teamDropdown.value = Mathf.Clamp(current + 1, 0, 2);
            _teamDropdown.onValueChanged.AddListener(OnTeamChanged);
        }

        private void OnTeamChanged(int index)
        {
            int teamId = index - 1; // 0 = Auto(-1), 1 = Team 1, 2 = Team 2
            var party = Social.PartyManager.Instance;
            party?.SetTeam(teamId);
            LobbyConfig.LocalTeamId = teamId < 0 ? 0 : teamId;

            if (_regionText != null)
                _regionText.text = teamId < 0 ? "Team: Auto" : $"Team: {teamId + 1}";
        }

        private void RefreshLobbyInfo()
        {
            var party = Social.PartyManager.Instance;
            if (party == null) return;

            if (_lobbyNameText != null)
                _lobbyNameText.text = party.IsInParty ? "Private Lobby (FR-20)" : "Schnelles Match";
            if (_inviteCodeText != null)
                _inviteCodeText.text = party.IsInParty && party.PartyId != null
                    ? $"Einladung: {party.PartyId}"
                    : "Einladung: -";
            if (_playerCountText != null)
                _playerCountText.text = $"{party.MemberCount} / {Rules.MaxPlayers} Spieler";

            if (_regionText != null)
                _regionText.text = Rules.FriendsOnly ? "Modus: Freunde-only" : $"Modus: {Rules.Mode} | Team: {(LobbyConfig.LocalTeamId + 1)}";

            if (_startButton != null)
                _startButton.interactable = party.IsLeader() && party.AllReady();
        }

        private void OnReadyPressed()
        {
            _isReady = !_isReady;
            if (_readyButton != null)
            {
                var text = _readyButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (text != null) text.text = _isReady ? "Bereit!" : "Bereitmachen";
            }

            var party = Social.PartyManager.Instance;
            if (party != null)
            {
                party.SetReady(_isReady);
                RefreshLobbyInfo();
            }
        }

        private void OnStart()
        {
            if (_isStarting) return;

            var party = Social.PartyManager.Instance;
            if (party != null && (!party.IsLeader() || !party.AllReady())) return;

            _isStarting = true;
            if (_startButton != null) _startButton.interactable = false;
            StartCoroutine(RunCountdown());
        }

        /// <summary>Lobby-Countdown (FR-24 UI-04): 3, 2, 1, dann Match-Szene.</summary>
        private IEnumerator RunCountdown()
        {
            for (int i = 3; i > 0; i--)
            {
                if (_countdownText != null) _countdownText.text = i.ToString();
                yield return new WaitForSeconds(1f);
            }

            if (_countdownText != null) _countdownText.text = "Go!";
            yield return new WaitForSeconds(0.3f);

            string hostId = Account.PlayerProfile.Instance?.PlayerId ?? "host";
            var host = Match.SessionHost.Instance;
            var rules = LobbyConfig.Rules;
            host?.BeginMatch(hostId, rules != null ? rules.Mode.ToString() : "Deathmatch", LobbyConfig.LocalTeamId, Time.time);

            UnityEngine.SceneManagement.SceneManager.LoadScene("Match");
        }

        private void OnLeave()
        {
            var party = Social.PartyManager.Instance;
            party?.LeaveParty();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        public void SetLobbyInfo(string lobbyName, string inviteCode, string region, int playerCount, int maxPlayers)
        {
            if (_lobbyNameText != null) _lobbyNameText.text = lobbyName;
            if (_inviteCodeText != null) _inviteCodeText.text = $"Einladung: {inviteCode}";
            if (_regionText != null) _regionText.text = $"Region: {region}";
            if (_playerCountText != null) _playerCountText.text = $"{playerCount} / {maxPlayers} Spieler";
        }

        private void UpdatePlayerList()
        {
            if (_playerListParent == null || _playerEntryPrefab == null) return;

            var party = Social.PartyManager.Instance;
            if (party == null) return;

            foreach (Transform child in _playerListParent)
                Destroy(child.gameObject);

            foreach (var member in party.Members)
            {
                GameObject entry = Instantiate(_playerEntryPrefab, _playerListParent);
                var text = entry.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (text != null)
                {
                    string teamTag = member.TeamId >= 0 ? $" · Team {member.TeamId + 1}" : "";
                    text.text = member.DisplayName + (member.IsReady ? " ✓" : "") +
                                (member.IsLeader ? " (Host)" : "") + teamTag;
                }
            }
        }
    }
}