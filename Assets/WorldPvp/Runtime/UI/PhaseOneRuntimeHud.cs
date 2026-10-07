using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;
using WorldPvp.Phase1.Player;

namespace WorldPvp.Phase1.UI
{
    /// <summary>
    /// Local IMGUI harness for the geospatial arena proof. API keys are masked and never serialized.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhaseOneRuntimeHud : MonoBehaviour
    {
        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private PhaseOneTestCharacterController characterMotor;
        [SerializeField] private PhaseOneWorldSettings settings;

        private string latitudeText = "51.5073";
        private string longitudeText = "-0.1657";
        private string altitudeText = "0";
        private string radiusText = "500";
        private string apiKeyText = string.Empty;
        private bool panelVisible = true;
        private string validationMessage = string.Empty;
        private Vector2 scrollPosition;

        public void Configure(
            GeospatialWorldManager manager,
            PhaseOneTestCharacterController motor,
            PhaseOneWorldSettings worldSettings)
        {
            worldManager = manager;
            characterMotor = motor;
            settings = worldSettings;
            ApplySettingsDefaults();
        }

        private void Awake()
        {
            ApplySettingsDefaults();
            apiKeyText = GoogleMapTilesKeyReader.TryReadLocalKey();
            panelVisible = true;
            ApplyInputMode();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                panelVisible = !panelVisible;
                ApplyInputMode();
            }
            else if (panelVisible && keyboard.escapeKey.wasPressedThisFrame)
            {
                panelVisible = false;
                ApplyInputMode();
            }
        }

        private void OnGUI()
        {
            if (worldManager == null)
            {
                DrawNotice("World scene references are missing. Use Tools → World PvP → Phase 2 → Build or Rebuild Scene.");
                return;
            }

            DrawStatusStrip();
            if (panelVisible)
            {
                DrawLocationPanel();
            }
        }

        private void ApplySettingsDefaults()
        {
            if (settings == null)
            {
                return;
            }

            GeoPosition origin = settings.DefaultOrigin;
            latitudeText = origin.LatitudeDegrees.ToString("F6", CultureInfo.InvariantCulture);
            longitudeText = origin.LongitudeDegrees.ToString("F6", CultureInfo.InvariantCulture);
            altitudeText = origin.AltitudeMeters.ToString("F1", CultureInfo.InvariantCulture);
            radiusText = settings.DefaultGameplayRadiusMeters.ToString("F0", CultureInfo.InvariantCulture);
        }

        private void DrawStatusStrip()
        {
            Rect rect = new Rect(12f, 12f, 650f, 188f);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f));
            GUILayout.Label("WORLD PvP  /  PHASE 2 GEOSPATIAL ARENA");
            GUILayout.Label("State: " + worldManager.State + " — " + worldManager.StatusMessage);
            GUILayout.Label(string.Format(
                CultureInfo.InvariantCulture,
                "Gameplay {0:F0} m  ·  visible {1:F0} m  ·  render/preload envelope {2:F0} m",
                worldManager.GameplayRadiusMeters,
                worldManager.VisibilityRadiusMeters,
                worldManager.RenderRadiusMeters));

            GeoPosition playerPosition;
            if (worldManager.TryGetPlayerGeographicPosition(out playerPosition))
            {
                LocalPosition localPosition = worldManager.GeographicToLocal(playerPosition);
                GUILayout.Label("Player WGS84: " + playerPosition);
                GUILayout.Label(string.Format(
                    CultureInfo.InvariantCulture,
                    "Player ENU: {0}  ·  arena distance: {1:F1} m",
                    localPosition,
                    worldManager.GetDistanceFromArenaCentre(playerPosition)));
                if (characterMotor != null)
                {
                    GUILayout.Label("Local speed: " +
                        characterMotor.CurrentPlanarSpeedMetersPerSecond.ToString("F1", CultureInfo.InvariantCulture) +
                        " m/s  ·  " + worldManager.BoundaryFeedback);
                }
                else
                {
                    GUILayout.Label(worldManager.BoundaryFeedback);
                }
            }
            else
            {
                GUILayout.Label("Boundary rings are reference graphics only; the local manager separately clamps prototype movement.");
            }

            GUILayout.Label(panelVisible
                ? "Use the arena panel; controls activate after ground collision is found."
                : "WASD move · Mouse look · Left Shift sprint · Space jump · F1/Esc settings");
            GUILayout.EndArea();
        }

        private void DrawLocationPanel()
        {
            float panelY = 210f;
            float panelHeight = Mathf.Max(260f, Mathf.Min(650f, Screen.height - panelY - 12f));
            Rect panel = new Rect(12f, panelY, 520f, panelHeight);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, panel.height - 20f));
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Choose a WGS84 arena centre");
            GUILayout.Label("Latitude / longitude are degrees. Altitude is metres above the WGS84 ellipsoid.");
            GUILayout.Space(6f);

            DrawCoordinateRow("Latitude", ref latitudeText);
            DrawCoordinateRow("Longitude", ref longitudeText);
            DrawCoordinateRow("Origin altitude (ellipsoid m)", ref altitudeText);
            DrawCoordinateRow("Gameplay radius (real metres)", ref radiusText);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("100 m", GUILayout.Height(28f))) SetRadius(100.0);
            if (GUILayout.Button("500 m", GUILayout.Height(28f))) SetRadius(500.0);
            if (GUILayout.Button("1 km", GUILayout.Height(28f))) SetRadius(1000.0);
            GUILayout.EndHorizontal();

            double visibilityBuffer = settings != null ? settings.VisibilityBufferMeters : 100.0;
            double preloadBuffer = settings != null ? settings.PreloadBufferMeters : 150.0;
            GUILayout.Label(string.Format(
                CultureInfo.InvariantCulture,
                "Buffers: +{0:F0} m visibility, then +{1:F0} m preload beyond it.",
                visibilityBuffer,
                preloadBuffer));
            GUILayout.Label("Render radius = gameplay + visibility + preload. Cesium itself streams by camera view/frustum.");
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Hyde Park (London)", GUILayout.Height(28f)))
            {
                SetCoordinates(51.5073, -0.1657, 0.0);
            }
            if (GUILayout.Button("Harefield area", GUILayout.Height(28f)))
            {
                SetCoordinates(51.6030, -0.4840, 0.0);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            GUILayout.Label("Google Map Tiles API key (masked; local Play Mode only)");
            apiKeyText = GUILayout.PasswordField(apiKeyText, '\u2022', GUILayout.Height(26f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Read local key again", GUILayout.Height(26f)))
            {
                apiKeyText = GoogleMapTilesKeyReader.TryReadLocalKey();
            }
            GUILayout.Label("env / ignored local file", GUILayout.Width(150f));
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            if (GUILayout.Button("Load geographic arena", GUILayout.Height(36f)))
            {
                BeginArenaFromFields();
            }
            if (!string.IsNullOrEmpty(validationMessage))
            {
                GUILayout.Label(validationMessage);
            }

            GUILayout.Space(4f);
            if (worldManager.IsReady)
            {
                if (GUILayout.Button("Close panel and start walking", GUILayout.Height(32f)))
                {
                    panelVisible = false;
                    ApplyInputMode();
                }
            }
            else if (worldManager.State == GeospatialWorldState.Failed)
            {
                if (GUILayout.Button("Retry selected arena", GUILayout.Height(30f)))
                {
                    BeginArenaFromFields();
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Controls: WASD · Mouse · Left Shift · Space · F1/Esc settings");
            GUILayout.Label("Boundary rings are visual only. A local prototype clamp runs after movement.");
            GUILayout.Label("No multiplayer server exists in this phase; future server code must call IsInsideArena.");
            GUILayout.Label("Google attribution is displayed by Cesium and must remain unobscured.");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCoordinateRow(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(200f));
            value = GUILayout.TextField(value, GUILayout.Height(24f));
            GUILayout.EndHorizontal();
        }

        private void BeginArenaFromFields()
        {
            double latitude;
            double longitude;
            double altitude;
            double radius;
            if (!TryParseFinite(latitudeText, out latitude) ||
                !TryParseFinite(longitudeText, out longitude) ||
                !TryParseFinite(altitudeText, out altitude) ||
                !TryParseFinite(radiusText, out radius))
            {
                validationMessage = "Enter finite numeric latitude, longitude, ellipsoid altitude, and radius values.";
                return;
            }

            GeoPosition centre = new GeoPosition(latitude, longitude, altitude);
            if (!centre.IsValid)
            {
                validationMessage = "Latitude must be -90…90 and longitude -180…180 degrees.";
                return;
            }
            if (radius <= 0.0 || radius > 50000.0)
            {
                validationMessage = "Gameplay radius must be greater than zero and no more than 50,000 metres.";
                return;
            }

            validationMessage = string.Empty;
            worldManager.BeginArena(centre, radius, apiKeyText);
        }

        private void SetCoordinates(double latitude, double longitude, double altitude)
        {
            latitudeText = latitude.ToString("F6", CultureInfo.InvariantCulture);
            longitudeText = longitude.ToString("F6", CultureInfo.InvariantCulture);
            altitudeText = altitude.ToString("F1", CultureInfo.InvariantCulture);
        }

        private void SetRadius(double radiusMeters)
        {
            radiusText = radiusMeters.ToString("F0", CultureInfo.InvariantCulture);
        }

        private void ApplyInputMode()
        {
            if (characterMotor != null)
            {
                characterMotor.SetInputEnabled(!panelVisible && worldManager != null && worldManager.IsReady);
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private static bool TryParseFinite(string text, out double value)
        {
            return double.TryParse(
                       text,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   GeoPosition.IsFinite(value);
        }

        private void DrawNotice(string message)
        {
            GUI.Box(new Rect(12f, 12f, 600f, 56f), message);
        }
    }
}
