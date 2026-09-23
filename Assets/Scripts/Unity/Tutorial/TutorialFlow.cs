using System;
using System.IO;
using Paintball.Core.Persistence;
using Paintball.Core.Progression;
using UnityEngine;
using UnityEngine.UI;
using CoreTutorialStep = Paintball.Core.Progression.TutorialStep;

namespace Paintball.Unity.Tutorial
{
    /// <summary>
    /// Interaktives Onboarding/Tutorial (UI-12): Führt neue Spieler an Steuerung,
    /// Waffenverständnis und Spielmodi heran (FR-19, NFR-21).
    /// Fortschritt kommt aus dem getesteten Core TutorialProgress und wird als
    /// Datei im persistentDataPath gespeichert (Core LocalPersistence); der alte
    /// PlayerPrefs-Schalter wird einmalig migriert. Jeder durchgeklappte Schritt
    /// wird im Core-Fortschritt abgeschlossen.
    /// </summary>
    public sealed class TutorialFlow : MonoBehaviour
    {
        private const string LegacyCompletedKey = "TutorialCompleted";

        [System.Serializable]
        public struct TutorialStep
        {
            public string Title;
            public string Description;
            public CoreTutorialStep CoreStep;
            public GameObject HighlightTarget;
            public float Duration;
        }

        private static string ProgressFilePath => Path.Combine(Application.persistentDataPath, "tutorial-progress.txt");

        [Header("Tutorial Steps")]
        [SerializeField] private TutorialStep[] _steps;

        [Header("UI")]
        [SerializeField] private GameObject _tutorialPanel;
        [SerializeField] private TMPro.TextMeshProUGUI _titleText;
        [SerializeField] private TMPro.TextMeshProUGUI _descriptionText;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _skipButton;

        private TutorialProgress _progress = new();
        private int _currentStep = -1;

        public bool IsTutorialCompleted => _progress.AllCompleted;

        private void Start()
        {
            if (_nextButton != null) _nextButton.onClick.AddListener(NextStep);
            if (_skipButton != null) _skipButton.onClick.AddListener(SkipTutorial);

            LoadProgress();

            if (!IsTutorialCompleted)
                StartTutorial();
            else
                _tutorialPanel?.SetActive(false);
        }

        private void LoadProgress()
        {
            string fromFile = LocalPersistence.LoadText(ProgressFilePath);
            if (!string.IsNullOrEmpty(fromFile))
            {
                _progress = TutorialProgress.Deserialize(fromFile) ?? new TutorialProgress();
                return;
            }

            if (PlayerPrefs.HasKey(LegacyCompletedKey))
            {
                foreach (CoreTutorialStep step in Enum.GetValues(typeof(CoreTutorialStep)))
                    _progress.Complete(step);
                PlayerPrefs.DeleteKey(LegacyCompletedKey);
                PlayerPrefs.Save();
                SaveProgress();
            }
        }

        private void SaveProgress() => LocalPersistence.SaveText(ProgressFilePath, _progress.Serialize());

        private void StartTutorial()
        {
            _tutorialPanel?.SetActive(true);
            _currentStep = -1;
            NextStep();
        }

        private void NextStep()
        {
            if (_currentStep >= 0 && _currentStep < _steps.Length)
            {
                _progress.Complete(_steps[_currentStep].CoreStep);
                SaveProgress();
            }

            _currentStep++;
            if (_currentStep >= _steps.Length)
            {
                CompleteTutorial();
                return;
            }

            var step = _steps[_currentStep];
            if (_titleText != null) _titleText.text = step.Title;
            if (_descriptionText != null) _descriptionText.text = step.Description;
        }

        private void CompleteTutorial()
        {
            foreach (CoreTutorialStep step in Enum.GetValues(typeof(CoreTutorialStep)))
                _progress.Complete(step);
            SaveProgress();
            _tutorialPanel?.SetActive(false);
        }

        private void SkipTutorial()
        {
            CompleteTutorial();
        }
    }
}