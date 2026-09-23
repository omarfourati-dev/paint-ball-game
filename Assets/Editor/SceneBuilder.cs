using System.Collections.Generic;
using Paintball.Unity;
using Paintball.Unity.Account;
using Paintball.Unity.Analytics;
using Paintball.Unity.Audio;
using Paintball.Unity.Combat;
using Paintball.Unity.Data;
using Paintball.Unity.Input;
using Paintball.Unity.Match;
using Paintball.Unity.Networking;
using Paintball.Unity.Player;
using Paintball.Unity.PowerUps;
using Paintball.Unity.Spawn;
using Paintball.Unity.UI;
using Paintball.Unity.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Paintball.Editor
{
    /// <summary>
    /// Erstellt die Test-Szene (P1: Core Prototype) programmatisch.
    /// Ein Testlevel mit Spawn-Punkten, Cover-Objekten, Nachschubstation,
    /// Power-Up-Pickups und einem Primitive-Player (M1: Erstes lokales Schießgefühl).
    /// </summary>
    public static class SceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";

        [MenuItem("Paintball/Build Scene/Playground (TDM)")]
        public static void BuildPlaygroundScene()
        {
            BuildMapScene(new Paintball.Core.Maps.MapCatalog().GetById("warehouse"), "TDM_Map01");
        }

        [MenuItem("Paintball/Build Scene/Warehouse (Lagerhaus)")]
        public static void BuildWarehouseScene()
        {
            BuildMapScene(new Paintball.Core.Maps.MapCatalog().GetById("warehouse"), "Warehouse");
        }

        [MenuItem("Paintball/Build Scene/Forest (Wald)")]
        public static void BuildForestScene()
        {
            BuildMapScene(new Paintball.Core.Maps.MapCatalog().GetById("forest"), "Forest");
        }

        [MenuItem("Paintball/Build Scene/Arena")]
        public static void BuildArenaScene()
        {
            BuildMapScene(new Paintball.Core.Maps.MapCatalog().GetById("arena"), "Arena");
        }

        private static void BuildMapScene(Paintball.Core.Maps.MapDefinition map, string sceneName)
        {
            if (map == null)
            {
                Debug.LogError("[SceneBuilder] Unbekannte Karte (MapCatalog) für Scene-Build.");
                return;
            }

            EnsureFolder(SceneFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = sceneName;

            CreateLighting(scene);
            CreateGround(map.SizeX, map.SizeZ);

            foreach (var cover in map.Covers)
            {
                if (cover.IsResupply)
                {
                    GameObject station = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    station.name = $"Resupply_{map.Id}";
                    station.transform.position = new Vector3(cover.X, cover.Y, cover.Z);
                    station.transform.localScale = new Vector3(1f, 0.5f, 1f);
                    station.GetComponent<MeshRenderer>().material =
                        CreateBasicMaterial(new Color(0.1f, 0.8f, 0.3f));
                    station.AddComponent<ResupplyStation>();
                    continue;
                }

                GameObject block = CreateCubeWall(
                    new Vector3(cover.X, cover.Y, cover.Z),
                    new Vector3(cover.ScaleX, cover.ScaleY, cover.ScaleZ),
                    cover.IsDynamic ? new Color(0.7f, 0.6f, 0.2f) : (Color?)null);
                if (cover.IsDynamic)
                    block.name = $"DynamicCover_{map.Id}";
            }

            foreach (var spawn in map.Spawns)
            {
                GameObject spawnPoint = new GameObject($"Spawn_Team{spawn.TeamId}_{spawn.X}_{spawn.Z}");
                spawnPoint.transform.position = new Vector3(spawn.X, spawn.Y, spawn.Z);
                spawnPoint.tag = "Respawn";
            }

            CreateGameSystems();
            CreateHud();

            EditorSceneManager.SaveScene(scene, $"{SceneFolder}/{sceneName}.unity");
            Debug.Log($"[SceneBuilder] {sceneName} (MapId={map.Id}) erstellt!");
        }

        [MenuItem("Paintball/Build Scene/Training (Bots)")]
        public static void BuildTrainingScene()
        {
            EnsureFolder(SceneFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Training_Map01";

            CreateLighting(scene);
            CreateGround();
            CreateWallsAndCover();
            CreateSpawnPoints();
            CreatePowerUpPickups();
            CreateResupplyStation();
            CreateGameSystems();
            CreateHud();
            CreateDummyTargets();

            EditorSceneManager.SaveScene(scene, $"{SceneFolder}/Training_Map01.unity");
            Debug.Log("[SceneBuilder] Training_Map01 erstellt!");
        }

        [MenuItem("Paintball/Build Scene/DebugPlayer")]
        public static void BuildDebugPlayerScene()
        {
            EnsureFolder(SceneFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "PlayerTest";

            CreateLighting(scene);
            CreateGround();
            CreateSpawnPoints();
            SpawnPlayer(new Vector3(0f, 1f, 0f), 0, "LocalPlayer");

            EditorSceneManager.SaveScene(scene, $"{SceneFolder}/PlayerTest.unity");
            Debug.Log("[SceneBuilder] PlayerTest erstellt!");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
                AssetDatabase.SaveAssets();
            }
        }

        private static void CreateLighting(Scene scene)
        {
            GameObject lightObj = new GameObject("Directional Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject ambObj = new GameObject("Ambient Light");
            var ambient = ambObj.AddComponent<Light>();
            ambient.type = LightType.Ambient;
            RenderSettings.ambientLight = new Color(0.3f, 0.35f, 0.4f);
        }

        private static void CreateGround()
        {
            CreateGround(80f, 80f);
        }

        private static void CreateGround(float sizeX, float sizeZ)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(sizeX, 1f, sizeZ);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            var renderer = ground.GetComponent<MeshRenderer>();
            renderer.material = CreateBasicMaterial(new Color(0.5f, 0.5f, 0.5f));
        }

        private static void CreateWallsAndCover()
        {
            // Lagerhaus-Wände
            CreateCubeWall(new Vector3(-40f, 2f, 0f), new Vector3(1f, 4f, 80f));
            CreateCubeWall(new Vector3(40f, 2f, 0f), new Vector3(1f, 4f, 80f));
            CreateCubeWall(new Vector3(0f, 2f, -40f), new Vector3(80f, 4f, 1f));
            CreateCubeWall(new Vector3(0f, 2f, 40f), new Vector3(80f, 4f, 1f));

            // Cover-Blöcke (Crates) im Spielfeld
            CreateCubeWall(new Vector3(-10f, 0.8f, -5f), new Vector3(2f, 1.6f, 2f), new Color(0.6f, 0.4f, 0.2f));
            CreateCubeWall(new Vector3(10f, 0.8f, 5f), new Vector3(2f, 1.6f, 2f), new Color(0.6f, 0.4f, 0.2f));
            CreateCubeWall(new Vector3(-15f, 0.8f, 12f), new Vector3(2f, 1.6f, 2f), new Color(0.6f, 0.4f, 0.2f));
            CreateCubeWall(new Vector3(15f, 0.8f, -12f), new Vector3(2f, 1.6f, 2f), new Color(0.6f, 0.4f, 0.2f));
            CreateCubeWall(new Vector3(-20f, 0.75f, 0f), new Vector3(3f, 1.5f, 0.5f), new Color(0.3f, 0.5f, 0.7f));
            CreateCubeWall(new Vector3(20f, 0.75f, 0f), new Vector3(3f, 1.5f, 0.5f), new Color(0.3f, 0.5f, 0.7f));
        }

        private static GameObject CreateCubeWall(Vector3 pos, Vector3 scale, Color? color = null)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = $"Cover_{pos.x}_{pos.z}";
            wall.transform.position = pos;
            wall.transform.localScale = scale;
            var renderer = wall.GetComponent<MeshRenderer>();
            renderer.material = CreateBasicMaterial(color ?? new Color(0.4f, 0.4f, 0.4f));
            return wall;
        }

        private static void CreateSpawnPoints()
        {
            GameObject team0 = new GameObject("Team0_Spawns");
            team0.transform.position = Vector3.zero;
            AddSpawnPoint(team0.transform, new Vector3(-35f, 0.5f, -35f));
            AddSpawnPoint(team0.transform, new Vector3(-30f, 0.5f, -30f));
            AddSpawnPoint(team0.transform, new Vector3(-25f, 0.5f, -35f));
            AddSpawnPoint(team0.transform, new Vector3(-30f, 0.5f, -25f));

            GameObject team1 = new GameObject("Team1_Spawns");
            team1.transform.position = Vector3.zero;
            AddSpawnPoint(team1.transform, new Vector3(35f, 0.5f, 35f));
            AddSpawnPoint(team1.transform, new Vector3(30f, 0.5f, 30f));
            AddSpawnPoint(team1.transform, new Vector3(25f, 0.5f, 35f));
            AddSpawnPoint(team1.transform, new Vector3(30f, 0.5f, 25f));
        }

        private static void AddSpawnPoint(Transform parent, Vector3 position)
        {
            GameObject spawnPoint = new GameObject($"Spawn_{position}");
            spawnPoint.transform.SetParent(parent);
            spawnPoint.transform.position = position;
            spawnPoint.tag = "Respawn";
        }

        private static void CreatePowerUpPickups()
        {
            SpawnPlaceholderPickup(new Vector3(-8f, 1f, 8f), PowerUpType.RapidFire, Color.yellow);
            SpawnPlaceholderPickup(new Vector3(8f, 1f, -8f), PowerUpType.Shield, Color.cyan);
            SpawnPlaceholderPickup(new Vector3(0f, 1f, 20f), PowerUpType.SpeedBoost, Color.green);
            SpawnPlaceholderPickup(new Vector3(0f, 1f, -20f), PowerUpType.AmmoRefill, Color.magenta);
        }

        private static void SpawnPlaceholderPickup(Vector3 pos, PowerUpType type, Color color)
        {
            GameObject pickup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pickup.name = $"PowerUp_{type}";
            pickup.transform.position = pos;
            pickup.transform.localScale = Vector3.one * 0.5f;

            var renderer = pickup.GetComponent<MeshRenderer>();
            renderer.material = CreateBasicMaterial(color);

            var pickupComp = pickup.AddComponent<PowerUpPickup>();
            pickupComp.Configure(type, 8f, 15f);
            var pickupManager = pickup.AddComponent<PowerUpManager>();

            // Sphere collider must be trigger
            var col = pickup.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private static void CreateResupplyStation()
        {
            GameObject station = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            station.name = "ResupplyStation";
            station.transform.position = new Vector3(-12f, 0.5f, -12f);
            station.transform.localScale = new Vector3(1f, 0.5f, 1f);
            var renderer = station.GetComponent<MeshRenderer>();
            renderer.material = CreateBasicMaterial(new Color(0.1f, 0.8f, 0.3f));

            station.AddComponent<ResupplyStation>();
        }

        private static void CreateGameSystems()
        {
            // GameConfig Mock
            GameObject gameConfig = new GameObject("GameConfig");
            gameConfig.AddComponent<GameConfig>();

            // Audio Manager
            GameObject audioManager = new GameObject("AudioManager");
            audioManager.AddComponent<AudioManager>();
            audioManager.AddComponent<AudioSource>();

            // Analytics
            GameObject analytics = new GameObject("AnalyticsTracker");
            analytics.AddComponent<AnalyticsTracker>();

            // Player Profile
            GameObject profile = new GameObject("PlayerProfile");
            profile.AddComponent<PlayerProfile>();

            // Network Game Manager (placeholder until Netcode is installed)
            GameObject networkManager = new GameObject("NetworkGameManager");
            networkManager.AddComponent<NetworkGameManager>();

            // GameMode Manager
            GameObject gameMode = new GameObject("GameModeManager");
            gameMode.AddComponent<GameModeManager>();

            // Match End Handler
            GameObject matchEnd = new GameObject("MatchEndHandler");
            matchEnd.AddComponent<MatchEndHandler>();

            // Spawn Manager
            GameObject spawnManager = new GameObject("SpawnManager");
            spawnManager.AddComponent<SpawnManager>();

            // Quick Chat
            GameObject quickChat = new GameObject("QuickChatSystem");
            quickChat.AddComponent<Social.QuickChatSystem>();

            // Report System
            GameObject report = new GameObject("ReportSystem");
            report.AddComponent<Social.ReportSystem>();

            // Party Manager
            GameObject party = new GameObject("PartyManager");
            party.AddComponent<Social.PartyManager>();

            // Friends Manager
            GameObject friends = new GameObject("FriendsManager");
            friends.AddComponent<Social.FriendsManager>();

            // Localization
            GameObject localization = new GameObject("LocalizationManager");
            localization.AddComponent<Localization.LocalizationManager>();

            // Season Manager
            GameObject season = new GameObject("SeasonManager");
            season.AddComponent<LiveOps.SeasonManager>();

            // Challenge System
            GameObject challenges = new GameObject("ChallengeSystem");
            challenges.AddComponent<LiveOps.ChallengeSystem>();

            // Battle Pass
            GameObject battlePass = new GameObject("BattlePass");
            battlePass.AddComponent<LiveOps.BattlePass>();

            // Platform Performance
            GameObject platform = new GameObject("PlatformPerformance");
            platform.AddComponent<Platform.PlatformPerformance>();

            SpawnPlayer(new Vector3(-5f, 1f, -5f), 0, "Player1");
        }

        private static void CreateHud()
        {
            GameObject canvas = new GameObject("InGameHUD");
            canvas.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.AddComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode =
                UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            canvas.AddComponent<InGameHud>();
        }

        private static void CreateDummyTargets()
        {
            for (int i = 0; i < 3; i++)
            {
                GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                dummy.name = $"DummyTarget_{i}";
                dummy.transform.position = new Vector3(-5f + i * 5f, 1.5f, 15f);
                dummy.transform.localScale = new Vector3(1f, 2f, 1f);
                var renderer = dummy.GetComponent<MeshRenderer>();
                renderer.material = CreateBasicMaterial(new Color(0.7f, 0.2f, 0.2f));

                SafeSetTag(dummy, "Player");
                var hitFeedback = dummy.AddComponent<Combat.HitFeedback>();
                dummy.AddComponent<Decals.PaintSurface>();
            }
        }

        private static void SpawnPlayer(Vector3 pos, int teamId, string name)
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = name;
            SafeSetTag(player, "Player");
            player.transform.position = pos;
            player.transform.localScale = new Vector3(1f, 1.4f, 1f);

            var renderer = player.GetComponent<MeshRenderer>();
            renderer.material = CreateBasicMaterial(teamId == 0 ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.8f, 0.2f, 0.2f));

            // Components
            var controller = player.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 1f, 0f);
            controller.height = 2f;
            controller.radius = 0.4f;

            player.AddComponent<PlayerController>();
            player.AddComponent<PlayerInputBridge>();
            player.AddComponent<Combat.HitFeedback>();
            player.AddComponent<Decals.PaintSurface>();
            player.AddComponent<PowerUpManager>();

            // Weapon
            GameObject weaponHolder = new GameObject("WeaponHolder");
            weaponHolder.transform.SetParent(player.transform);
            weaponHolder.transform.localPosition = new Vector3(0.3f, 1.3f, 0.5f);

            var weapon = weaponHolder.AddComponent<WeaponHandler>();

            // Camera
            GameObject camera = new GameObject("PlayerCamera");
            camera.transform.SetParent(player.transform);
            var cameraController = camera.AddComponent<Camera.PlayerCameraController>();
            cameraController.Target = player.transform;

            var cam = camera.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            SafeSetTag(cam, "MainCamera");
        }

        private static void SafeSetTag(GameObject gameObject, string tag)
        {
            foreach (string existing in UnityEditorInternal.InternalEditorUtility.tags)
            {
                if (existing == tag)
                {
                    gameObject.tag = tag;
                    return;
                }
            }
            Debug.LogWarning($"[SceneBuilder] Tag '{tag}' ist in Project Settings noch nicht definiert - "
                + "bitte unter Edit > Project Settings > Tags and Layers anlegen.");
        }

        private static Material CreateBasicMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            var material = new Material(shader);
            material.color = color;
            return material;
        }
    }
}