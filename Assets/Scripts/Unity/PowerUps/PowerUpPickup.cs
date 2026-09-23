using Paintball.Core.PowerUps;
using Paintball.Unity.Data;
using UnityEngine;

namespace Paintball.Unity.PowerUps
{
    /// <summary>
    /// Aufsammelbares Power-Up auf der Karte (FR-09).
    /// Sobald ein Spieler es berührt, wird der entsprechende Effekt aktiviert.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PowerUpPickup : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private PowerUpType _type = PowerUpType.RapidFire;
        [SerializeField] private float _duration = 8f;
        [SerializeField] private float _respawnTime = 15f;

        [Header("Visuals")]
        [SerializeField] private GameObject _visualModel;
        [SerializeField] private float _bobSpeed = 2f;
        [SerializeField] private float _bobHeight = 0.3f;
        [SerializeField] private float _rotateSpeed = 90f;

        private Vector3 _startPosition;
        private bool _isActive = true;
        private float _respawnTimer;

        public PowerUpType Type => _type;
        public bool IsActive => _isActive;

        public void Configure(PowerUpType type, float duration, float respawnTime)
        {
            _type = type;
            _duration = duration;
            _respawnTime = respawnTime;
        }

        private void Start()
        {
            _startPosition = transform.position;
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void Update()
        {
            if (_isActive)
            {
                if (_visualModel != null)
                {
                    float bob = Mathf.Sin(Time.time * _bobSpeed) * _bobHeight;
                    _visualModel.transform.position = _startPosition + Vector3.up * bob;
                    _visualModel.transform.Rotate(Vector3.up, _rotateSpeed * Time.deltaTime);
                }
            }
            else
            {
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer <= 0f)
                    Respawn();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_isActive) return;

            var player = other.GetComponent<Player.PlayerController>();
            if (player == null || !player.IsAlive) return;

            var powerUpManager = other.GetComponent<PowerUpManager>();
            if (powerUpManager != null)
            {
                powerUpManager.ActivatePowerUp(_type, Time.time, _duration);
                _isActive = false;
                _respawnTimer = _respawnTime;
                if (_visualModel != null) _visualModel.SetActive(false);
            }
        }

        private void Respawn()
        {
            _isActive = true;
            if (_visualModel != null) _visualModel.SetActive(true);
        }
    }
}
