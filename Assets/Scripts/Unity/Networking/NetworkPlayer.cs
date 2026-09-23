using Unity.Netcode;
using UnityEngine;

namespace Paintball.Unity.Networking
{
    /// <summary>
    /// NetworkPlayer: Synchronisiert Spielerposition, Rotation und Zustand über das Netzwerk.
    /// Client-Prediction und Interpolation (FR-26) sorgen für flüssiges Spielgefühl bei Latenz.
    /// </summary>
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(Player.PlayerController))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        private NetworkVariable<Vector3> _netPosition = new(
            writePermission: NetworkVariableWritePermission.Owner);

        private NetworkVariable<Quaternion> _netRotation = new(
            writePermission: NetworkVariableWritePermission.Owner);

        private NetworkVariable<int> _netHealth = new(
            writePermission: NetworkVariableWritePermission.Server);

        private NetworkVariable<int> _netTeamId = new(
            writePermission: NetworkVariableWritePermission.Server);

        private Player.PlayerController _playerController;

        public int TeamId => _netTeamId.Value;
        public int NetworkHealth => _netHealth.Value;

        private void Awake()
        {
            _playerController = GetComponent<Player.PlayerController>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsOwner)
            {
                var camera = GetComponentInChildren<Camera.PlayerCameraController>();
                if (camera != null)
                    camera.Target = transform;
            }
            else
            {
                if (_playerController != null)
                    _playerController.enabled = false;
            }

            _netHealth.OnValueChanged += OnHealthChanged;
        }

        public override void OnNetworkDespawn()
        {
            _netHealth.OnValueChanged -= OnHealthChanged;
            base.OnNetworkDespawn();
        }

        private void OnHealthChanged(int previousValue, int newValue)
        {
            if (!IsOwner && _playerController != null)
            {
                float hp = newValue;
                _playerController.Health?.Revive();
                while (_playerController.Health.CurrentHitPoints > hp)
                    _playerController.Health.ApplyDamage(1f);
            }
        }

        private void Update()
        {
            if (IsServer)
            {
                if (_playerController != null)
                    _netHealth.Value = (int)_playerController.Health.CurrentHitPoints;
            }

            if (!IsOwner)
            {
                if (_playerController != null)
                    transform.position = Vector3.Lerp(transform.position, _netPosition.Value, Time.deltaTime * 15f);
            }
        }

        [ServerRpc(RequireOwnership = true)]
        public void UpdatePositionServerRpc(Vector3 position, Quaternion rotation)
        {
            if (!IsServer) return;
            _netPosition.Value = position;
            _netRotation.Value = rotation;
            transform.position = position;
            transform.rotation = rotation;
        }

        [ServerRpc(RequireOwnership = true)]
        public void ApplyDamageServerRpc(int damage)
        {
            if (!IsServer) return;
            _playerController?.TakeDamage(damage);
            _netHealth.Value = (int)_playerController.Health.CurrentHitPoints;
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetTeamServerRpc(int teamId)
        {
            if (!IsServer) return;
            _netTeamId.Value = teamId;
        }
    }
}
