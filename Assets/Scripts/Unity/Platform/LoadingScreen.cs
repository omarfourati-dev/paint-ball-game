using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.Platform
{
    /// <summary>
    /// Ladebildschirm (UI-01): Logo, Ladefortschritt, Tipps (NFR-02).
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        [Header("Loading UI")]
        [SerializeField] private GameObject _loadingPanel;
        [SerializeField] private TMPro.TextMeshProUGUI _progressText;
        [SerializeField] private Slider _progressBar;
        [SerializeField] private TMPro.TextMeshProUGUI _tipText;

        private static readonly string[] Tips =
        {
            "Tipp: Zielen Sie auf den Kopf für doppelten Schaden!",
            "Tipp: Nutzen Sie Deckung, um Kugeln zu vermeiden.",
            "Tipp: Teamplay bringt Extra-XP!",
            "Tipp: Power-Ups können das Spiel wenden!",
            "Tipp: Nachladen wird automatisch unterbrochen, wenn Sie schießen."
        };

        private int _lastProgress;

        private void Start()
        {
            ShowRandomTip();

            if (_progressBar != null)
                _progressBar.value = 0f;

            if (_progressText != null)
                _progressText.text = "0%";
        }

        /**
         * Reports progress from an async load operation.
         * @param asyncOp The AsyncOperation being tracked.
         */
        public void UpdateProgress(AsyncOperation asyncOp)
        {
            if (asyncOp == null) return;

            int progress = Mathf.RoundToInt(asyncOp.progress * 100f);
            if (progress == _lastProgress) return;
            _lastProgress = progress;

            if (_progressBar != null)
                _progressBar.value = (float)progress / 100f;
            if (_progressText != null)
                _progressText.text = $"{progress}%";
        }

        private void ShowRandomTip()
        {
            if (_tipText == null) return;
            int index = Random.Range(0, Tips.Length);
            _tipText.text = Tips[index];
        }
    }
}