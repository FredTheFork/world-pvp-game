using CesiumForUnity;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPvp.Phase1.Geospatial
{
    /// <summary>
    /// Draws three anchored reference rings: gameplay radius, visibility extent, and outer preload
    /// envelope. These rings communicate scale only; they do not enforce arena membership.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaBoundaryVisualizer : MonoBehaviour
    {
        private const int SegmentCount = 160;

        [SerializeField] private GeospatialWorldManager worldManager;

        private GameObject anchoredRoot;
        private CesiumGlobeAnchor globeAnchor;
        private LineRenderer gameplayRing;
        private LineRenderer visibilityRing;
        private LineRenderer preloadRing;
        private Material sharedLineMaterial;
        private double cachedGameplayRadius = double.NaN;
        private double cachedVisibilityRadius = double.NaN;
        private double cachedRenderRadius = double.NaN;
        private GeoPosition cachedOrigin;
        private double cachedGroundAltitude = double.NaN;

        public void Configure(GeospatialWorldManager manager)
        {
            worldManager = manager;
            Refresh();
        }

        public void Refresh()
        {
            bool shouldShow = worldManager != null &&
                              worldManager.IsArenaConfigured &&
                              worldManager.HasArenaGroundPosition &&
                              worldManager.Georeference != null;
            if (!shouldShow)
            {
                if (anchoredRoot != null)
                {
                    anchoredRoot.SetActive(false);
                }
                return;
            }

            EnsureRootAndRings();
            if (anchoredRoot == null)
            {
                return;
            }

            GeoPosition origin = worldManager.WorldOrigin;
            double gameplayRadius = worldManager.GameplayRadiusMeters;
            double visibilityRadius = worldManager.VisibilityRadiusMeters;
            double renderRadius = worldManager.RenderRadiusMeters;
            double groundAltitude = worldManager.ArenaGroundAltitudeMeters;

            bool geometryChanged =
                !origin.Equals(cachedOrigin) ||
                !gameplayRadius.Equals(cachedGameplayRadius) ||
                !visibilityRadius.Equals(cachedVisibilityRadius) ||
                !renderRadius.Equals(cachedRenderRadius) ||
                !groundAltitude.Equals(cachedGroundAltitude);

            if (geometryChanged)
            {
                globeAnchor.detectTransformChanges = true;
                globeAnchor.longitudeLatitudeHeight = new Unity.Mathematics.double3(
                    origin.LongitudeDegrees,
                    origin.LatitudeDegrees,
                    groundAltitude + 0.05);
                globeAnchor.Sync();

                SetRing(gameplayRing, gameplayRadius, 0.12f);
                SetRing(visibilityRing, visibilityRadius, 0.08f);
                SetRing(preloadRing, renderRadius, 0.04f);

                cachedOrigin = origin;
                cachedGameplayRadius = gameplayRadius;
                cachedVisibilityRadius = visibilityRadius;
                cachedRenderRadius = renderRadius;
                cachedGroundAltitude = groundAltitude;
            }

            anchoredRoot.SetActive(true);
        }

        private void EnsureRootAndRings()
        {
            if (anchoredRoot != null)
            {
                return;
            }

            Transform parent = worldManager.Georeference.transform;
            anchoredRoot = new GameObject("ArenaBoundaryRings");
            anchoredRoot.transform.SetParent(parent, false);
            globeAnchor = anchoredRoot.AddComponent<CesiumGlobeAnchor>();
            globeAnchor.detectTransformChanges = true;
            globeAnchor.adjustOrientationForGlobeWhenMoving = true;

            sharedLineMaterial = CreateLineMaterial();
            gameplayRing = CreateRing("Gameplay radius", new Color(0.12f, 0.92f, 1f, 1f), 1.1f, 30);
            visibilityRing = CreateRing("Visibility buffer", new Color(0.45f, 0.82f, 0.92f, 1f), 0.65f, 20);
            preloadRing = CreateRing("Preload envelope", new Color(0.42f, 0.58f, 0.78f, 1f), 0.45f, 10);
        }

        private LineRenderer CreateRing(string objectName, Color color, float widthMeters, int sortingOrder)
        {
            GameObject ringObject = new GameObject(objectName);
            ringObject.transform.SetParent(anchoredRoot.transform, false);
            LineRenderer renderer = ringObject.AddComponent<LineRenderer>();
            renderer.useWorldSpace = false;
            renderer.loop = true;
            renderer.positionCount = SegmentCount;
            renderer.widthMultiplier = widthMeters;
            renderer.startColor = color;
            renderer.endColor = color;
            renderer.numCornerVertices = 2;
            renderer.numCapVertices = 0;
            renderer.alignment = LineAlignment.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = sortingOrder;
            if (sharedLineMaterial != null)
            {
                renderer.sharedMaterial = sharedLineMaterial;
            }
            return renderer;
        }

        private static Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader);
            material.name = "Runtime Arena Boundary Unlit";
            material.hideFlags = HideFlags.HideAndDontSave;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }
            return material;
        }

        private static void SetRing(LineRenderer renderer, double radiusMeters, float upOffsetMeters)
        {
            if (renderer == null)
            {
                return;
            }

            float radius = (float)radiusMeters;
            for (int i = 0; i < SegmentCount; i++)
            {
                float angle = (Mathf.PI * 2f * i) / SegmentCount;
                renderer.SetPosition(
                    i,
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        upOffsetMeters,
                        Mathf.Sin(angle) * radius));
            }
        }

        private void OnDestroy()
        {
            if (sharedLineMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(sharedLineMaterial);
                }
                else
                {
                    DestroyImmediate(sharedLineMaterial);
                }
            }

            if (anchoredRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(anchoredRoot);
                }
                else
                {
                    DestroyImmediate(anchoredRoot);
                }
            }
        }
    }
}
