using System.Globalization;
using UnityEngine;
using WorldPvp.Phase1.Battles;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.UI
{
    /// <summary>
    /// Phase 5 in-game geographic debug readout and local ENU minimap. This is a small Unity
    /// overlay, not a second map renderer and not an additional source of geographic truth.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhaseFiveGameplayHud : MonoBehaviour
    {
        private const int PlayerRefreshFrameInterval = 15;
        private const int CircleSegmentCount = 48;
        private const float PanelPadding = 10f;

        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private PhaseOneWorldSettings settings;
        [SerializeField] private BattleSessionCoordinator coordinator;

        private NetworkPlayer[] players = new NetworkPlayer[0];
        private int nextPlayerRefreshFrame;
        private GUIStyle debugStyle;
        private GUIStyle smallStyle;

        public void Configure(
            GeospatialWorldManager manager,
            PhaseOneWorldSettings worldSettings,
            BattleSessionCoordinator sessionCoordinator)
        {
            worldManager = manager;
            settings = worldSettings;
            coordinator = sessionCoordinator;
        }

        private void OnGUI()
        {
            if (worldManager == null || coordinator == null ||
                coordinator.State != BattleFlowState.InGameplay)
            {
                return;
            }

            EnsureStyles();
            RefreshPlayerCache();
            DrawGeographicDebug();
            DrawMinimap();
        }

        private void DrawGeographicDebug()
        {
            Rect rect = new Rect(12f, 48f, 332f, 66f);
            GUI.color = new Color(0.015f, 0.03f, 0.05f, 0.84f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GeoPosition geographic;
            if (!worldManager.TryGetPlayerGeographicPosition(out geographic) || !geographic.IsValid)
            {
                GUI.Label(new Rect(rect.x + PanelPadding, rect.y + 8f, rect.width - 20f, 50f),
                    "GEO DEBUG  ·  waiting for the anchored player position", debugStyle);
                return;
            }

            string text = string.Format(
                CultureInfo.InvariantCulture,
                "GEO DEBUG   LAT {0:F6}   LON {1:F6}\nALT {2:F1} m ellipsoid   ·   host-simulated ENU",
                geographic.LatitudeDegrees,
                geographic.LongitudeDegrees,
                geographic.AltitudeMeters);
            GUI.Label(new Rect(rect.x + PanelPadding, rect.y + 8f, rect.width - 20f, 52f), text, debugStyle);
        }

        private void DrawMinimap()
        {
            float size = Mathf.Clamp(Screen.width * 0.19f, 148f, 205f);
            Rect panel = new Rect(Screen.width - size - 14f, 18f, size, size);
            GUI.color = new Color(0.012f, 0.025f, 0.04f, 0.88f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.BeginGroup(panel);
            Vector2 centre = new Vector2(size * 0.5f, size * 0.54f);
            float usableRadius = size * 0.36f;
            float arenaRadius = Mathf.Max(1f, (float)worldManager.GameplayRadiusMeters);
            float displayRange = Mathf.Max(
                arenaRadius,
                settings != null ? settings.MinimapMinimumRangeMeters : 150f);
            float pixelsPerMeter = usableRadius / displayRange;
            float boundaryPixels = arenaRadius * pixelsPerMeter;

            DrawCircle(centre, boundaryPixels, new Color(0.82f, 0.88f, 0.91f, 0.92f), 1.5f);
            DrawCircle(centre, boundaryPixels * 0.5f, new Color(0.43f, 0.53f, 0.60f, 0.35f), 1f);
            DrawLine(
                new Vector2(centre.x, centre.y - usableRadius),
                new Vector2(centre.x, centre.y + usableRadius),
                1f,
                new Color(0.43f, 0.53f, 0.60f, 0.25f));
            DrawLine(
                new Vector2(centre.x - usableRadius, centre.y),
                new Vector2(centre.x + usableRadius, centre.y),
                1f,
                new Color(0.43f, 0.53f, 0.60f, 0.25f));

            GUI.Label(new Rect(centre.x - 8f, 3f, 20f, 18f), "N", smallStyle);
            GUI.Label(new Rect(5f, 19f, size - 10f, 14f), "CYAN = YOU  ·  ARROWS = HEADING", smallStyle);
            GUI.Label(new Rect(8f, size - 19f, size - 16f, 16f),
                string.Format(CultureInfo.InvariantCulture, "ARENA  {0:0} m radius", arenaRadius), smallStyle);

            NetworkPlayerSnapshot localState = default(NetworkPlayerSnapshot);
            bool hasLocalPlayer = false;
            for (int i = 0; i < players.Length; i++)
            {
                NetworkPlayer player = players[i];
                if (player != null && player.IsOwner)
                {
                    localState = player.CurrentState;
                    hasLocalPlayer = localState.Initialized;
                    break;
                }
            }

            if (hasLocalPlayer)
            {
                DrawPlayerMarker(
                    centre,
                    localState.EastMeters,
                    localState.NorthMeters,
                    pixelsPerMeter,
                    new Color(0.26f, 0.95f, 0.98f, 1f),
                    localState.YawDegrees,
                    5.5f,
                    true);
            }

            for (int i = 0; i < players.Length; i++)
            {
                NetworkPlayer player = players[i];
                if (player == null || player.IsOwner)
                {
                    continue;
                }
                NetworkPlayerSnapshot state = player.CurrentState;
                if (!state.Initialized)
                {
                    continue;
                }

                Color playerColor = Color.HSVToRGB(
                    (float)(((state.PlayerId + 1UL) * 0.137f) % 1.0f),
                    0.78f,
                    1f);
                DrawPlayerMarker(
                    centre,
                    state.EastMeters,
                    state.NorthMeters,
                    pixelsPerMeter,
                    playerColor,
                    state.YawDegrees,
                    4.2f,
                    false);
            }
            GUI.EndGroup();
        }

        private void RefreshPlayerCache()
        {
            if (Time.frameCount < nextPlayerRefreshFrame)
            {
                return;
            }
            nextPlayerRefreshFrame = Time.frameCount + PlayerRefreshFrameInterval;
            players = FindObjectsOfType<NetworkPlayer>();
        }

        private static void DrawPlayerMarker(
            Vector2 mapCentre,
            double eastMeters,
            double northMeters,
            float pixelsPerMeter,
            Color color,
            float yawDegrees,
            float markerRadius,
            bool localPlayer)
        {
            Vector2 point = new Vector2(
                mapCentre.x + (float)eastMeters * pixelsPerMeter,
                mapCentre.y - (float)northMeters * pixelsPerMeter);
            GUI.color = color;
            GUI.DrawTexture(
                new Rect(point.x - markerRadius, point.y - markerRadius, markerRadius * 2f, markerRadius * 2f),
                Texture2D.whiteTexture);
            GUI.color = Color.white;

            float yaw = yawDegrees * Mathf.Deg2Rad;
            Vector2 heading = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw));
            float headingLength = localPlayer ? 16f : 9f;
            DrawLine(point, point + heading * headingLength, localPlayer ? 2.5f : 1.5f, color);
        }

        private static void DrawCircle(Vector2 centre, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
            {
                return;
            }
            Vector2 previous = centre + new Vector2(radius, 0f);
            for (int i = 1; i <= CircleSegmentCount; i++)
            {
                float angle = (i / (float)CircleSegmentCount) * Mathf.PI * 2f;
                Vector2 next = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                DrawLine(previous, next, thickness, color);
                previous = next;
            }
        }

        private static void DrawLine(Vector2 start, Vector2 end, float thickness, Color color)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.01f)
            {
                return;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.color = color;
            GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = Color.white;
        }

        private void EnsureStyles()
        {
            if (debugStyle == null)
            {
                debugStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true
                };
                debugStyle.normal.textColor = Color.white;
            }
            if (smallStyle == null)
            {
                smallStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false
                };
                smallStyle.normal.textColor = new Color(0.91f, 0.95f, 0.98f, 0.96f);
            }
        }
    }
}
