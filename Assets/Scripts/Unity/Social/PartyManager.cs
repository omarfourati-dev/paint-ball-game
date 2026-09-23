using System;
using System.Collections.Generic;
using Paintball.Core.Social;
using UnityEngine;

namespace Paintball.Unity.Social
{
    /// <summary>
    /// Party-System (FR-30): echte Daten aus der Core-PartyLogic –
    /// Einladungen, gemeinsames Matchmaking, Lobby-Verbleib.
    /// </summary>
    public sealed class PartyManager : MonoBehaviour
    {
        private const string SaveKey = "PartyManager.V1";

        public static PartyManager Instance { get; private set; }

        private PartyLogic _party = new();
        private string _localPlayerId;

        public string PartyId => _party.InviteCode;
        public IReadOnlyList<PartyMember> Members => _party.Members;
        public int MemberCount => _party.MemberCount;
        public bool IsInParty => _party.IsInParty;

        public event Action OnPartyChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _party.OnPartyChanged += () =>
            {
                PlayerPrefs.SetString(SaveKey, _party.InviteCode ?? string.Empty);
                PlayerPrefs.Save();
                OnPartyChanged?.Invoke();
            };

            _localPlayerId = PlayerPrefs.GetString("LocalPlayerId", Guid.NewGuid().ToString());
            PlayerPrefs.SetString("LocalPlayerId", _localPlayerId);
        }

        public void CreateParty(string leaderDisplayName)
        {
            _party.CreateParty(_localPlayerId, leaderDisplayName);
        }

        public void JoinParty(string inviteCode, string displayName)
        {
            _party.JoinParty(inviteCode, _localPlayerId, displayName);
        }

        public void LeaveParty()
        {
            _party.LeaveParty(_localPlayerId);
        }

        public void SetReady(bool ready)
        {
            _party.SetReady(_localPlayerId, ready);
        }

        public bool AllReady() => _party.AllReady();
        public bool IsLeader() => _party.IsLeader(_localPlayerId);
        public string GetInviteCode() => _party.InviteCode;

        public int LocalTeamId => _party.GetTeam(_localPlayerId);

        /// <summary>Weist den lokalen Spieler einem Team zu (FR-24), -1 = nicht zugewiesen.</summary>
        public void SetTeam(int teamId) => _party.SetTeam(_localPlayerId, teamId);

        public int TeamSize(int teamId) => _party.TeamSize(teamId);
    }
}