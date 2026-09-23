using System;
using System.Collections.Generic;

namespace Paintball.Core.Social
{
    /// <summary>Ein Freundeseintrag (FR-50).</summary>
    public struct FriendEntry
    {
        public string PlayerId;
        public string DisplayName;
        public bool IsOnline;
        public string Status;
    }

    /// <summary>
    /// Freundesliste als pure Core-Logik (FR-50): CRUD, Online-Status,
    /// Change-Event und persistente Serialisierung. Keine Unity-Abhängigkeiten.
    /// </summary>
    public sealed class FriendRepository
    {
        public const char LineSeparator = '\n';
        public const char FieldSeparator = '|';

        private readonly List<FriendEntry> _friends = new();

        public IReadOnlyList<FriendEntry> Friends => _friends;

        /// <summary>Wird bei jeder Änderung an der Liste ausgelöst.</summary>
        public event Action OnFriendsChanged;

        public void AddFriend(string playerId, string displayName, bool isOnline = false, string status = "offline")
        {
            if (string.IsNullOrEmpty(playerId)) return;
            if (IsFriend(playerId)) return;

            _friends.Add(new FriendEntry
            {
                PlayerId = playerId,
                DisplayName = displayName ?? string.Empty,
                IsOnline = isOnline,
                Status = status ?? "offline"
            });
            OnFriendsChanged?.Invoke();
        }

        public bool RemoveFriend(string playerId)
        {
            int removed = _friends.RemoveAll(f => f.PlayerId == playerId);
            if (removed > 0)
                OnFriendsChanged?.Invoke();
            return removed > 0;
        }

        public bool IsFriend(string playerId)
        {
            foreach (var f in _friends)
                if (f.PlayerId == playerId)
                    return true;
            return false;
        }

        public void SetStatus(string playerId, bool online, string status)
        {
            if (status == null) status = online ? "online" : "offline";

            for (int i = 0; i < _friends.Count; i++)
            {
                if (_friends[i].PlayerId != playerId) continue;

                var entry = _friends[i];
                entry.IsOnline = online;
                entry.Status = status;
                _friends[i] = entry;
                OnFriendsChanged?.Invoke();
                break;
            }
        }

        public string Serialize()
        {
            var parts = new List<string>(_friends.Count);
            foreach (var f in _friends)
            {
                parts.Add(string.Join(FieldSeparator,
                    Escape(f.PlayerId), Escape(f.DisplayName),
                    f.IsOnline ? "1" : "0", Escape(f.Status)));
            }
            return string.Join(LineSeparator, parts);
        }

        public static FriendRepository Deserialize(string data)
        {
            var repo = new FriendRepository();
            if (string.IsNullOrEmpty(data)) return repo;

            foreach (string line in data.Split(LineSeparator))
            {
                string[] fields = line.Split(FieldSeparator);
                if (fields.Length < 3) continue;

                repo._friends.Add(new FriendEntry
                {
                    PlayerId = Unescape(fields[0]),
                    DisplayName = Unescape(fields[1]),
                    IsOnline = fields[2] == "1",
                    Status = fields.Length > 3 ? Unescape(fields[3]) : "offline"
                });
            }
            return repo;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("%", "%25")
                .Replace(FieldSeparator.ToString(), "%7C")
                .Replace(LineSeparator.ToString(), "%0A");
        }

        private static string Unescape(string value)
        {
            return (value ?? string.Empty)
                .Replace("%0A", LineSeparator.ToString())
                .Replace("%7C", FieldSeparator.ToString())
                .Replace("%25", "%");
        }
    }
}