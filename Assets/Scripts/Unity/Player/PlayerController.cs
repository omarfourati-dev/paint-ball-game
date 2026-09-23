using System;
using Paintball.Core.Combat;
using Paintball.Unity.Data;
using UnityEngine;

namespace Paintball.Unity.Player
{
    /// <summary>
    /// Steuert den Spieler: Bewegen (FR-02), Springen, Ducken, Sprinten.
    /// Nutzt das Pure-C# HitPointPool aus Paintball.Core für Trefferlogik (FR-05).
    /// Engine-Bridge zwischen Unity-Physik und engine-unabhängiger Core-Logik (AR-04).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameConfig _config;
        [SerializeField] private Camera.PlayerCameraController _cameraController;

        [Header("Input (wird von PlayerInputBridge gesetzt)")]
        public Vector2 MoveInput;
        public Vector2 LookInput;
        public bool JumpPressed;
        public bool SprintHeld;
        public bool CrouchHeld;
        public bool FireHeld;

        private CharacterController _characterController;
        private Vector3 _velocity;
        private bool _isGrounded;
        private bool _isCrouching;
        private bool _isSprinting;

        public HitPointPool Health { get; private set; }
        public bool IsAlive => Health != null && !Health.IsEliminated;
        public bool IsCrouching => _isCrouching;
        public bool IsSprinting => _isSprinting;
        public Vector3 Velocity => _characterController != null ? _characterController.velocity : Vector3.zero;

        public event Action OnDied;
        public event Action OnRespawned;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            Health = new HitPointPool(_config != null ? _config.DefaultHitPoints : 100f);
            Health.Eliminated += () => OnDied?.Invoke();
        }

        private void Update()
        {
            if (!IsAlive) return;

            HandleGroundCheck();
            HandleMovement();
            HandleJump();
            ApplyGravity();
            ApplyFinalMovement();
        }

        private void HandleGroundCheck()
        {
            _isGrounded = _characterController.isGrounded;
            if (_isGrounded && _velocity.y < 0f)
                _velocity.y = -2f;
        }

        private void HandleMovement()
        {
            float speed = _config != null ? _config.WalkSpeed : 5f;

            _isCrouching = CrouchHeld;
            _isSprinting = SprintHeld && !_isCrouching && MoveInput.sqrMagnitude > 0.01f;

            if (_isCrouching)
                speed *= _config != null ? _config.CrouchMultiplier : 0.6f;
            else if (_isSprinting)
                speed *= _config != null ? _config.SprintMultiplier : 1.5f;

            Vector3 move = new Vector3(MoveInput.x, 0f, MoveInput.y);

            if (move.sqrMagnitude > 1f)
                move.Normalize();

            Transform camTransform = _cameraController != null ? _cameraController.transform : transform;
            Vector3 forward = Vector3.ProjectOnPlane(camTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(camTransform.right, Vector3.up).normalized;

            Vector3 worldMove = forward * move.z + right * move.x;
            _characterController.Move(worldMove * (speed * Time.deltaTime));
        }

        private void HandleJump()
        {
            if (JumpPressed && _isGrounded && !_isCrouching)
            {
                float jumpForce = _config != null ? _config.JumpForce : 7f;
                _velocity.y = jumpForce;
            }
            JumpPressed = false;
        }

        private void ApplyGravity()
        {
            float gravity = Physics.gravity.y * (_config != null ? _config.GravityMultiplier : 2f);
            _velocity.y += gravity * Time.deltaTime;
        }

        private void ApplyFinalMovement()
        {
            _characterController.Move(_velocity * Time.deltaTime);
        }

        public void RotatePlayer(float yawDelta)
        {
            transform.Rotate(Vector3.up, yawDelta * Mathf.Rad2Deg);
        }

        public void Respawn(Vector3 position)
        {
            Health.Revive();
            transform.position = position;
            _velocity = Vector3.zero;
            OnRespawned?.Invoke();
        }

        public void TakeDamage(float damage)
        {
            Health.ApplyDamage(damage);
        }
    }
}
