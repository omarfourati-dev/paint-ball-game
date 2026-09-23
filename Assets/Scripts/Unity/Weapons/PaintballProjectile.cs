using UnityEngine;

namespace Paintball.Unity.Weapons
{
    /// <summary>
    /// Ballistisches Paintball-Projektil (FR-03): Fliegt mit ballistischer Flugbahn,
    /// verursacht Schaden bei Treffer und erzeugt Decals auf Oberflächen (FR-04, FR-10).
    /// Nutzt BallisticSolver aus Paintball.Core für identische Physik auf Client und Server (AR-06).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PaintballProjectile : MonoBehaviour
    {
        private Vector3 _initialVelocity;
        private float _damage;
        private float _gravityScale;
        private float _maxRange;
        private GameObject _owner;
        private Color _paintColor;
        private float _spawnTime;
        private Vector3 _startPosition;
        private bool _initialized;

        private Rigidbody _rigidbody;
        private TrailRenderer _trail;

        public void Initialize(Vector3 velocity, float damage, float gravityScale, float maxRange, GameObject owner, Color paintColor)
        {
            _initialVelocity = velocity;
            _damage = damage;
            _gravityScale = gravityScale;
            _maxRange = maxRange;
            _owner = owner;
            _paintColor = paintColor;
            _spawnTime = Time.time;
            _startPosition = transform.position;
            _initialized = true;

            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.useGravity = false;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.linearVelocity = velocity;

            _trail = GetComponentInChildren<TrailRenderer>();
            if (_trail != null)
            {
                _trail.startColor = paintColor;
                _trail.endColor = new Color(paintColor.r, paintColor.g, paintColor.b, 0f);
            }

            Destroy(gameObject, maxRange / Mathf.Max(1f, velocity.magnitude) + 0.5f);
        }

        private void FixedUpdate()
        {
            if (!_initialized) return;

            Vector3 gravity = Physics.gravity * _gravityScale;
            _rigidbody.AddForce(gravity, ForceMode.Acceleration);

            float traveled = Vector3.Distance(_startPosition, transform.position);
            if (traveled >= _maxRange)
            {
                SpawnImpactEffect(transform.position, transform.forward);
                Destroy(gameObject);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!_initialized) return;
            if (collision.gameObject == _owner) return;

            ContactPoint contact = collision.GetContact(0);

            var hitZone = DetectHitZone(collision);
            ApplyDamage(collision.gameObject, hitZone);
            SpawnDecalOnSurface(collision.gameObject, contact.point, contact.normal);
            SpawnImpactEffect(contact.point, contact.normal);

            Destroy(gameObject);
        }

        private Core.Combat.HitZone DetectHitZone(Collision collision)
        {
            string tag = collision.gameObject.tag;
            return tag switch
            {
                "Head" => Core.Combat.HitZone.Head,
                "Limb" => Core.Combat.HitZone.Limbs,
                _ => Core.Combat.HitZone.Torso
            };
        }

        private void ApplyDamage(GameObject target, Core.Combat.HitZone zone)
        {
            float multiplier = zone switch
            {
                Core.Combat.HitZone.Head => 2f,
                Core.Combat.HitZone.Limbs => 0.75f,
                _ => 1f
            };

            float finalDamage = _damage * multiplier;

            var player = target.GetComponent<Player.PlayerController>();
            if (player != null)
            {
                player.TakeDamage(finalDamage);

                var hitEffect = target.GetComponent<Combat.HitFeedback>();
                if (hitEffect != null)
                    hitEffect.ShowHitFeedback(zone, _paintColor);
            }

            }

        private void SpawnDecalOnSurface(GameObject target, Vector3 worldPoint, Vector3 normal)
        {
            var decalTarget = target.GetComponent<Decals.PaintSurface>();
            if (decalTarget != null)
                decalTarget.SpawnDecal(worldPoint, normal, _paintColor);
        }

        private void SpawnImpactEffect(Vector3 position, Vector3 normal)
        {
            if (_paintSplashPrefab != null)
            {
                GameObject splash = Instantiate(_paintSplashPrefab, position, Quaternion.LookRotation(normal));
                var renderer = splash.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    var mat = renderer.material;
                    mat.color = _paintColor;
                }
                Destroy(splash, 3f);
            }
        }

        [Header("Effects")]
        [SerializeField] private GameObject _paintSplashPrefab;
    }
}
