using UnityEngine;

namespace Paintball.Unity.Gameplay
{
    /// <summary>
    /// Physics-basierte Zielhilfe (FR-03: taktisches Zielen).
    /// Projiziert einen Kegel-Raycast von der Kamera aus, um gegnerische Trefferpunkte
    /// zu detektieren und dem UI eine Fadenkreuz-Ansage zu liefern.
    /// Unterstützt Touch-Konsolen ohne Analogen Stick (UX-11) durch subtile Hilfestellung.
    /// </summary>
    public sealed class PhysicsAimAssist : MonoBehaviour
    {
        [Header("Raycast")]
        [SerializeField] private float _maxRange = 30f;
        [SerializeField] private float _coneAngleDeg = 3f;
        [SerializeField] private LayerMask _targetLayers = ~0;
        [SerializeField] private int _maxTargetsPerScan = 4;
        [SerializeField] private float _scanInterval = 0.1f;

        [Header("Assist")]
        [Tooltip("Maximaler Assist-Snap in Grad pro Frame")]
        [SerializeField] private float _maxAssistDegrees = 1.5f;
        [Tooltip("Abstand unter dem kein Assist greift (sehr nah am Ziel)")]
        [SerializeField] private float _snapDeadzoneDegrees = 0.3f;

        [Header("UI Feedback")]
        [Tooltip("Grobe Trefferpunkthöhe über Boden (Y-Offset vom Centre der Objekt-Bounds)")]
        [SerializeField] private float _hitHeightOffset = 1.0f;

        private Transform _origin;
        private float _lastScanTime;
        private readonly RaycastHit[] _scanResults = new RaycastHit[8];

        private AimingTarget _bestTarget;
        private float _assistStrength;

        public AimingTarget BestTarget => _bestTarget;
        public float AssistStrength => _assistStrength;
        public bool HasTarget => _bestTarget.Transform != null;

        public struct AimingTarget
        {
            public Transform Transform;
            public Vector3 AimPoint;
            public float ScreenDistance;
            public float NormalizedDist;
        }

        private void Awake()
        {
            _origin = GetComponentInChildren<UnityEngine.Camera>()?.transform;
            if (_origin == null)
                _origin = transform;
        }

        private void LateUpdate()
        {
            if (Time.time - _lastScanTime < _scanInterval) return;
            _lastScanTime = Time.time;

            Scan();
        }

        private void Scan()
        {
            _bestTarget = default;
            _assistStrength = 0f;

            if (_origin == null) return;

            Vector3 originPos = _origin.position;
            Vector3 forward = _origin.forward;

            int count = Physics.SphereCastNonAlloc(
                originPos,
                0.1f,
                forward,
                _scanResults,
                _maxRange,
                _targetLayers,
                QueryTriggerInteraction.Ignore);

            AimingTarget best = default;
            best.ScreenDistance = float.MaxValue;

            float halfCone = Mathf.Tan(_coneAngleDeg * Mathf.Deg2Rad) * _maxRange;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _scanResults[i];
                if (hit.transform == transform || hit.transform.IsChildOf(transform))
                    continue;

                Vector3 targetPoint = hit.collider.bounds.center + Vector3.up * _hitHeightOffset;
                Vector3 toTarget = targetPoint - originPos;
                float dist = toTarget.magnitude;

                if (dist < 0.5f) continue;

                float perpDist = Vector3.Cross(forward, toTarget).magnitude;
                if (perpDist > halfCone) continue;

                Vector3 screenPoint = UnityEngine.Camera.main != null
                    ? UnityEngine.Camera.main.WorldToScreenPoint(targetPoint)
                    : targetPoint;

                Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 screenPos = new Vector2(screenPoint.x, screenPoint.y);
                float screenDist = Vector2.Distance(screenPos, screenCenter);

                if (screenDist < best.ScreenDistance)
                {
                    best.Transform = hit.transform;
                    best.AimPoint = targetPoint;
                    best.ScreenDistance = screenDist;
                    best.NormalizedDist = dist / _maxRange;
                }
            }

            _bestTarget = best;
            _assistStrength = CalculateAssistStrength(best);
        }

        private float CalculateAssistStrength(AimingTarget target)
        {
            if (target.Transform == null) return 0f;

            Vector3 originPos = _origin.position;
            Vector3 toTarget = (target.AimPoint - originPos).normalized;
            Vector3 forward = _origin.forward;

            float angleDeg = Vector3.Angle(forward, toTarget);

            if (angleDeg < _snapDeadzoneDegrees) return 0f;
            if (angleDeg > _coneAngleDeg) return 0f;

            float t = Mathf.InverseLerp(_snapDeadzoneDegrees, _coneAngleDeg, angleDeg);
            float strength = Mathf.Clamp01(1f - t);

            strength *= Mathf.Lerp(1f, 0.3f, target.NormalizedDist);

            return strength;
        }

        public Vector3 GetAssistedDirection(Vector3 currentForward)
        {
            if (_bestTarget.Transform == null || _assistStrength <= 0f)
                return currentForward;

            Vector3 toTarget = (_bestTarget.AimPoint - _origin.position).normalized;
            float maxTurn = _maxAssistDegrees * _assistStrength * Time.deltaTime;

            return Vector3.RotateTowards(currentForward, toTarget, maxTurn * Mathf.Deg2Rad, 0f);
        }

        public Vector2 GetCrosshairOffset()
        {
            if (_bestTarget.Transform == null) return Vector2.zero;

            Vector3 screenPoint = UnityEngine.Camera.main != null
                ? UnityEngine.Camera.main.WorldToScreenPoint(_bestTarget.AimPoint)
                : Vector3.zero;

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 offset = new Vector2(screenPoint.x, screenPoint.y) - screenCenter;

            return offset * _assistStrength;
        }

        private void OnDrawGizmosSelected()
        {
            Transform t = _origin != null ? _origin : transform;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(t.position, t.forward * _maxRange);

            if (_bestTarget.Transform != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(t.position, _bestTarget.AimPoint);
                Gizmos.DrawWireSphere(_bestTarget.AimPoint, 0.2f);
            }
        }
    }
}