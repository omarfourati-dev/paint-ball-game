using UnityEngine;
using UnityEngine.AI;

namespace Paintball.Unity.Bot
{
    /// <summary>
    /// Einfache KI für Trainingsmodus (FR-19): Bots bewegen sich zu Zielen,
    /// schießen auf Gegner und suchen Deckung.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Player.PlayerController))]
    public sealed class BotController : MonoBehaviour
    {
        [Header("AI Settings")]
        [SerializeField] private float _detectionRange = 30f;
        [SerializeField] private float _fireRange = 25f;
        [SerializeField] private float _coverSearchRange = 15f;
        [SerializeField] private float _retreatHealthThreshold = 0.3f;
        [SerializeField] private float _thinkInterval = 0.5f;

        [Header("References")]
        [SerializeField] private Weapons.WeaponHandler _weaponHandler;

        private NavMeshAgent _agent;
        private Player.PlayerController _playerController;
        private Transform _currentTarget;
        private float _thinkTimer;
        private BotState _state = BotState.Idle;

        private enum BotState { Idle, Pursuing, Attacking, SeekingCover, Retreating }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _playerController = GetComponent<Player.PlayerController>();
        }

        private void Start()
        {
            _agent.speed = 4f;
            _agent.stoppingDistance = 3f;
        }

        private void Update()
        {
            if (_playerController != null && !_playerController.IsAlive)
            {
                _agent.isStopped = true;
                return;
            }

            _thinkTimer -= Time.deltaTime;
            if (_thinkTimer > 0f) return;
            _thinkTimer = _thinkInterval;

            FindTarget();
            UpdateState();
            ExecuteState();
        }

        private void FindTarget()
        {
            float closestDist = _detectionRange;
            _currentTarget = null;

            foreach (var player in FindObjectsByType<Player.PlayerController>(FindObjectsSortMode.None))
            {
                if (player == _playerController) continue;
                if (!player.IsAlive) continue;

                float dist = Vector3.Distance(transform.position, player.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    _currentTarget = player.transform;
                }
            }
        }

        private void UpdateState()
        {
            if (_currentTarget == null)
            {
                _state = BotState.Idle;
                return;
            }

            float distToTarget = Vector3.Distance(transform.position, _currentTarget.position);
            float healthPercent = _playerController.Health.CurrentHitPoints /
                                  _playerController.Health.MaxHitPoints;

            if (healthPercent < _retreatHealthThreshold)
            {
                _state = BotState.Retreating;
            }
            else if (distToTarget <= _fireRange)
            {
                _state = BotState.Attacking;
            }
            else
            {
                _state = BotState.Pursuing;
            }
        }

        private void ExecuteState()
        {
            switch (_state)
            {
                case BotState.Idle:
                    _agent.isStopped = true;
                    break;

                case BotState.Pursuing:
                    _agent.isStopped = false;
                    if (_currentTarget != null)
                        _agent.SetDestination(_currentTarget.position);
                    break;

                case BotState.Attacking:
                    _agent.isStopped = true;
                    if (_currentTarget != null)
                        transform.LookAt(new Vector3(_currentTarget.position.x, transform.position.y, _currentTarget.position.z));
                    _weaponHandler?.TryFire();
                    break;

                case BotState.Retreating:
                    _agent.isStopped = false;
                    Vector3 retreatDir = (transform.position - _currentTarget.position).normalized;
                    _agent.SetDestination(transform.position + retreatDir * 10f);
                    break;
            }
        }
    }
}
