using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldPvp.Phase1.Battles;
using WorldPvp.Phase1.Combat;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;
using WorldPvp.Phase1.Player;

namespace WorldPvp.Phase1.UI
{
    /// <summary>
    /// Client-only combat presentation and spectator camera. Crosshair feedback, damage indicators,
    /// effects and kill feed react only to server-broadcast CombatEvents and replicated player state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhaseSevenCombatHud : MonoBehaviour
    {
        private const int EffectPoolCapacity = 24;
        private const int MaximumFeedEntries = 5;
        private const float FeedLifetimeSeconds = 8f;
        private const float TargetRefreshIntervalSeconds = 0.5f;
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorPropertyId = Shader.PropertyToID("_Color");

        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private BattleSessionCoordinator coordinator;
        [SerializeField] private PhaseOneWorldSettings settings;
        [SerializeField] private WeaponDefinition presentationWeapon;

        private sealed class PooledEffect
        {
            public GameObject Root;
            public Renderer Renderer;
            public Light Light;
            public MaterialPropertyBlock Properties;
            public float StartedAt;
            public float Lifetime;
            public float InitialScale;
            public float InitialLightIntensity;
        }

        private struct FeedEntry
        {
            public string Text;
            public float ExpiresAt;
        }

        private readonly List<PooledEffect> effects = new List<PooledEffect>(EffectPoolCapacity);
        private readonly List<FeedEntry> killFeed = new List<FeedEntry>(MaximumFeedEntries);
        private readonly List<NetworkPlayer> spectatorTargets = new List<NetworkPlayer>(16);
        private readonly List<MatchScoreEntry> scoreboardScratch = new List<MatchScoreEntry>(32);
        private AudioSource audioSource;
        private AudioClip generatedGunshotClip;
        private AudioClip generatedHitClip;
        private AudioClip generatedReloadClip;
        private Material effectMaterial;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private NetworkPlayer localPlayer;
        private NetworkPlayer spectatorTarget;
        private CombatMatchController matchController;
        private int spectatorTargetIndex;
        private float nextTargetRefreshTime;
        private float hitMarkerExpiresAt;
        private float damageFlashExpiresAt;
        private ushort lastDamageAmount;
        private string lastDamageSource = string.Empty;
        private bool cameraDetached;
        private bool matchEnded;
        private bool hasWinner;
        private string winnerName = string.Empty;
        private bool wasReloading;

        public void Configure(
            GeospatialWorldManager manager,
            BattleSessionCoordinator sessionCoordinator,
            PhaseOneWorldSettings worldSettings,
            WeaponDefinition weaponDefinition)
        {
            worldManager = manager;
            coordinator = sessionCoordinator;
            settings = worldSettings;
            presentationWeapon = weaponDefinition;
        }

        private void OnEnable()
        {
            NetworkPlayer.CombatEventReceived += OnCombatEvent;
        }

        private void OnDisable()
        {
            NetworkPlayer.CombatEventReceived -= OnCombatEvent;
            RestoreSpectatorCamera();
        }

        private void Start()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 2f;
            audioSource.maxDistance = 140f;
            audioSource.volume = 0.8f;
            generatedGunshotClip = CreateProceduralCue("Phase7_Gunshot", 0.13f, 110f, 0.55f);
            generatedHitClip = CreateProceduralCue("Phase7_Hit", 0.09f, 520f, 0.30f);
            generatedReloadClip = CreateProceduralCue("Phase7_Reload", 0.20f, 260f, 0.08f);
            CreateEffectPool();
        }

        private void Update()
        {
            UpdateEffects();
            ExpireFeedEntries();

            if (coordinator == null || coordinator.State != BattleFlowState.InGameplay)
            {
                RestoreSpectatorCamera();
                localPlayer = null;
                matchEnded = false;
                hasWinner = false;
                winnerName = string.Empty;
                return;
            }

            if (matchController == null)
            {
                matchController = FindObjectOfType<CombatMatchController>();
            }

            NetworkPlayer currentLocal = coordinator.LocalNetworkPlayer;
            if (localPlayer != currentLocal)
            {
                RestoreSpectatorCamera();
                localPlayer = currentLocal;
                spectatorTarget = null;
                wasReloading = false;
            }
            if (localPlayer == null || !localPlayer.HasAuthoritativeState)
            {
                return;
            }

            NetworkPlayerSnapshot state = localPlayer.CurrentState;
            bool eliminated = !state.Alive || state.Health == 0;
            bool frozen = state.MatchFinished;
            if (eliminated)
            {
                EnterSpectatorCamera();
                if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                {
                    CycleSpectatorTarget();
                }
                if (Time.unscaledTime >= nextTargetRefreshTime)
                {
                    nextTargetRefreshTime = Time.unscaledTime + TargetRefreshIntervalSeconds;
                    RefreshSpectatorTargets();
                }
                UpdateSpectatorCamera(state);
            }
            else
            {
                RestoreSpectatorCamera();
                if (frozen && localPlayer.LocalMotor != null)
                {
                    localPlayer.LocalMotor.SetInputEnabled(false);
                }
            }

            if (state.IsReloading && !wasReloading)
            {
                PlaySound(presentationWeapon != null ? presentationWeapon.ReloadSound : null,
                    localPlayer.LocalMotor != null && localPlayer.LocalMotor.ViewCamera != null
                        ? localPlayer.LocalMotor.ViewCamera.transform.position
                        : transform.position,
                    generatedReloadClip);
            }
            wasReloading = state.IsReloading;
        }

        private void OnCombatEvent(CombatEvent combatEvent)
        {
            NetworkManager manager = NetworkManager.Singleton;
            ulong localId = manager != null ? manager.LocalClientId : ulong.MaxValue;

            if (combatEvent.Type == CombatEventType.MatchEnded)
            {
                matchEnded = true;
                hasWinner = combatEvent.HasWinner;
                winnerName = combatEvent.WinnerName.ToString();
                string result = combatEvent.HasWinner
                    ? winnerName + " won the match."
                    : "The match ended with no survivor.";
                AddFeedEntry("MATCH OVER  ·  " + result);
                return;
            }

            if (combatEvent.Type == CombatEventType.ShotResolved)
            {
                PresentConfirmedShot(combatEvent, localId);
                if (combatEvent.Hit && combatEvent.ShooterId == localId)
                {
                    hitMarkerExpiresAt = Time.unscaledTime + 0.28f;
                }
                if (combatEvent.Hit && combatEvent.VictimId == localId)
                {
                    damageFlashExpiresAt = Time.unscaledTime + 0.48f;
                    lastDamageAmount = combatEvent.Damage;
                    lastDamageSource = combatEvent.ShooterName.ToString();
                }
                return;
            }

            if (combatEvent.Type == CombatEventType.PlayerDied)
            {
                AddFeedEntry(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} eliminated {1}  ·  {2}",
                    combatEvent.ShooterName.ToString(),
                    combatEvent.VictimName.ToString(),
                    combatEvent.HitboxZone));
            }
        }

        private void PresentConfirmedShot(CombatEvent combatEvent, ulong localId)
        {
            Vector3 worldOrigin;
            if (combatEvent.ShooterId == localId && localPlayer != null &&
                localPlayer.LocalMotor != null && localPlayer.LocalMotor.ViewCamera != null)
            {
                Transform view = localPlayer.LocalMotor.ViewCamera.transform;
                worldOrigin = view.position + view.forward * 0.50f + view.right * 0.20f - view.up * 0.17f;
            }
            else if (worldManager != null && worldManager.IsReady)
            {
                LocalPosition origin = new LocalPosition(
                    combatEvent.OriginEastMeters,
                    combatEvent.OriginUpMeters,
                    combatEvent.OriginNorthMeters);
                try
                {
                    worldOrigin = worldManager.GeographicToUnityWorld(worldManager.LocalToGeographic(origin));
                }
                catch (Exception)
                {
                    return;
                }
            }
            else
            {
                return;
            }

            SpawnEffect(worldOrigin, new Color(1f, 0.72f, 0.22f, 1f), 0.07f, 0.12f, true);
            Vector3 soundPosition = combatEvent.ShooterId == localId && localPlayer != null &&
                                    localPlayer.LocalMotor != null && localPlayer.LocalMotor.ViewCamera != null
                ? localPlayer.LocalMotor.ViewCamera.transform.position
                : worldOrigin;
            PlaySound(
                presentationWeapon != null ? presentationWeapon.FireSound : null,
                soundPosition,
                generatedGunshotClip);

            if (!combatEvent.HasImpact || worldManager == null || !worldManager.IsReady)
            {
                return;
            }

            LocalPosition impact = new LocalPosition(
                combatEvent.ImpactEastMeters,
                combatEvent.ImpactUpMeters,
                combatEvent.ImpactNorthMeters);
            try
            {
                Vector3 impactWorld = worldManager.GeographicToUnityWorld(worldManager.LocalToGeographic(impact));
                Color impactColor = combatEvent.Hit
                    ? new Color(0.88f, 0.08f, 0.12f, 1f)
                    : new Color(0.80f, 0.88f, 0.95f, 1f);
                SpawnEffect(impactWorld, impactColor, combatEvent.Hit ? 0.20f : 0.12f,
                    combatEvent.Hit ? 0.16f : 0.07f, combatEvent.Hit);
                if (combatEvent.Hit)
                {
                    PlaySound(generatedHitClip, impactWorld, generatedHitClip);
                }
            }
            catch (Exception)
            {
                // A transient Cesium/origin change only drops this cosmetic effect.
            }
        }

        private void EnterSpectatorCamera()
        {
            if (localPlayer == null || localPlayer.LocalMotor == null)
            {
                return;
            }

            localPlayer.LocalMotor.SetInputEnabled(false);
            localPlayer.LocalMotor.SetSpectatorCameraDetached(true);
            cameraDetached = localPlayer.LocalMotor.IsSpectatorCameraDetached;
        }

        private void RestoreSpectatorCamera()
        {
            if (localPlayer != null && localPlayer.LocalMotor != null && cameraDetached)
            {
                localPlayer.LocalMotor.SetSpectatorCameraDetached(false);
            }
            cameraDetached = false;
            spectatorTarget = null;
            spectatorTargets.Clear();
        }

        private void RefreshSpectatorTargets()
        {
            spectatorTargets.Clear();
            NetworkPlayer[] candidates = FindObjectsOfType<NetworkPlayer>();
            for (int i = 0; i < candidates.Length; i++)
            {
                NetworkPlayer candidate = candidates[i];
                if (candidate == null || candidate == localPlayer || !candidate.IsSpawned ||
                    !candidate.HasAuthoritativeState)
                {
                    continue;
                }
                NetworkPlayerSnapshot state = candidate.CurrentState;
                if (state.Alive && state.Health > 0 && !state.MatchFinished)
                {
                    spectatorTargets.Add(candidate);
                }
            }
            spectatorTargets.Sort((left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));

            if (spectatorTarget != null && spectatorTargets.Contains(spectatorTarget))
            {
                spectatorTargetIndex = spectatorTargets.IndexOf(spectatorTarget);
                return;
            }
            spectatorTargetIndex = 0;
            spectatorTarget = spectatorTargets.Count > 0 ? spectatorTargets[0] : null;
        }

        private void CycleSpectatorTarget()
        {
            if (spectatorTargets.Count == 0)
            {
                RefreshSpectatorTargets();
            }
            if (spectatorTargets.Count == 0)
            {
                spectatorTarget = null;
                return;
            }

            spectatorTargetIndex = (spectatorTargetIndex + 1) % spectatorTargets.Count;
            spectatorTarget = spectatorTargets[spectatorTargetIndex];
        }

        private void UpdateSpectatorCamera(NetworkPlayerSnapshot localState)
        {
            if (localPlayer == null || localPlayer.LocalMotor == null ||
                worldManager == null || !worldManager.IsReady)
            {
                return;
            }

            NetworkPlayerSnapshot targetState = localState;
            bool hasLivingTarget = spectatorTarget != null && spectatorTarget.HasAuthoritativeState &&
                                   spectatorTarget.CurrentState.Alive && spectatorTarget.CurrentState.Health > 0;
            if (hasLivingTarget)
            {
                targetState = spectatorTarget.CurrentState;
            }

            try
            {
                LocalPosition foot = new LocalPosition(
                    targetState.EastMeters,
                    targetState.UpMeters,
                    targetState.NorthMeters);
                LocalPosition focus = new LocalPosition(
                    foot.EastMeters,
                    foot.UpMeters + (settings != null ? settings.CharacterHeightMeters * 0.62f : 1.1f),
                    foot.NorthMeters);
                Vector3 focusWorld = worldManager.GeographicToUnityWorld(worldManager.LocalToGeographic(focus));
                Vector3 upWorld = worldManager.LocalDirectionToUnityWorld(focus, Vector3.up);
                Vector3 forwardEnu = CombatMath.AimDirectionFromYawPitch(targetState.YawDegrees, 0f);
                Vector3 forwardWorld = worldManager.LocalDirectionToUnityWorld(focus, forwardEnu);

                Vector3 cameraPosition;
                Vector3 cameraLookAt;
                if (hasLivingTarget)
                {
                    cameraPosition = focusWorld - forwardWorld * 3.5f + upWorld * 1.4f;
                    cameraLookAt = focusWorld;
                }
                else
                {
                    LocalPosition deathEye = new LocalPosition(
                        foot.EastMeters,
                        foot.UpMeters + (settings != null ? settings.FirstPersonEyeHeightMeters : 1.62f),
                        foot.NorthMeters);
                    cameraPosition = worldManager.GeographicToUnityWorld(worldManager.LocalToGeographic(deathEye));
                    Vector3 look = CombatMath.AimDirectionFromYawPitch(
                        targetState.YawDegrees,
                        targetState.PitchDegrees);
                    cameraLookAt = cameraPosition + worldManager.LocalDirectionToUnityWorld(deathEye, look) * 30f;
                }

                Quaternion rotation = Quaternion.LookRotation(cameraLookAt - cameraPosition, upWorld);
                localPlayer.LocalMotor.SetSpectatorCameraPose(cameraPosition, rotation);
            }
            catch (Exception)
            {
                // Spectator presentation fails closed and does not modify authoritative movement.
            }
        }

        private void CreateEffectPool()
        {
            if (effects.Count > 0)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            effectMaterial = shader != null ? new Material(shader) : null;
            if (effectMaterial != null)
            {
                effectMaterial.name = "WorldPvp_Phase7_CombatEffect";
            }

            Transform parent = worldManager != null && worldManager.Georeference != null
                ? worldManager.Georeference.transform
                : transform;
            for (int i = 0; i < EffectPoolCapacity; i++)
            {
                GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                root.name = "Phase7CombatEffect_" + i;
                root.transform.SetParent(parent, false);
                Collider collider = root.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }
                Renderer renderer = root.GetComponent<Renderer>();
                if (renderer != null && effectMaterial != null)
                {
                    renderer.sharedMaterial = effectMaterial;
                }
                Light light = root.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 4f;
                light.intensity = 0f;
                root.SetActive(false);
                effects.Add(new PooledEffect
                {
                    Root = root,
                    Renderer = renderer,
                    Light = light,
                    Properties = new MaterialPropertyBlock()
                });
            }

        }

        private void SpawnEffect(Vector3 position, Color color, float lifetime, float initialScale, bool lit)
        {
            PooledEffect chosen = null;
            for (int i = 0; i < effects.Count; i++)
            {
                if (!effects[i].Root.activeSelf || Time.unscaledTime >= effects[i].StartedAt + effects[i].Lifetime)
                {
                    chosen = effects[i];
                    break;
                }
            }
            if (chosen == null || chosen.Root == null)
            {
                return;
            }

            chosen.StartedAt = Time.unscaledTime;
            chosen.Lifetime = Mathf.Max(0.02f, lifetime);
            chosen.InitialScale = Mathf.Max(0.01f, initialScale);
            chosen.InitialLightIntensity = lit ? 2.4f : 0f;
            chosen.Root.transform.SetPositionAndRotation(position, Quaternion.identity);
            chosen.Root.transform.localScale = Vector3.one * chosen.InitialScale;
            chosen.Root.SetActive(true);
            if (chosen.Renderer != null)
            {
                Color renderColor = color;
                renderColor.a = 1f;
                chosen.Properties.Clear();
                chosen.Properties.SetColor(BaseColorPropertyId, renderColor);
                chosen.Properties.SetColor(LegacyColorPropertyId, renderColor);
                chosen.Renderer.SetPropertyBlock(chosen.Properties);
            }
            if (chosen.Light != null)
            {
                chosen.Light.color = color;
                chosen.Light.intensity = chosen.InitialLightIntensity;
            }
        }

        private void UpdateEffects()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < effects.Count; i++)
            {
                PooledEffect effect = effects[i];
                if (effect.Root == null || !effect.Root.activeSelf)
                {
                    continue;
                }

                float normalized = Mathf.Clamp01((now - effect.StartedAt) / effect.Lifetime);
                if (normalized >= 1f)
                {
                    effect.Root.SetActive(false);
                    continue;
                }

                float scale = effect.InitialScale * Mathf.Lerp(1f, 2.3f, normalized);
                effect.Root.transform.localScale = Vector3.one * scale;
                if (effect.Light != null)
                {
                    effect.Light.intensity = effect.InitialLightIntensity * (1f - normalized);
                }
            }
        }

        private void PlaySound(AudioClip preferred, Vector3 position, AudioClip fallback)
        {
            if (audioSource == null)
            {
                return;
            }
            AudioClip clip = preferred != null ? preferred : fallback;
            if (clip == null)
            {
                return;
            }
            audioSource.transform.position = position;
            audioSource.PlayOneShot(clip, 0.75f);
        }

        private static AudioClip CreateProceduralCue(string clipName, float duration, float frequency, float noiseMix)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.Max(64, Mathf.CeilToInt(duration * sampleRate));
            float[] samples = new float[sampleCount];
            uint random = 0xA341316Cu;
            float filteredNoise = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)sampleRate;
                random ^= random << 13;
                random ^= random >> 17;
                random ^= random << 5;
                float noise = ((random & 0xFFFFu) / 32767.5f) - 1f;
                filteredNoise += (noise - filteredNoise) * 0.24f;
                float envelope = Mathf.Exp(-time * 32f);
                float tone = Mathf.Sin(time * frequency * Mathf.PI * 2f) * (1f - noiseMix);
                samples[i] = Mathf.Clamp((tone + filteredNoise * noiseMix) * envelope, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void AddFeedEntry(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }
            while (killFeed.Count >= MaximumFeedEntries)
            {
                killFeed.RemoveAt(0);
            }
            killFeed.Add(new FeedEntry
            {
                Text = message,
                ExpiresAt = Time.unscaledTime + FeedLifetimeSeconds
            });
        }

        private void ExpireFeedEntries()
        {
            for (int i = killFeed.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime >= killFeed[i].ExpiresAt)
                {
                    killFeed.RemoveAt(i);
                }
            }
        }

        private void OnGUI()
        {
            if (coordinator == null || coordinator.State != BattleFlowState.InGameplay || localPlayer == null ||
                !localPlayer.HasAuthoritativeState)
            {
                return;
            }

            EnsureStyles();
            NetworkPlayerSnapshot state = localPlayer.CurrentState;
            if (Time.unscaledTime < damageFlashExpiresAt)
            {
                float alpha = 0.16f * Mathf.Clamp01((damageFlashExpiresAt - Time.unscaledTime) / 0.48f);
                GUI.color = new Color(0.88f, 0.04f, 0.07f, alpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(18f, Screen.height * 0.62f, 280f, 26f),
                    "-" + lastDamageAmount.ToString(CultureInfo.InvariantCulture) + "  FROM  " + lastDamageSource,
                    bodyStyle);
            }

            DrawMatchStatusAndScoreboard();
            DrawKillFeed();
            if (state.Alive && state.Health > 0 && !state.MatchFinished)
            {
                DrawCrosshair();
                DrawPlayerStatus(state);
            }

            bool eliminated = !state.Alive || state.Health == 0;
            if (eliminated)
            {
                DrawSpectatorOverlay();
            }
            else if (state.MatchFinished || matchEnded)
            {
                DrawMatchEndedOverlay();
            }
        }

        private void DrawMatchStatusAndScoreboard()
        {
            if (matchController == null || !matchController.IsSpawned)
            {
                return;
            }

            scoreboardScratch.Clear();
            NetworkList<MatchScoreEntry> entries = matchController.ScoreEntries;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    scoreboardScratch.Add(entries[i]);
                }
            }
            scoreboardScratch.Sort((left, right) =>
            {
                int scoreOrder = right.Kills.CompareTo(left.Kills);
                return scoreOrder != 0 ? scoreOrder : left.PlayerId.CompareTo(right.PlayerId);
            });

            int seconds = matchController.MatchRemainingSeconds;
            string timer = string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}",
                seconds / 60,
                seconds % 60);
            int visibleRows = Mathf.Min(6, scoreboardScratch.Count);
            float width = 230f;
            float height = 46f + visibleRows * 22f;
            Rect panel = new Rect(Screen.width - width - 16f, 14f, width, height);
            GUI.color = new Color(0.015f, 0.025f, 0.04f, 0.84f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 10f, panel.y + 5f, panel.width - 20f, 22f),
                "ROUND  " + timer + "     ·     NO RESPAWNS",
                titleStyle);

            for (int i = 0; i < visibleRows; i++)
            {
                MatchScoreEntry entry = scoreboardScratch[i];
                string row = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.  {1}  ·  {2} K",
                    i + 1,
                    entry.DisplayName.ToString(),
                    entry.Kills);
                GUI.Label(new Rect(panel.x + 10f, panel.y + 26f + i * 22f,
                    panel.width - 20f, 20f), row, bodyStyle);
            }
        }

        private void DrawCrosshair()
        {
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Color crosshairColor = Color.white;
            if (Time.unscaledTime < hitMarkerExpiresAt)
            {
                DrawCross(centre, 13f, 7f, 2.4f, new Color(0.96f, 0.97f, 1f, 1f));
            }
            else
            {
                DrawLine(centre + Vector2.left * 7f, centre + Vector2.left * 2.5f, 2f, crosshairColor);
                DrawLine(centre + Vector2.right * 2.5f, centre + Vector2.right * 7f, 2f, crosshairColor);
                DrawLine(centre + Vector2.up * 7f, centre + Vector2.up * 2.5f, 2f, crosshairColor);
                DrawLine(centre + Vector2.down * 2.5f, centre + Vector2.down * 7f, 2f, crosshairColor);
            }
        }

        private void DrawPlayerStatus(NetworkPlayerSnapshot state)
        {
            float width = 230f;
            Rect panel = new Rect(Screen.width - width - 16f, Screen.height - 84f, width, 66f);
            GUI.color = new Color(0.015f, 0.025f, 0.04f, 0.84f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            string displayName = presentationWeapon != null
                ? presentationWeapon.DisplayName
                : state.EquippedWeaponId.ToString();
            GUI.Label(new Rect(panel.x + 10f, panel.y + 5f, panel.width - 20f, 20f), displayName, titleStyle);
            string ammo = state.IsReloading
                ? "RELOADING"
                : state.MagazineAmmo.ToString(CultureInfo.InvariantCulture) + " / " +
                  (localPlayer.EquippedWeapon != null
                      ? localPlayer.EquippedWeapon.MagazineCapacity.ToString(CultureInfo.InvariantCulture)
                      : "?");
            GUI.Label(new Rect(panel.x + 10f, panel.y + 27f, panel.width - 20f, 20f),
                "HEALTH  " + state.Health.ToString(CultureInfo.InvariantCulture) +
                "     ·     AMMO  " + ammo,
                bodyStyle);
        }

        private void DrawKillFeed()
        {
            float y = 174f;
            for (int i = killFeed.Count - 1; i >= 0; i--)
            {
                Rect item = new Rect(12f, y, 360f, 24f);
                GUI.color = new Color(0.02f, 0.025f, 0.04f, 0.82f);
                GUI.DrawTexture(item, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(item.x + 8f, item.y + 2f, item.width - 16f, 20f), killFeed[i].Text, bodyStyle);
                y += 27f;
            }
        }

        private void DrawSpectatorOverlay()
        {
            Rect panel = new Rect(Screen.width * 0.5f - 240f, 50f, 480f, 78f);
            GUI.color = new Color(0.02f, 0.015f, 0.025f, 0.82f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 12f, panel.y + 7f, panel.width - 24f, 28f), "ELIMINATED  ·  SPECTATOR MODE", titleStyle);
            string detail;
            if (matchEnded)
            {
                detail = hasWinner ? winnerName + " won. Press F1 to leave or review the match." : "Match ended with no survivor.";
            }
            else if (spectatorTarget != null && spectatorTarget.HasAuthoritativeState)
            {
                detail = "Watching " + spectatorTarget.CurrentState.DisplayName.ToString() + "  ·  TAB cycles players";
            }
            else
            {
                detail = "No living opponent to follow  ·  waiting for the match to end";
            }
            GUI.Label(new Rect(panel.x + 12f, panel.y + 39f, panel.width - 24f, 25f), detail, bodyStyle);
        }

        private void DrawMatchEndedOverlay()
        {
            Rect panel = new Rect(Screen.width * 0.5f - 220f, 44f, 440f, 62f);
            GUI.color = new Color(0.02f, 0.03f, 0.04f, 0.82f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 12f, panel.y + 7f, panel.width - 24f, 24f),
                hasWinner ? "MATCH COMPLETE  ·  " + winnerName + " WINS" : "MATCH COMPLETE  ·  NO SURVIVOR",
                titleStyle);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 33f, panel.width - 24f, 20f),
                "Combat is frozen. Spectate or leave the session when ready.", bodyStyle);
        }

        private void EnsureStyles()
        {
            if (panelStyle != null)
            {
                return;
            }
            panelStyle = new GUIStyle(GUI.skin.box);
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
            titleStyle.normal.textColor = new Color(0.91f, 0.95f, 1f, 1f);
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
            bodyStyle.normal.textColor = new Color(0.86f, 0.91f, 0.96f, 1f);
        }

        private static void DrawCross(Vector2 centre, float outer, float inner, float thickness, Color color)
        {
            DrawLine(centre + new Vector2(-outer, -outer), centre + new Vector2(-inner, -inner), thickness, color);
            DrawLine(centre + new Vector2(outer, -outer), centre + new Vector2(inner, -inner), thickness, color);
            DrawLine(centre + new Vector2(-outer, outer), centre + new Vector2(-inner, inner), thickness, color);
            DrawLine(centre + new Vector2(outer, outer), centre + new Vector2(inner, inner), thickness, color);
        }

        private static void DrawLine(Vector2 start, Vector2 end, float thickness, Color color)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.01f)
            {
                return;
            }
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.color = color;
            GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
            GUI.matrix = previous;
            GUI.color = Color.white;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Root != null)
                {
                    Destroy(effects[i].Root);
                }
            }
            effects.Clear();

            if (effectMaterial != null)
            {
                Destroy(effectMaterial);
                effectMaterial = null;
            }
            if (generatedGunshotClip != null) Destroy(generatedGunshotClip);
            if (generatedHitClip != null) Destroy(generatedHitClip);
            if (generatedReloadClip != null) Destroy(generatedReloadClip);
            if (audioSource != null) Destroy(audioSource);
        }
    }
}
