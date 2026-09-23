using System;
using System.Collections.Generic;

namespace Paintball.Core.Social
{
    /// <summary>Ein Mitglied einer Party (FR-30, FR-24: Team-Auswahl).</summary>
    public struct PartyMember
    {
        public string PlayerId;
        public string DisplayName;
        public bool IsLeader;
        public bool IsReady;

        /// <summary>Team-Zugehörigkeit für die Lobby (0 oder 1, -1 = nicht zugewiesen).</summary>
        public int TeamId;
    }

    /// <summary>
    /// Party-Lifecycle als pure Core-Logik (FR-30): Erstellen, Beitreten über
    /// Einladungscode, Austreten (Leader-Übergabe), Ready-Gating für
    /// gemeinsames Matchmaking. Keine Unity-Abhängigkeiten.
    /// </summary>
    public sealed class PartyLogic
    {
        private readonly List<PartyMember> _members = new();

        public string InviteCode { get; private set; }
        public IReadOnlyList<PartyMember> Members => _members;
        public int MemberCount => _members.Count;
        public bool IsInParty => !string.IsNullOrEmpty(InviteCode) && _members.Count > 0;

        /// <summary>Wird bei jeder Änderung ausgelöst.</summary>
        public event Action OnPartyChanged;

        public void CreateParty(string leaderPlayerId, string leaderDisplayName)
        {
            InviteCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            _members.Clear();
            _members.Add(new PartyMember
            {
                PlayerId = leaderPlayerId,
                DisplayName = leaderDisplayName ?? string.Empty,
                IsLeader = true,
                IsReady = false,
                TeamId = -1
            });
            OnPartyChanged?.Invoke();
        }

        public void JoinParty(string inviteCode, string playerId, string displayName)
        {
            if (string.IsNullOrEmpty(inviteCode)) return;
            if (Contains(playerId)) return;

            InviteCode = inviteCode;
            _members.Add(new PartyMember
            {
                PlayerId = playerId,
                DisplayName = displayName ?? string.Empty,
                IsLeader = false,
                IsReady = false,
                TeamId = -1
            });
            OnPartyChanged?.Invoke();
        }

        public bool LeaveParty(string playerId)
        {
            int removed = _members.RemoveAll(m => m.PlayerId == playerId);
            if (removed == 0) return false;

            if (_members.Count == 0)
            {
                InviteCode = null;
            }
            else if (IsLeader(playerId))
            {
                // Leader verlässt die Party → nächstes Mitglied übernimmt.
                var first = _members[0];
                first.IsLeader = true;
                _members[0] = first;
            }
            OnPartyChanged?.Invoke();
            return true;
        }

        public void SetReady(string playerId, bool ready)
        {
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].PlayerId != playerId) continue;

                var member = _members[i];
                member.IsReady = ready;
                _members[i] = member;
                OnPartyChanged?.Invoke();
                break;
            }
        }

        public bool AllReady()
        {
            if (_members.Count == 0) return false;
            foreach (var member in _members)
                if (!member.IsReady)
                    return false;
            return true;
        }

        public bool IsLeader(string playerId)
        {
            foreach (var member in _members)
                if (member.PlayerId == playerId)
                    return member.IsLeader;
            return false;
        }

        /// <summary>Weist einem Mitglied ein Team zu (FR-24): -1 = keins/entfernen, sonst 0/1.</summary>
        public void SetTeam(string playerId, int teamId)
        {
            if (teamId < 0) teamId = -1;
            if (teamId > 1) teamId = 1;

            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].PlayerId != playerId) continue;

                var member = _members[i];
                member.TeamId = teamId;
                _members[i] = member;
                OnPartyChanged?.Invoke();
                break;
            }
        }

        /// <summary>Team eines Mitglieds, -1 wenn nicht zugewiesen.</summary>
        public int GetTeam(string playerId)
        {
            foreach (var member in _members)
                if (member.PlayerId == playerId)
                    return member.TeamId;
            return -1;
        }

        /// <summary>Anzahl der Mitglieder im Team (0 oder 1).</summary>
        public int TeamSize(int teamId)
        {
            int count = 0;
            foreach (var member in _members)
                if (member.TeamId == teamId)
                    count++;
            return count;
        }

        private bool Contains(string playerId)
        {
            foreach (var member in _members)
                if (member.PlayerId == playerId)
                    return true;
            return false;
        }
    }
}