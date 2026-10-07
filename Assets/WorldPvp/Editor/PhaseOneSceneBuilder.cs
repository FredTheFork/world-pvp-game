using System;
using System.IO;
using System.Linq;
using CesiumForUnity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using WorldPvp.Phase1.Battles;
using WorldPvp.Phase1.Backend;
using WorldPvp.Phase1.Combat;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;
using WorldPvp.Phase1.Gameplay;
using WorldPvp.Phase1.Player;
using WorldPvp.Phase1.Streaming;
using WorldPvp.Phase1.UI;

namespace WorldPvp.Phase1.Editor
{
    /// <summary>
    /// Creates the generated Cesium/URP + NGO scene used by the current Phase 7 combat prototype.
    /// Earlier Phase 2–6 menu aliases remain available for existing project workflows.
    /// </summary>
    [InitializeOnLoad]
    public static class PhaseOneSceneBuilder
    {
        private const string ScenePath = "Assets/WorldPvp/Scenes/Phase1_GoogleWorld.unity";
        private const string SettingsPath = "Assets/WorldPvp/Configuration/PhaseOneWorldSettings.asset";
        private const string RendererPath = "Assets/WorldPvp/Rendering/Phase1_ForwardRenderer.asset";
        private const string PipelinePath = "Assets/WorldPvp/Rendering/Phase1_UniversalRenderPipeline.asset";
        private const string CapsuleMaterialPath = "Assets/WorldPvp/Rendering/Phase1_Capsule.mat";
        private const string SessionPlayerPrefabPath = "Assets/WorldPvp/Prefabs/SessionPlayerNetworkObject.prefab";

        static PhaseOneSceneBuilder()
        {
            EditorApplication.delayCall += BuildFirstSceneIfNeeded;
        }

        [MenuItem("Tools/World PvP/Phase 2/Build or Rebuild Scene")]
        [MenuItem("Tools/World PvP/Phase 3/Build or Rebuild Scene")]
        [MenuItem("Tools/World PvP/Phase 4/Build or Rebuild Scene")]
        [MenuItem("Tools/World PvP/Phase 5/Build or Rebuild Scene")]
        [MenuItem("Tools/World PvP/Phase 6/Build or Rebuild Scene")]
        [MenuItem("Tools/World PvP/Phase 7/Build or Rebuild Combat Scene")]
        [MenuItem("Tools/World PvP/Phase 9/Build or Rebuild Virtual Gameplay Scene")]
        [MenuItem("Tools/World PvP/Phase 10/Build or Rebuild Control-Plane Scene")]
        public static void BuildOrRebuildScene()
        {
            if (SceneFileExists() &&
                !EditorUtility.DisplayDialog(
                    "Rebuild the generated arena scene?",
                    "This replaces the generated Cesium arena scene. Continue only if you have no custom edits to preserve.",
                    "Rebuild",
                    "Cancel"))
            {
                return;
            }

            BuildScene(true);
        }

        private static void BuildFirstSceneIfNeeded()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += BuildFirstSceneIfNeeded;
                return;
            }

            if (!SceneFileExists())
            {
                try
                {
                    BuildScene(true);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "[Phase 7] Automatic scene creation failed (" + exception.GetType().Name +
                        "). Use Tools → World PvP → Phase 7 → Build or Rebuild Combat Scene after imports complete.");
                }
            }
        }

        private static void BuildScene(bool openWhenComplete)
        {
            EnsureFolders();
            PhaseOneWorldSettings settings = LoadOrCreateSettings();
            WeaponDefinition prototypeRifle = PhaseSevenCombatAssetBuilder.LoadOrCreatePrototypeRifle();
            ConfigureUrp();
            Material capsuleMaterial = LoadOrCreateCapsuleMaterial();
            GameObject sessionPlayerPrefab = LoadOrCreateSessionPlayerPrefab(prototypeRifle);

            if (SceneFileExists())
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureNetworkManager(sessionPlayerPrefab);

            GameObject georeferenceObject = new GameObject("CesiumGeoreference");
            CesiumGeoreference georeference = georeferenceObject.AddComponent<CesiumGeoreference>();
            georeference.Initialize();
            GeoPosition defaultOrigin = settings.DefaultOrigin;
            georeference.SetOriginLongitudeLatitudeHeight(
                defaultOrigin.LongitudeDegrees,
                defaultOrigin.LatitudeDegrees,
                defaultOrigin.AltitudeMeters);

            GameObject tilesetObject = new GameObject("GooglePhotorealistic3DTiles");
            tilesetObject.transform.SetParent(georeferenceObject.transform, false);
            Cesium3DTileset tileset = tilesetObject.AddComponent<Cesium3DTileset>();
            tileset.enabled = false;

            GameObject characterObject = new GameObject("TestCharacter");
            characterObject.transform.SetParent(georeferenceObject.transform, false);
            characterObject.transform.localPosition = Vector3.zero;
            characterObject.transform.localRotation = Quaternion.identity;

            CesiumGlobeAnchor globeAnchor = characterObject.AddComponent<CesiumGlobeAnchor>();
            globeAnchor.detectTransformChanges = true;
            globeAnchor.adjustOrientationForGlobeWhenMoving = true;

            // The anchored player is the origin-shift reference. The manager retains a separate,
            // immutable arena ENU frame so local metres do not jump when Cesium shifts rendering.
            CesiumOriginShift originShift = characterObject.AddComponent<CesiumOriginShift>();
            originShift.distance = settings.OriginShiftThresholdMeters;

            CharacterController characterController = characterObject.AddComponent<CharacterController>();
            characterController.height = settings.CharacterHeightMeters;
            characterController.radius = settings.CharacterRadiusMeters;
            characterController.center = new Vector3(0f, settings.CharacterHeightMeters * 0.5f, 0f);
            characterController.skinWidth = settings.CharacterSkinWidthMeters;
            characterController.stepOffset = settings.StepOffsetMeters;
            characterController.slopeLimit = settings.MaximumSlopeDegrees;
            characterController.minMoveDistance = 0f;
            characterController.enabled = false;

            GameObject capsuleVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsuleVisual.name = "FirstPersonHiddenBody";
            capsuleVisual.transform.SetParent(characterObject.transform, false);
            capsuleVisual.transform.localPosition = new Vector3(0f, settings.CharacterHeightMeters * 0.5f, 0f);
            capsuleVisual.transform.localRotation = Quaternion.identity;
            capsuleVisual.transform.localScale = new Vector3(
                settings.CharacterRadiusMeters * 2f,
                settings.CharacterHeightMeters * 0.5f,
                settings.CharacterRadiusMeters * 2f);
            Collider visualCollider = capsuleVisual.GetComponent<Collider>();
            if (visualCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(visualCollider);
            }
            MeshRenderer capsuleRenderer = capsuleVisual.GetComponent<MeshRenderer>();
            capsuleRenderer.sharedMaterial = capsuleMaterial;

            GameObject cameraPivotObject = new GameObject("FirstPersonCameraPivot");
            cameraPivotObject.transform.SetParent(characterObject.transform, false);
            cameraPivotObject.transform.localPosition = new Vector3(0f, settings.FirstPersonEyeHeightMeters, 0f);
            cameraPivotObject.transform.localRotation = Quaternion.Euler(settings.InitialCameraPitchDegrees, 0f, 0f);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraPivotObject.transform, false);
            cameraObject.transform.localPosition = Vector3.zero;
            cameraObject.transform.localRotation = Quaternion.identity;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = settings.FirstPersonFieldOfViewDegrees;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20000f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();

            Transform leftFirstPersonArm = CreateFirstPersonArm(
                "FirstPersonLeftArm", cameraObject.transform, capsuleMaterial,
                new Vector3(-0.27f, -0.30f, 0.50f), new Vector3(0.12f, 0.28f, 0.12f),
                Quaternion.Euler(-18f, 0f, -12f));
            Transform rightFirstPersonArm = CreateFirstPersonArm(
                "FirstPersonRightArm", cameraObject.transform, capsuleMaterial,
                new Vector3(0.27f, -0.30f, 0.50f), new Vector3(0.12f, 0.28f, 0.12f),
                Quaternion.Euler(-18f, 0f, 12f));

            PhaseOneTestCharacterController motor = characterObject.AddComponent<PhaseOneTestCharacterController>();
            ProceduralPlayerPresentation presentation = characterObject.AddComponent<ProceduralPlayerPresentation>();
            presentation.ConfigureFirstPerson(
                cameraPivotObject.transform,
                leftFirstPersonArm,
                rightFirstPersonArm,
                settings);

            GameObject systemsObject = new GameObject("PhaseOneWorldSystems");
            GeospatialWorldManager manager = systemsObject.AddComponent<GeospatialWorldManager>();
            ArenaBoundaryVisualizer boundaryVisualizer = systemsObject.AddComponent<ArenaBoundaryVisualizer>();
            manager.ConfigureReferences(
                georeference,
                tileset,
                characterObject.transform,
                globeAnchor,
                characterController,
                motor,
                settings,
                boundaryVisualizer,
                camera);
            GameplayCollisionWorld gameplayCollisionWorld = systemsObject.AddComponent<GameplayCollisionWorld>();
            GameplayArenaRuntime gameplayArenaRuntime = systemsObject.AddComponent<GameplayArenaRuntime>();
            manager.ConfigureGameplay(gameplayCollisionWorld, gameplayArenaRuntime);
            // Explicitly enabled only so the existing local prototype remains inspectable. Dedicated
            // server bootstrap disables this path and requires reviewed independent gameplay data.
            gameplayCollisionWorld.ConfigureReferences(manager, settings, null, true);
            MapUsageTracker mapUsageTracker = systemsObject.AddComponent<MapUsageTracker>();
            WorldStreamingController streamingController = systemsObject.AddComponent<WorldStreamingController>();
            WorldPerformanceTelemetry performanceTelemetry = systemsObject.AddComponent<WorldPerformanceTelemetry>();
            manager.ConfigurePhaseSix(streamingController, mapUsageTracker);
            performanceTelemetry.Configure(
                manager,
                settings,
                mapUsageTracker,
                streamingController,
                camera);
            motor.Configure(characterController, cameraPivotObject.transform, globeAnchor, settings, manager);
            motor.ConfigurePresentation(capsuleRenderer, presentation);
            PhaseOneRuntimeHud phaseTwoHud = systemsObject.AddComponent<PhaseOneRuntimeHud>();
            phaseTwoHud.Configure(manager, motor, settings);
            phaseTwoHud.enabled = false;

            WorldLocationValidator locationValidator = systemsObject.AddComponent<WorldLocationValidator>();
            locationValidator.Configure(manager);
            PhaseEightServerAuthority serverAuthority = systemsObject.AddComponent<PhaseEightServerAuthority>();
            PhaseEightDedicatedServerBootstrap dedicatedBootstrap =
                systemsObject.AddComponent<PhaseEightDedicatedServerBootstrap>();
            GameObject matchAuthorityObject = new GameObject("CombatMatchAuthority");
            matchAuthorityObject.AddComponent<NetworkObject>();
            CombatMatchController combatMatchController = matchAuthorityObject.AddComponent<CombatMatchController>();
            dedicatedBootstrap.Configure(
                manager,
                serverAuthority,
                combatMatchController,
                gameplayCollisionWorld,
                gameplayArenaRuntime);

            BattleSessionCoordinator sessionCoordinator = systemsObject.AddComponent<BattleSessionCoordinator>();
            sessionCoordinator.Configure(
                manager,
                locationValidator,
                mapUsageTracker,
                combatMatchController,
                gameplayArenaRuntime);
            // Supabase Auth and profile requests go through the same-origin Vercel API. No secret key is stored in the Unity scene.
            systemsObject.AddComponent<PhaseTenAccountClient>();
            PhaseThreeRuntimeHud phaseThreeHud = systemsObject.AddComponent<PhaseThreeRuntimeHud>();
            phaseThreeHud.Configure(manager, motor, settings, sessionCoordinator);
            PhaseFiveGameplayHud phaseFiveHud = systemsObject.AddComponent<PhaseFiveGameplayHud>();
            phaseFiveHud.Configure(manager, settings, sessionCoordinator);
            PhaseSixDiagnosticsHud phaseSixHud = systemsObject.AddComponent<PhaseSixDiagnosticsHud>();
            phaseSixHud.Configure(performanceTelemetry, mapUsageTracker, sessionCoordinator, settings);
            PhaseSevenCombatHud phaseSevenCombatHud = systemsObject.AddComponent<PhaseSevenCombatHud>();
            phaseSevenCombatHud.Configure(manager, sessionCoordinator, settings, prototypeRifle);

            GameObject lightObject = new GameObject("Directional Light");
            Light directionalLight = lightObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;
            directionalLight.intensity = 1.15f;
            directionalLight.color = Color.white;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (openWhenComplete)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            Debug.Log("[Phase 7] Combat scene created: " + ScenePath +
                      ". Weapons use the editable Phase 7 WeaponDefinition asset; the Relay host owns shot resolution and damage. API keys are not stored in the scene.");
        }

        private static Transform CreateFirstPersonArm(
            string objectName,
            Transform parent,
            Material material,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation)
        {
            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            arm.name = objectName;
            arm.transform.SetParent(parent, false);
            arm.transform.localPosition = localPosition;
            arm.transform.localScale = localScale;
            arm.transform.localRotation = localRotation;
            Collider collider = arm.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            Renderer renderer = arm.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
            return arm.transform;
        }

        private static bool SceneFileExists()
        {
            return File.Exists(Path.Combine(
                Application.dataPath,
                "WorldPvp",
                "Scenes",
                "Phase1_GoogleWorld.unity"));
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/WorldPvp/Configuration");
            EnsureFolder("Assets/WorldPvp/Rendering");
            EnsureFolder("Assets/WorldPvp/Resources");
            EnsureFolder("Assets/WorldPvp/Prefabs");
            EnsureFolder("Assets/WorldPvp/Scenes");
            EnsureFolder("Assets/WorldPvp/StreamingAssets");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string folderName = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static PhaseOneWorldSettings LoadOrCreateSettings()
        {
            PhaseOneWorldSettings settings = AssetDatabase.LoadAssetAtPath<PhaseOneWorldSettings>(SettingsPath);
            if (settings != null)
            {
                return settings;
            }

            settings = ScriptableObject.CreateInstance<PhaseOneWorldSettings>();
            settings.name = "PhaseOneWorldSettings";
            AssetDatabase.CreateAsset(settings, SettingsPath);
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static GameObject LoadOrCreateSessionPlayerPrefab(WeaponDefinition defaultWeapon)
        {
            if (defaultWeapon == null || !defaultWeapon.IsValid)
            {
                throw new InvalidOperationException("The Phase 7 prototype WeaponDefinition asset is missing or invalid.");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SessionPlayerPrefabPath);
            if (prefab != null &&
                (prefab.GetComponent<NetworkObject>() == null || prefab.GetComponent<NetworkPlayer>() == null))
            {
                AssetDatabase.DeleteAsset(SessionPlayerPrefabPath);
                prefab = null;
            }

            if (prefab == null)
            {
                // Input and fire intent are networked. Health, ammo, hits, damage and death are host-authored.
                GameObject temporaryPlayer = new GameObject("SessionNetworkPlayer");
                NetworkObject networkObject = temporaryPlayer.AddComponent<NetworkObject>();
                networkObject.SpawnWithObservers = false;
                temporaryPlayer.AddComponent<NetworkPlayer>();
                Weapon weapon = temporaryPlayer.AddComponent<Weapon>();
                weapon.ConfigureDefinition(defaultWeapon);
                PrefabUtility.SaveAsPrefabAsset(temporaryPlayer, SessionPlayerPrefabPath);
                UnityEngine.Object.DestroyImmediate(temporaryPlayer);
                AssetDatabase.ImportAsset(SessionPlayerPrefabPath, ImportAssetOptions.ForceSynchronousImport);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SessionPlayerPrefabPath);
            }
            else
            {
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(SessionPlayerPrefabPath);
                try
                {
                    NetworkObject networkObject = prefabContents.GetComponent<NetworkObject>();
                    if (networkObject != null)
                    {
                        networkObject.SpawnWithObservers = false;
                        EditorUtility.SetDirty(networkObject);
                    }
                    Weapon weapon = prefabContents.GetComponent<Weapon>();
                    if (weapon == null)
                    {
                        weapon = prefabContents.AddComponent<Weapon>();
                    }
                    if (weapon.Definition == null || !weapon.Definition.IsValid)
                    {
                        weapon.ConfigureDefinition(defaultWeapon);
                    }
                    EditorUtility.SetDirty(weapon);
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, SessionPlayerPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SessionPlayerPrefabPath);
            }

            AssetDatabase.SaveAssets();
            if (prefab == null || prefab.GetComponent<NetworkObject>() == null ||
                prefab.GetComponent<NetworkPlayer>() == null || prefab.GetComponent<Weapon>() == null)
            {
                throw new InvalidOperationException("Could not create the NGO combat player prefab with its data-backed Weapon component.");
            }
            return prefab;
        }

        private static void ConfigureNetworkManager(GameObject sessionPlayerPrefab)
        {
            GameObject managerObject = new GameObject("NetworkManager");
            NetworkManager networkManager = managerObject.AddComponent<NetworkManager>();
            UnityTransport transport = managerObject.AddComponent<UnityTransport>();
            NetworkConfig networkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = sessionPlayerPrefab,
                TickRate = NetworkPlayer.ServerSimulationTickRate,
                EnableSceneManagement = false
            };
            networkManager.NetworkConfig = networkConfig;
        }

        private static void ConfigureUrp()
        {
            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.name = "Phase1_ForwardRenderer";
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }

            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                pipeline.name = "Phase1_UniversalRenderPipeline";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int originalQuality = QualitySettings.GetQualityLevel();
            string[] qualityNames = QualitySettings.names;
            for (int i = 0; i < qualityNames.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            if (qualityNames.Length > 0)
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp(originalQuality, 0, qualityNames.Length - 1), false);
            }

            EditorUtility.SetDirty(rendererData);
            EditorUtility.SetDirty(pipeline);
        }

        private static Material LoadOrCreateCapsuleMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(CapsuleMaterialPath);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            material.name = "Phase1_Capsule";
            material.color = new Color(0.18f, 0.74f, 0.91f, 1f);
            AssetDatabase.CreateAsset(material, CapsuleMaterialPath);
            return material;
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            if (existing.Any(item => item.path == scenePath))
            {
                return;
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(scenePath, true)
            }.Concat(existing).ToArray();
        }
    }
}
