using System.Collections.Generic;
using Paintball.Core.Social;
using UnityEngine;

namespace Paintball.Unity.Social
{
    /// <summary>
    /// Freundesliste (FR-50): echte Daten aus dem Core-FriendRepository
    /// inkl. Persistenz und Online-Status. Einladungen werden als Daten
    /// festgehalten und über ein Event gemeldet (kein Debug.Log-Fake).
    /// </summary>
    public sealed class FriendsManager : MonoBehaviour
    {
        private const string SaveKey = "FriendsManager.V1";

        public static FriendsManager Instance { get; private set; }

        private FriendRepository _repository = new();
        private readonly List<string> _pendingInvites = new();

        public FriendRepository Repository => _repository;
        public IReadOnlyList<FriendEntry> Friends => _repository.Friends;
        public IReadOnlyList<string> PendingInvites => _pendingInvites;

        public event System.Action OnFriendsChanged;
        public event System.Action OnInvitesChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _repository = FriendRepository.Deserialize(PlayerPrefs.GetString(SaveKey, null));
            if (_repository == null) _repository = new FriendRepository();

            _repository.OnFriendsChanged += SaveAndNotify;
        }

        public void AddFriend(string playerId, string displayName)
        {
            _repository.AddFriend(playerId, displayName);
            SaveAndNotify();
        }

        public void RemoveFriend(string playerId)
        {
            _repository.RemoveFriend(playerId);
            SaveAndNotify();
        }

        public void SetFriendStatus(string playerId, bool online, string status)
        {
            _repository.SetStatus(playerId, online, status);
            SaveAndNotify();
        }

        public bool IsFriend(string playerId) => _repository.IsFriend(playerId);

        public void SendInvite(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            if (_pendingInvites.Contains(playerId)) return;

            _pendingInvites.Add(playerId);
            OnInvitesChanged?.Invoke();
        }

        public void RemoveInvite(string playerId)
        {
            if (_pendingInvites.Remove(playerId))
                OnInvitesChanged?.Invoke();
        }

        private void SaveAndNotify()
        {
            PlayerPrefs.SetString(SaveKey, _repository.Serialize());
            PlayerPrefs.Save();
            OnFriendsChanged?.Invoke();
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause) SaveAndNotify();
        }

        private void OnApplicationQuit()
        {
            SaveAndNotify();
        }
    }
}