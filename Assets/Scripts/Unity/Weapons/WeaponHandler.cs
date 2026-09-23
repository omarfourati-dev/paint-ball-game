using System;
using Paintball.Core.Ballistics;
using Paintball.Core.Weapons;
using Paintball.Unity.Data;
using UnityEngine;

namespace Paintball.Unity.Weapons
{
    /// <summary>
    /// Unity-Bridge für den MarkerStateMachine: Feuert Paintball-Projektile,
    /// verwaltet Munition und Nachladen im Unity-Kontext (FR-06, FR-08).
    /// </summary>
    public sealed class WeaponHandler : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private MarkerSpecsAsset _specsAsset;

        [Header("References")]
        [SerializeField] private Transform _muzzlePoint;
        [SerializeField] private Player.PlayerController _owner;

        private MarkerStateMachine _stateMachine;
        private float _lastFireTime;
        private Random _rng = new();

        public MarkerStateMachine StateMachine => _stateMachine;
        public MarkerSpecsAsset SpecsAsset => _specsAsset;

        public event Action<FireResult> OnFired;
        public event Action OnReloadStarted;
        public event Action OnReloadCompleted;

        private void Awake()
        {
            if (_specsAsset != null)
                _stateMachine = new MarkerStateMachine(_specsAsset.ToCoreSpecs());
        }

        private void Update()
        {
            if (_stateMachine == null) return;
            _stateMachine.Update(Time.time);

            if (_owner != null && _owner.FireHeld)
                TryFire();
        }

        public void Initialize(MarkerSpecsAsset specs, Transform muzzle)
        {
            _specsAsset = specs;
            _muzzlePoint = muzzle;
            if (specs != null)
                _stateMachine = new MarkerStateMachine(specs.ToCoreSpecs());
        }

        public void TryFire()
        {
            if (_stateMachine == null || _owner == null) return;
            if (!_owner.IsAlive) return;

            FireResult result = _stateMachine.TryFire(Time.time);
            OnFired?.Invoke(result);

            if (result.Status == FireStatus.Fired)
                SpawnProjectile();
        }

        private void SpawnProjectile()
        {
            if (_specsAsset == null || _specsAsset.ProjectilePrefab == null) return;
            if (_muzzlePoint == null) return;

            Transform camTransform = UnityEngine.Camera.main != null
                ? UnityEngine.Camera.main.transform
                : transform;

            Vector3 forward = camTransform.forward;

            Vector3 spreadDir = BallisticSolver.ApplySpread(
                forward, _specsAsset.SpreadDegrees, _rng);

            Vector3 velocity = spreadDir * _specsAsset.MuzzleVelocity;

            GameObject projObj = Instantiate(
                _specsAsset.ProjectilePrefab,
                _muzzlePoint.position,
                Quaternion.LookRotation(spreadDir));

            var projectile = projObj.GetComponent<PaintballProjectile>();
            if (projectile != null)
            {
                projectile.Initialize(
                    velocity,
                    _specsAsset.BaseDamage,
                    _specsAsset.GravityScale,
                    _specsAsset.MaxRange,
                    _owner != null ? _owner.gameObject : gameObject,
                    _specsAsset.PaintColor);
            }

            if (_specsAsset.MuzzleFlashPrefab != null)
            {
                GameObject flash = Instantiate(_specsAsset.MuzzleFlashPrefab, _muzzlePoint.position, _muzzlePoint.rotation);
                Destroy(flash, 0.15f);
            }
        }

        public void StartReload()
        {
            if (_stateMachine != null && _stateMachine.TryStartReload(Time.time))
                OnReloadStarted?.Invoke();
        }

        public void AddReserveAmmo(int amount)
        {
            _stateMachine?.AddReserveAmmo(amount);
        }
    }
}
