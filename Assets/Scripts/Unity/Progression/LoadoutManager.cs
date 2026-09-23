using Paintball.Core.Progression;
using Paintball.Unity.Data;
using UnityEngine;

namespace Paintball.Unity.Progression
{
    /// <summary>
    /// Loadout-System (FR-33 bis FR-37): Auswahl von Marker, Skins, Paintball-Farbe,
    /// Ausweichgadget und Verbrauchsgegenstand. Delegiert an die Pure-C#-Logik
    /// EquipmentGarage (FR-35) für Besitz- und Cooldown-Regeln.
    /// </summary>
    public sealed class LoadoutManager : MonoBehaviour
    {
        public static LoadoutManager Instance { get; private set; }

        private readonly EquipmentGarage _equipmentGarage = new();

        [Header("Available Items")]
        [SerializeField] private MarkerSpecsAsset[] _availableMarkers;

        [Header("Current Loadout")]
        [SerializeField] private string _selectedMarkerId;
        [SerializeField] private string _selectedOutfitId;
        [SerializeField] private Color _paintColor = Color.red;

        public string SelectedMarkerId => _selectedMarkerId;
        public string SelectedOutfitId => _selectedOutfitId;
        public Color PaintColor => _paintColor;
        public MarkerSpecsAsset[] AvailableMarkers => _availableMarkers;
        public EquipmentGarage Garage => _equipmentGarage;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SyncGarageMarkers();
            LoadLoadout();
        }

        private void SyncGarageMarkers()
        {
            if (_availableMarkers == null) return;
            foreach (var marker in _availableMarkers)
            {
                if (marker != null)
                    _equipmentGarage.SetOwnedMarker(marker.Id, true);
            }
        }

        public MarkerSpecsAsset GetSelectedMarker()
        {
            if (_availableMarkers == null) return null;
            foreach (var marker in _availableMarkers)
            {
                if (marker != null && marker.Id == _selectedMarkerId)
                    return marker;
            }
            return _availableMarkers != null && _availableMarkers.Length > 0 ? _availableMarkers[0] : null;
        }

        public void SetMarker(string markerId)
        {
            if (!_equipmentGarage.TryEquipMarker(markerId)) return;
            _selectedMarkerId = markerId;
            SaveLoadout();
        }

        public bool TryUseDodgeGadget(float now)
        {
            return _equipmentGarage.TryDodge(now);
        }

        public bool TryConsume(string consumableId)
        {
            return _equipmentGarage.TryConsume(consumableId);
        }

        public void SetOutfit(string outfitId)
        {
            _selectedOutfitId = outfitId;
            SaveLoadout();
        }

        public void SetPaintColor(Color color)
        {
            _paintColor = color;
            SaveLoadout();
        }

        private void SaveLoadout()
        {
            PlayerPrefs.SetString("LoadoutMarker", _selectedMarkerId);
            PlayerPrefs.SetString("LoadoutOutfit", _selectedOutfitId);
            ColorUtility.ToHtmlStringRGB(_paintColor);
            PlayerPrefs.SetString("PaintColor", ColorUtility.ToHtmlStringRGB(_paintColor));
            PlayerPrefs.Save();
        }

        private void LoadLoadout()
        {
            _selectedMarkerId = PlayerPrefs.GetString("LoadoutMarker", _availableMarkers != null && _availableMarkers.Length > 0 ? _availableMarkers[0].Id : "marker_default");
            _selectedOutfitId = PlayerPrefs.GetString("LoadoutOutfit", "default");
            string colorHex = PlayerPrefs.GetString("PaintColor", "FF0000");
            if (ColorUtility.TryParseHtmlString("#" + colorHex, out Color color))
                _paintColor = color;
        }
    }
}