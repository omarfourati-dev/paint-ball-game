using System;
using System.Collections.Generic;

namespace Paintball.Core.Social
{
    /// <summary>Kategorien für Schnell-Chat-Phrasen (schnelle Team-Kommunikation).</summary>
    public enum QuickChatCategory
    {
        Callout,
        Help,
        Praise,
        Tactical
    }

    /// <summary>
    /// Echte Schnell-Chat-Phrasen (Team-Kommunikation ohne Tastatur):
    /// kurze, klare Kategorien für Callouts, Hilfe, Lob und Taktik.
    /// Keine Unity-Abhängigkeit.
    /// </summary>
    public sealed class QuickChatMessages
    {
        private static readonly Dictionary<QuickChatCategory, string[]> Phrases = new()
        {
            [QuickChatCategory.Callout] = new[]
            {
                "Gegner links!", "Gegner rechts!", "Feindkontakt voraus!", "Gegner hinten!", "Zone gesichert!"
            },
            [QuickChatCategory.Help] = new[]
            {
                "Brauche Unterstützung!", "Ich brauche Munition!", "Decke mich!", "Nur noch wenig Trefferpunkte!"
            },
            [QuickChatCategory.Praise] = new[]
            {
                "Gut gemacht!", "Starke Runde!", "Danke!", "Nice Play!"
            },
            [QuickChatCategory.Tactical] = new[]
            {
                "Zurück zur Basis!", "Stürmt jetzt!", "Halte die Position!", "Konzentriert auf das Ziel!"
            }
        };

        public IReadOnlyList<string> GetByCategory(QuickChatCategory category)
        {
            return Phrases.TryGetValue(category, out string[] phrases) ? phrases : System.Array.Empty<string>();
        }
    }

    /// <summary>
    /// Toxizitätsfilter (FR-52, NFR-15): erkennt und maskiert beleidigende Ausdrücke
    /// in Chat-Nachrichten – deterministisch, lokal und mit echten Wortlisten
    /// (DE-Daten im Prototyp, später erweiterbar für weitere Sprachen).
    /// </summary>
    public sealed class ChatFilter
    {
        private static readonly string[] OffensiveWords =
        {
            // DE
            "idiot", "dumm", "noob", "hirnlos", "dummkopf", "trottel",
            "bastard", "duffer", "spast", "schwachkopf",
            // EN
            "idiot", "stupid", "noob", "trash", "loser", "moron",
            "retard", "dumbass", "jackass", "asshole"
        };

        private const string Mask = "***";

        public bool IsOffensive(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string lowered = message.ToLowerInvariant();
            foreach (string word in OffensiveWords)
            {
                if (lowered.Contains(word))
                    return true;
            }
            return false;
        }

        /// <summary>Ersetzt beleidigende Wörter durch Maskierung, Rest bleibt erhalten.</summary>
        public string Sanitize(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;

            string[] words = message.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i].Trim('.', '!', '?', ',', ';', ':', '"', '\'', '(' , ')', '[', ']');
                if (Array.IndexOf(OffensiveWords, word.ToLowerInvariant()) >= 0)
                    words[i] = Mask;
            }

            return string.Join(' ', words);
        }
    }
}