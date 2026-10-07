using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using WorldPvp.Phase1.Battles;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Streaming;

namespace WorldPvp.Phase1.UI
{
    /// <summary>
    /// Compact, in-game performance and map-usage panel. All request and cost fields are explicitly
    /// labelled estimates; it is not a billing report or a hard stop on Cesium's native HTTP work.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhaseSixDiagnosticsHud : MonoBehaviour
    {
        private const float PanelWidth = 760f;
        private const float PanelHeight = 270f;
        private const float Padding = 9f;

        [SerializeField] private WorldPerformanceTelemetry performanceTelemetry;
        [SerializeField] private MapUsageTracker mapUsageTracker;
        [SerializeField] private BattleSessionCoordinator coordinator;
        [SerializeField] private PhaseOneWorldSettings settings;

        private bool visible = true;
        private GUIStyle textStyle;
        private GUIStyle titleStyle;
        private readonly StringBuilder textBuilder = new StringBuilder(1200);

        public void Configure(
            WorldPerformanceTelemetry telemetry,
            MapUsageTracker usageTracker,
            BattleSessionCoordinator sessionCoordinator,
            PhaseOneWorldSettings worldSettings)
        {
            performanceTelemetry = telemetry;
            mapUsageTracker = usageTracker;
            coordinator = sessionCoordinator;
            settings = worldSettings;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame)
            {
                visible = !visible;
            }

            if (mapUsageTracker != null && coordinator != null && mapUsageTracker.HasActiveMatchScope)
            {
                mapUsageTracker.SetExpectedPlayerCount(Mathf.Max(1, coordinator.ConnectedNetworkPlayerCount));
            }
        }

        private void OnGUI()
        {
            if (!visible || mapUsageTracker == null || coordinator == null ||
                !mapUsageTracker.HasActiveMatchScope)
            {
                return;
            }

            EnsureStyles();
            float x = 12f;
            float y = Mathf.Max(8f, Screen.height - PanelHeight - 12f);
            Rect panel = new Rect(x, y, PanelWidth, PanelHeight);
            GUI.color = new Color(0.01f, 0.02f, 0.035f, 0.89f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + Padding, panel.y + 5f, panel.width - Padding * 2f, 19f),
                "STREAM / COST DIAGNOSTICS   ·   F2 TO HIDE", titleStyle);
            GUI.Label(new Rect(panel.x + Padding, panel.y + 26f, panel.width - Padding * 2f, panel.height - 30f),
                BuildDiagnosticsText(), textStyle);
        }

        private string BuildDiagnosticsText()
        {
            textBuilder.Length = 0;
            if (performanceTelemetry != null)
            {
                textBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "FPS {0:F1}/{1} target  ·  frame P95 {2:F1}/{3:F1} ms  ·  CPU {4}\n",
                    performanceTelemetry.AverageFramesPerSecond,
                    performanceTelemetry.TargetFramesPerSecond,
                    performanceTelemetry.P95FrameTimeMilliseconds,
                    performanceTelemetry.P95FrameTimeBudgetMilliseconds,
                    FormatMilliseconds(performanceTelemetry.CpuFrameTimeMilliseconds));
                textBuilder.Append("GPU frame time ");
                textBuilder.Append(performanceTelemetry.HasGpuFrameTiming
                    ? FormatMilliseconds(performanceTelemetry.GpuFrameTimeMilliseconds) + " ms"
                    : "unavailable in Unity WebGL");
                textBuilder.Append("  ·  GPU alloc ");
                textBuilder.Append(FormatMiB(performanceTelemetry.GraphicsDriverAllocatedMemoryBytes));
                textBuilder.Append(" MiB  ·  Unity alloc/reserved ");
                textBuilder.Append(FormatMiB(performanceTelemetry.UnityAllocatedMemoryBytes));
                textBuilder.Append("/");
                textBuilder.Append(FormatMiB(performanceTelemetry.UnityReservedMemoryBytes));
                textBuilder.Append(" MiB (reserved target ");
                textBuilder.Append(performanceTelemetry.UnityMemoryBudgetMiB.ToString("F0", CultureInfo.InvariantCulture));
                textBuilder.Append(" MiB)  ·  GPU target ");
                textBuilder.Append(performanceTelemetry.GpuMemoryBudgetMiB.ToString("F0", CultureInfo.InvariantCulture));
                textBuilder.AppendLine(" MiB (Web allocation not readable)");

                textBuilder.Append("Cesium cache target ");
                textBuilder.Append(FormatMiB(performanceTelemetry.ConfiguredCesiumCacheCapBytes));
                textBuilder.Append(" MiB SOFT (resident bytes not exposed)  ·  live tile objects ~");
                textBuilder.Append(performanceTelemetry.LiveTileObjectCount.ToString(CultureInfo.InvariantCulture));
                textBuilder.AppendLine();
                textBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Visible Cesium triangles ~{0:N0}/{1:N0}  ·  0–{2:0}m {3:N0}  /  {2:0}–{4:0}m {5:N0}  /  {4:0}–{6:0}m {7:N0}  /  horizon {8:N0}\n",
                    performanceTelemetry.EstimatedVisibleCesiumTriangles,
                    performanceTelemetry.VisibleTriangleBudget,
                    settings != null ? settings.VeryHighDetailDistanceMeters : 100f,
                    performanceTelemetry.VeryHighBandTriangles,
                    settings != null ? settings.HighDetailDistanceMeters : 300f,
                    performanceTelemetry.HighBandTriangles,
                    settings != null ? settings.MediumDetailDistanceMeters : 750f,
                    performanceTelemetry.MediumBandTriangles,
                    performanceTelemetry.HorizonBandTriangles);
            }
            else
            {
                textBuilder.AppendLine("Performance telemetry is not configured.");
            }

            textBuilder.Append("Map bandwidth ");
            if (mapUsageTracker.HasFreshBandwidthSample)
            {
                textBuilder.Append(mapUsageTracker.EstimatedBandwidthMbps.ToString("F2", CultureInfo.InvariantCulture));
                textBuilder.Append(" Mbit/s observed transfer estimate");
                if (performanceTelemetry != null)
                {
                    textBuilder.Append(" / ");
                    textBuilder.Append(performanceTelemetry.MapBandwidthBudgetMbps.ToString("F1", CultureInfo.InvariantCulture));
                    textBuilder.Append(" Mbit/s target");
                }
                if (mapUsageTracker.BandwidthSampleIsPartial)
                {
                    textBuilder.Append(" (partial sizes)");
                }
            }
            else
            {
                textBuilder.Append("unavailable / cross-origin sizes not exposed");
                if (performanceTelemetry != null)
                {
                    textBuilder.Append(" / ");
                    textBuilder.Append(performanceTelemetry.MapBandwidthBudgetMbps.ToString("F1", CultureInfo.InvariantCulture));
                    textBuilder.Append(" Mbit/s target");
                }
            }
            textBuilder.AppendLine();

            MapUsageTracker.UsageCounters local = mapUsageTracker.GetLocalCounters();
            MapUsageTracker.UsageCounters match = mapUsageTracker.GetMatchCounters();
            textBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "Map session {0:F0}s  ·  local root start attempts {1}, browser root starts {2}, renderer request starts {3}, tile objects created {4}\n",
                mapUsageTracker.SessionDurationSeconds,
                local.RootTilesetLoadAttempts,
                local.BrowserObservedRootRequestStarts,
                local.BrowserObservedRendererRequestStarts,
                local.CesiumTileGameObjectCreations);
            bool isHostAggregate = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            textBuilder.Append(isHostAggregate
                ? "Host aggregate renderer starts "
                : "Local renderer starts (host aggregate not replicated) ");
            textBuilder.Append(match.BrowserObservedRendererRequestStarts.ToString("N0", CultureInfo.InvariantCulture));
            textBuilder.Append("  ·  reporters ");
            textBuilder.Append(mapUsageTracker.ReportingPlayerCount.ToString(CultureInfo.InvariantCulture));
            textBuilder.Append("/");
            textBuilder.Append(Mathf.Max(1, coordinator.ConnectedNetworkPlayerCount).ToString(CultureInfo.InvariantCulture));
            textBuilder.Append(" connected  ·  gross first-band estimate ");
            textBuilder.Append(mapUsageTracker.HasMatchCostEstimate
                ? "$" + mapUsageTracker.MatchEstimatedGrossCostUsdAtFirstTier.ToString("F4", CultureInfo.InvariantCulture) +
                  " @ $" + mapUsageTracker.ConfiguredPriceUsdPerThousandEvents.ToString("F2", CultureInfo.InvariantCulture) + "/1k"
                : "unavailable");
            textBuilder.AppendLine();

            textBuilder.Append("Estimate only: browser Resource Timing starts / successful Cesium tile objects are not Google billable events. ");
            textBuilder.Append("Cost is before the shared ");
            textBuilder.Append(mapUsageTracker.ConfiguredMonthlyFreeEventThreshold.ToString("N0", CultureInfo.InvariantCulture));
            textBuilder.Append(" free events/month (remaining unknown) and volume tiers; Cloud billing is authoritative.");
            if (mapUsageTracker.IsMatchRequestWarningExceeded)
            {
                textBuilder.Append("  LOCAL WARNING ONLY — this does not stop Cesium requests.");
            }
            return textBuilder.ToString();
        }

        private void EnsureStyles()
        {
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true,
                    richText = false
                };
                textStyle.normal.textColor = new Color(0.90f, 0.94f, 0.98f, 1f);
            }
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = false
                };
                titleStyle.normal.textColor = new Color(0.32f, 0.91f, 0.98f, 1f);
            }
        }

        private static string FormatMilliseconds(double milliseconds)
        {
            return milliseconds >= 0.0
                ? milliseconds.ToString("F1", CultureInfo.InvariantCulture)
                : "n/a";
        }

        private static string FormatMiB(long bytes)
        {
            return bytes >= 0L
                ? (bytes / (1024.0 * 1024.0)).ToString("F0", CultureInfo.InvariantCulture)
                : "n/a";
        }
    }
}
