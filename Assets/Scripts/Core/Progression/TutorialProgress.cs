using System;
using System.Collections.Generic;

namespace Paintball.Core.Progression
{
    /// <summary>Schritte der interaktiven Einführung (UI-12).</summary>
    public enum TutorialStep
    {
        Move,
        AimAndShoot,
        Reload,
        UsePowerUp,
        PlayObjective
    }

    /// <summary>
    /// Tutorial/Onboarding-Fortschritt (UI-12): verfolgt, welche Einführungs-Schritte
    /// ein Spieler abgeschlossen hat, liefert einen Abschlussgrad und kann ohne Unity
    /// serialisiert werden – echte Persistenz statt PlayerPrefs.
    /// </summary>
    public sealed class TutorialProgress
    {
        private readonly HashSet<TutorialStep> _completed = new();

        public bool AllCompleted => _completed.Count >= Enum.GetValues(typeof(TutorialStep)).Length;

        public double CompletedRatio =>
            (double)_completed.Count / Enum.GetValues(typeof(TutorialStep)).Length;

        public bool IsCompleted(TutorialStep step) => _completed.Contains(step);

        public void Complete(TutorialStep step) => _completed.Add(step);

        public string Serialize()
        {
            return string.Join(",", _completed);
        }

        public static TutorialProgress Deserialize(string data)
        {
            var progress = new TutorialProgress();
            if (string.IsNullOrWhiteSpace(data)) return progress;

            foreach (string token in data.Split(','))
            {
                string trimmed = token.Trim();
                if (trimmed.Length == 0) continue;
                if (Enum.TryParse(trimmed, out TutorialStep step))
                    progress._completed.Add(step);
            }

            return progress;
        }
    }
}