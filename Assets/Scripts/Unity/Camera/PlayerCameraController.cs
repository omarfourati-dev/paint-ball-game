using Paintball.Unity.Data;
using UnityEngine;

namespace Paintball.Unity.Camera
{
    /// <summary>
    /// Third-Person-Kamera (FR-01): Folgt dem Spieler aus definierter Distanz und Höhe.
    /// Freie Kamera-Rotation über Maus/Touch/Gyroskop (FR-02, AR-05).
    /// Klare Lesbarkeit für Nahkampf, Deckung und Teamübersicht (FR-01).
    /// </summary>
    public sealed class PlayerCameraController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private GameConfig _config;

        [Header("References")]
        [SerializeField] private Transform _target;

        [Header("State")]
        [SerializeField] private float _yaw;
        [SerializeField] private float _pitch;

        private float _distance;
        private float _height;

        public Transform Target
        {
            get => _target;
            set => _target = value;
        }

        private void Start()
        {
            if (_config == null) return;
            _distance = _config.CameraDistance;
            _height = _config.CameraHeight;
            _yaw = transform.eulerAngles.y;
            _pitch = 15f;
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            if (_config == null) return;

            UpdateRotation();
            UpdatePosition();
        }

        public void AddRotation(float yawDelta, float pitchDelta)
        {
            float sensX = _config != null ? _config.CameraSensitivityX : 0.003f;
            float sensY = _config != null ? _config.CameraSensitivityY : 0.003f;

            _yaw += yawDelta * sensX * Mathf.Rad2Deg;
            _pitch -= pitchDelta * sensY * Mathf.Rad2Deg;

            float minPitch = _config != null ? _config.CameraMinPitch : -80f;
            float maxPitch = _config != null ? _config.CameraMaxPitch : 80f;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private void UpdateRotation()
        {
            if (_target == null) return;
            _yaw = _target.eulerAngles.y;
        }

        private void UpdatePosition()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_distance) + Vector3.up * _height;
            Vector3 targetPos = _target.position + Vector3.up * 1.5f;

            Vector3 desiredPos = targetPos + offset;

            float charRadius = 0.4f;
            if (Physics.Linecast(targetPos, desiredPos, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            {
                desiredPos = hit.point + hit.normal * charRadius;
            }

            transform.position = desiredPos;
            transform.LookAt(targetPos);
        }
    }
}
