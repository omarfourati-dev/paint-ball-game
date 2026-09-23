using Paintball.Core.Privacy;
using Paintball.Unity.Account;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Datenschutz-Screen (NFR-12, DSGVO): Datenauskunft (portables JSON anzeigen
    /// bzw. speichern) und vollständige lokale Löschung aller Spielerdaten.
    /// Nutzt den Core <see cref="AccountDataExport"/>; danach wird das Konto neu
    /// aufgesetzt und zum Hauptmenü gewechselt.
    /// </summary>
    public sealed class PrivacyScreen : MonoBehaviour
    {
        [Header("Data Export")]
        [SerializeField] private TextMeshProUGUI _exportText;
        [SerializeField] private TextMeshProUGUI _exportInfoText;

        [Header("Buttons")]
        [SerializeField] private Button _exportButton;
        [SerializeField] private Button _deleteButton;
        [SerializeField] private Button _backButton;

        private void Start()
        {
            if (_exportButton != null) _exportButton.onClick.AddListener(OnExport);
            if (_deleteButton != null) _deleteButton.onClick.AddListener(OnDelete);
            if (_backButton != null) _backButton.onClick.AddListener(OnBack);

            if (_exportInfoText != null)
                _exportInfoText.text = "Gibt alle lokal gespeicherten Spielerdaten als "
                    + "portables JSON aus.\nKeine Zugangsdaten werden exportiert.";
        }

        private void OnExport()
        {
            var profile = PlayerProfile.Instance;
            string json = profile != null
                ? AccountDataExport.ExportJson(profile.CoreAccount)
                : "{}";

            if (_exportText != null) _exportText.text = json;
            Debug.Log($"[Datenschutz] Export ({json.Length}) Zeichen erzeugt");
        }

        private void OnDelete()
        {
            var deleted = AccountDataExport.DeleteLocalPlayerData(Application.persistentDataPath);
            string summary = deleted.Count > 0
                ? "Gelöscht: " + string.Join(", ", deleted)
                : "Keine lokalen Daten gefunden.";

            if (_exportText != null) _exportText.text = summary;
            Debug.Log($"[Datenschutz] {summary}");

            var profile = PlayerProfile.Instance;
            if (profile != null) profile.ResetAccount();

            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        private void OnBack()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}