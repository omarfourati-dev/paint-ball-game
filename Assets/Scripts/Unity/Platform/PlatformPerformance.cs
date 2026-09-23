using UnityEngine;
using UnityEngine.Rendering;

namespace Paintball.Unity.Platform
{
    /// <summary>
    /// Plattformspezifische Performance-Optimierung (NFR-01, NFR-04, PA-04):
    /// FPS-Cap-Option, thermisches Verhalten auf Mobile, Ladezeit-Tuning im Web.
    /// </summary>
    public sealed class PlatformPerformance : MonoBehaviour
    {
        public static PlatformPerformance Instance { get; private set; }

        [Header("FPS Settings")]
        [SerializeField] private int _targetFpsDesktop = 144;
        [SerializeField] private int _targetFpsMobile = 60;
        [SerializeField] private int _targetFpsWeb = 60;
        [SerializeField] private bool _applyDynamicResolution = false;

        [Header("Dynamic Resolution")]
        [SerializeField] private float _minDynamicScale = 0.6f;
        [SerializeField] private float _maxDynamicScale = 1f;
        [SerializeField] private float _lowFpsThreshold = 45f;
        [SerializeField] private float _highFpsThreshold = 58f;

        private DynamicResolutionHandler _dynamicResolution;
        private float _frameTimeAccumulator;
        private int _frameCount;
        private float _currentFps;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            ApplyPlatformSettings();
        }

        public void ApplyPlatformSettings()
        {
            int targetFps;
#if UNITY_WEBGL
            targetFps = _targetFpsWeb;
#elif UNITY_ANDROID || UNITY_IOS
            targetFps = _targetFpsMobile;
#else
            targetFps = _targetFpsDesktop;
#endif
            Application.targetFrameRate = Mathf.Max(30, targetFps);

            if (_applyDynamicResolution)
            {
                ScalableBufferManager.ResizeBuffers(1f, 1f);
                DynamicResolutionHandler.SetDynamicResScaler(RescaleDrs, DynamicResScalePolicyType.UsesSystemDefaultScaler);
                DynamicResolutionHandler.DynamicResEnabled = true;
                DynamicResolutionHandler.SetDrsTriggerAllocationThreshold(1);
                DynamicResolutionHandler.SetDrsTriggerMaxFrames(10);
            }

            ApplyMobileSettings();
        }

        private void ApplyMobileSettings()
        {
#if UNITY_ANDROID || UNITY_IOS
            // Battery optimization: Disable VSync on mobile to allow adaptive FPS
            QualitySettings.vSyncCount = 0;
#endif
        }

        private static float RescaleDrs()
        {
            return Mathf.Clamp01(0.8f);
        }

        private void Update()
        {
            _frameTimeAccumulator += Time.unscaledDeltaTime;
            _frameCount++;

            if (_frameTimeAccumulator >= 1f)
            {
                _currentFps = _frameCount / _frameTimeAccumulator;
                _frameCount = 0;
                _frameTimeAccumulator = 0f;

                ReportPerformance();
            }
        }

        private void ReportPerformance()
        {
            var analytics = Analytics.AnalyticsTracker.Instance;
            if (analytics != null)
                analytics.TrackPerformance(
                    fps: (int)_currentFps,
                    frameTimeMs: (long)(_currentFps > 0f ? 1000f / _currentFps : 0f),
                    gcAllocMb: 0L);
        }
    }
}