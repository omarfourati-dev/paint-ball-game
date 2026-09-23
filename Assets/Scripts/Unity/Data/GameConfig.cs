using UnityEngine;

namespace Paintball.Unity.Data
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "Paintball/Game Config", order = 0)]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Player")]
        [Tooltip("Start-Lebenspunkte")]
        public float DefaultHitPoints = 100f;
        [Tooltip("Laufgeschwindigkeit in m/s")]
        public float WalkSpeed = 5f;
        [Tooltip("Sprint-Multiplikator")]
        public float SprintMultiplier = 1.5f;
        [Tooltip("Crouch-Multiplikator")]
        public float CrouchMultiplier = 0.6f;
        [Tooltip("Sprunghöhe in m/s")]
        public float JumpForce = 7f;
        [Tooltip("Schwerkraft-Multiplikator für Spieler")]
        public float GravityMultiplier = 2f;

        [Header("Camera")]
        public float CameraDistance = 6f;
        public float CameraHeight = 2f;
        public float CameraSensitivityX = 0.003f;
        public float CameraSensitivityY = 0.003f;
        public float CameraMinPitch = -80f;
        public float CameraMaxPitch = 80f;

        [Header("Match")]
        public int TdmTargetScore = 25;
        public float TdmTimeLimit = 300f;
        public float CountdownSeconds = 3f;
        public int MaxPlayers = 8;

        [Header("Spawn (FR-12)")]
        public float SpawnProtectionSeconds = 3f;
        public float MinSpawnDistanceFromEnemy = 15f;

        [Header("Power-Ups (FR-09)")]
        public float PowerUpDuration = 8f;

        [Header("Network")]
        public float ServerTickRate = 20f;
        public float InterpolationDelay = 0.1f;
        public float ClientPredictionTime = 0.1f;

        [Header("Performance")]
        public int TargetFpsMobile = 60;
        public int TargetFpsDesktop = 144;
    }
}
