using System;
using System.Collections.Generic;
using UnityEngine;
using Paintball.Core.Social;

namespace Paintball.Unity.Social
{
    /// <summary>
    /// In-Match-Kommunikation via Quick-Chat, Emotes und Ping-System (FR-51).
    /// Verdrahtet mit Core QuickChatMessages + ChatFilter.
    /// Kein offener Text-Chat aus Jugendschutzgründen.
    /// </summary>
    public sealed class QuickChatSystem : MonoBehaviour
    {
        public static QuickChatSystem Instance { get; private set; }

        public enum MessageType { QuickChat, Emote, Ping }

        public enum PingType { EnemySpotted, HelpNeeded, AttackHere, DefendHere, Objective }

        private readonly QuickChatMessages _coreMessages = new();
        private readonly ChatFilter _filter = new();

        private static readonly string[] Emotes =
        {
            "\U0001F44B", "\U0001F44D", "\U0001F389", "\U0001F91D", "\U0001F60E", "\U0001F4AA"
        };

        public event Action<int, string, string> OnQuickChatMessage;
        public event Action<int, string> OnEmote;
        public event Action<Vector3, PingType> OnPing;

        private readonly Dictionary<QuickChatCategory, List<int>> _categoryIndices = new();
        private readonly List<(QuickChatCategory cat, int index, string text)> _allQuickChats = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildIndex();
        }

        private void BuildIndex()
        {
            int flatIndex = 0;
            foreach (QuickChatCategory cat in Enum.GetValues(typeof(QuickChatCategory)))
            {
                IReadOnlyList<string> phrases = _coreMessages.GetByCategory(cat);
                if (!_categoryIndices.ContainsKey(cat))
                    _categoryIndices[cat] = new List<int>();

                for (int i = 0; i < phrases.Count; i++)
                {
                    _categoryIndices[cat].Add(flatIndex);
                    _allQuickChats.Add((cat, i, phrases[i]));
                    flatIndex++;
                }
            }
        }

        public void SendQuickChat(int messageIndex)
        {
            if (messageIndex < 0 || messageIndex >= _allQuickChats.Count) return;

            (QuickChatCategory cat, int _, string text) = _allQuickChats[messageIndex];
            string sanitized = _filter.Sanitize(text);
            OnQuickChatMessage?.Invoke(messageIndex, cat.ToString(), sanitized);
        }

        public void SendQuickChat(QuickChatCategory category, int indexInCategory)
        {
            if (!_categoryIndices.TryGetValue(category, out List<int> indices)) return;
            if (indexInCategory < 0 || indexInCategory >= indices.Count) return;

            int flatIndex = indices[indexInCategory];
            SendQuickChat(flatIndex);
        }

        public void SendEmote(int emoteIndex)
        {
            if (emoteIndex < 0 || emoteIndex >= Emotes.Length) return;
            OnEmote?.Invoke(emoteIndex, Emotes[emoteIndex]);
        }

        public void SendPing(Vector3 worldPosition, PingType type)
        {
            OnPing?.Invoke(worldPosition, type);
        }

        public IReadOnlyList<string> GetCategoryNames()
        {
            var names = new List<string>();
            foreach (QuickChatCategory cat in Enum.GetValues(typeof(QuickChatCategory)))
                names.Add(cat.ToString());
            return names;
        }

        public IReadOnlyList<string> GetPhrasesByCategory(QuickChatCategory category)
        {
            return _coreMessages.GetByCategory(category);
        }

        public IReadOnlyList<string> GetAllQuickChatTexts()
        {
            var texts = new List<string>();
            foreach (var (_, _, text) in _allQuickChats)
                texts.Add(text);
            return texts;
        }

        public bool ContainsOffensiveContent(string message)
        {
            return _filter.IsOffensive(message);
        }

        public string SanitizeMessage(string message)
        {
            return _filter.Sanitize(message);
        }

        public int QuickChatCount => _allQuickChats.Count;
        public int EmoteCount => Emotes.Length;
    }
}
