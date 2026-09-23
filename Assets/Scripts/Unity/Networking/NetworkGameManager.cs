using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Paintball.Unity.Networking
{
    /// <summary>
    /// Multiplayer-Manager für Unity Netcode for GameObjects.
    /// Server-autoritatives Modell (FR-25, AR-06): Dedicated Server oder Host-Modell.
    /// </summary>
    public sealed class NetworkGameManager : NetworkBehaviour
    {
        public static NetworkGameManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private int _maxPlayers = 8;
        [SerializeField] private float _serverTickRate = 20f;

        [Header("State")]
        private NetworkVariable<int> _team0Score = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<int> _team1Score = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<int> _matchPhase = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<float> _matchTimer = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int Team0Score => _team0Score.Value;
        public int Team1Score => _team1Score.Value;
        public int MatchPhaseValue => _matchPhase.Value;
        public float MatchTimer => _matchTimer.Value;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                NetworkManager.Singleton.ConnectionEventCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.ConnectionEventCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            base.OnNetworkDespawn();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RegisterKillServerRpc(int shooterClientId, int shooterTeamId)
        {
            if (!IsServer) return;

            if (shooterTeamId == 0)
                _team0Score.Value++;
            else
                _team1Score.Value++;

            NotifyScoreChangedClientRpc(shooterTeamId, shooterTeamId == 0 ? _team0Score.Value : _team1Score.Value);
        }

        [ClientRpc]
        private void NotifyScoreChangedClientRpc(int teamId, int newScore)
        {
            Debug.Log($"[Match] Team {teamId} Score: {newScore}");
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetMatchPhaseServerRpc(int phase)
        {
            if (!IsServer) return;
            _matchPhase.Value = phase;
        }

        [ServerRpc(RequireOwnership = false)]
        public void UpdateMatchTimerServerRpc(float time)
        {
            if (!IsServer) return;
            _matchTimer.Value = time;
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[Network] Client {clientId} connected. Total: {NetworkManager.Singleton.ConnectedClientsList.Count}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[Network] Client {clientId} disconnected.");
        }

        public void StartServer()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.StartServer();
        }

        public void StartHost()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.StartHost();
        }

        public void StartClient()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.StartClient();
        }

        public void ShutdownNetwork()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
        }
    }
}
