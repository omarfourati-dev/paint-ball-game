using System.Collections.Generic;
using Paintball.Core.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Anpassungs-Screen (UI-08): Charakter, Marker, Skins, 3D-Vorschau.
    /// Verdrahtet mit Core CosmeticInventory für Besitz- und Equip-Prüfung (FR-33, FR-36).
    /// FR-33 bis FR-37.
    /// </summary>
    public sealed class CustomizeScreen : MonoBehaviour
    {
        private const string SaveKey = "CosmeticInventory.V1";

        [Header("Marker Selection")]
        [SerializeField] private Transform _markerListParent;
        [SerializeField] private GameObject _markerButtonPrefab;
        [SerializeField] private TMPro.TextMeshProUGUI _markerStatsText;

        [Header("Color Selection")]
        [SerializeField] private Button _colorPickerButton;
        [SerializeField] private Image _colorPreview;
        [SerializeField] private TMPro.TMP_Dropdown _colorPresetDropdown;

        [Header("Outfit Selection")]
        [SerializeField] private Transform _outfitListParent;
        [SerializeField] private GameObject _outfitButtonPrefab;

        [Header("Preview")]
        [SerializeField] private Transform _characterPreviewPivot;

        [Header("Buttons")]
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _equipButton;
        [SerializeField] private TMPro.TextMeshProUGUI _statusText;

        private static readonly Color[] PaintColorPresets =
        {
            Color.red, Color.blue, Color.green, Color.yellow,
            Color.cyan, Color.magenta, Color.white, Color.black
        };

        private static readonly string[] PaintColorNames =
        {
            "Rot", "Blau", "Grün", "Gelb", "Cyan", "Magenta", "Weiß", "Schwarz"
        };

        private static readonly string[] OutfitIds =
        {
            "outfit_default", "outfit_camo", "outfit_neon", "outfit_tactical", "outfit_royal"
        };

        private static readonly string[] OutfitNames =
        {
            "Standard", "Camouflage", "Neon", "Taktisch", "Royal"
        };

        private readonly CosmeticInventory _inventory = new();
        private string _selectedOutfitId;
        private string _selectedPaintColorId;
        private Color _selectedPaintColor = Color.red;

        private void Start()
        {
            LoadInventory();

            if (_backButton != null) _backButton.onClick.AddListener(OnBack);
            if (_equipButton != null) _equipButton.onClick.AddListener(OnEquip);
            if (_colorPickerButton != null) _colorPickerButton.onClick.AddListener(OnPickColor);
            if (_colorPresetDropdown != null)
            {
                _colorPresetDropdown.ClearOptions();
                _colorPresetDropdown.AddOptions(new List<string>(PaintColorNames));
                _colorPresetDropdown.onValueChanged.AddListener(OnColorPresetSelected);
            }

            PopulateMarkerList();
            PopulateOutfitList();
            LoadCurrentLoadout();
        }

        private void LoadInventory()
        {
            CosmeticInventory restored = CosmeticInventory.Deserialize(PlayerPrefs.GetString(SaveKey, null));
            if (restored != null && restored.OwnedCount > 0)
            {
                _inventory.RestoreFrom(restored);
            }
            else
            {
                _inventory.Grant("outfit_default");
                _inventory.Grant("paint_red");
                _inventory.Grant("paint_blue");
                _inventory.Grant("paint_green");
            }
        }

        private void PopulateMarkerList()
        {
            var loadout = Progression.LoadoutManager.Instance;
            if (loadout == null || _markerListParent == null || _markerButtonPrefab == null) return;

            var markers = loadout.AvailableMarkers;
            if (markers == null) return;

            foreach (var marker in markers)
            {
                if (marker == null) continue;
                GameObject btnObj = Instantiate(_markerButtonPrefab, _markerListParent);
                var text = btnObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (text != null) text.text = marker.DisplayName;

                var button = btnObj.GetComponent<Button>();
                if (button != null)
                {
                    string markerId = marker.Id;
                    button.onClick.AddListener(() => OnMarkerSelected(markerId));
                }
            }
        }

        private void PopulateOutfitList()
        {
            if (_outfitListParent == null || _outfitButtonPrefab == null) return;

            for (int i = 0; i < OutfitIds.Length; i++)
            {
                string outfitId = OutfitIds[i];
                bool unlocked = _inventory.Owns(outfitId);

                GameObject btnObj = Instantiate(_outfitButtonPrefab, _outfitListParent);
                var text = btnObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (text != null)
                    text.text = unlocked ? OutfitNames[i] : OutfitNames[i] + " (gesperrt)";

                var button = btnObj.GetComponent<Button>();
                if (button != null)
                {
                    button.interactable = unlocked;
                    string capturedId = outfitId;
                    button.onClick.AddListener(() => OnOutfitSelected(capturedId));
                }
            }
        }

        private void OnOutfitSelected(string outfitId)
        {
            _selectedOutfitId = outfitId;

            var loadout = Progression.LoadoutManager.Instance;
            if (loadout != null) loadout.SetOutfit(outfitId);

            ShowStatus($"Outfit '{(GetOutfitName(outfitId))}' gewählt");
        }

        private void OnMarkerSelected(string markerId)
        {
            var loadout = Progression.LoadoutManager.Instance;
            if (loadout != null) loadout.SetMarker(markerId);

            if (_markerStatsText != null)
            {
                var marker = loadout != null ? loadout.GetSelectedMarker() : null;
                if (marker != null)
                {
                    _markerStatsText.text =
                        $"Feuerrate: {marker.RoundsPerSecond}/s\n" +
                        $"Schaden: {marker.BaseDamage}\n" +
                        $"Magazin: {marker.MagazineSize}\n" +
                        $"Reichweite: {marker.MaxRange}m";
                }
            }
        }

        private void OnEquip()
        {
            string itemToEquip = !string.IsNullOrEmpty(_selectedPaintColorId) ? _selectedPaintColorId : _selectedOutfitId;
            if (string.IsNullOrEmpty(itemToEquip))
                itemToEquip = "outfit_default";

            if (!_inventory.Owns(itemToEquip))
            {
                ShowStatus("Nicht im Besitz – zuerst freischalten!");
                return;
            }

            _inventory.Equip(itemToEquip);
            SaveInventory();

            var loadout = Progression.LoadoutManager.Instance;
            if (loadout != null)
            {
                if (itemToEquip.StartsWith("paint_"))
                    loadout.SetPaintColor(_selectedPaintColor);
                else if (itemToEquip.StartsWith("outfit_"))
                    loadout.SetOutfit(itemToEquip);
            }

            ShowStatus("Ausgerüstet: " + GetOutfitName(itemToEquip));
        }

        private void OnPickColor()
        {
            int index = _colorPresetDropdown != null ? _colorPresetDropdown.value : 0;
            OnColorPresetSelected(index);
        }

        private void OnColorPresetSelected(int index)
        {
            if (index < 0 || index >= PaintColorPresets.Length) return;

            string paintId = "paint_" + ColorUtility.ToHtmlStringRGB(PaintColorPresets[index]);
            if (!_inventory.Owns(paintId))
            {
                _inventory.Grant(paintId);
                SaveInventory();
            }
            _selectedPaintColorId = paintId;
            _selectedPaintColor = PaintColorPresets[index];

            var loadout = Progression.LoadoutManager.Instance;
            if (loadout != null) loadout.SetPaintColor(PaintColorPresets[index]);

            if (_colorPreview != null)
                _colorPreview.color = PaintColorPresets[index];

            ShowStatus("Farbe gewählt: " + PaintColorNames[index]);
        }

        private void LoadCurrentLoadout()
        {
            var loadout = Progression.LoadoutManager.Instance;
            if (loadout == null) return;

            _selectedPaintColor = loadout.PaintColor;

            if (_colorPreview != null)
                _colorPreview.color = loadout.PaintColor;

            for (int i = 0; i < PaintColorPresets.Length; i++)
            {
                if (ColorUtility.ToHtmlStringRGB(PaintColorPresets[i]) == ColorUtility.ToHtmlStringRGB(loadout.PaintColor))
                {
                    if (_colorPresetDropdown != null) _colorPresetDropdown.value = i;
                    break;
                }
            }

            _selectedOutfitId = string.IsNullOrEmpty(loadout.SelectedOutfitId) ? "outfit_default" : loadout.SelectedOutfitId;
        }

        private string GetOutfitName(string itemId)
        {
            for (int i = 0; i < OutfitIds.Length; i++)
            {
                if (OutfitIds[i] == itemId)
                    return OutfitNames[i];
            }

            if (itemId.StartsWith("paint_"))
                return itemId.Substring("paint_".Length);
            return itemId;
        }

        private void SaveInventory()
        {
            PlayerPrefs.SetString(SaveKey, _inventory.Serialize());
            PlayerPrefs.Save();
        }

        private void ShowStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
        }

        private void OnBack()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}