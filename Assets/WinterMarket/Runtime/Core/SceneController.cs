using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WinterMarket
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class SceneController : MonoBehaviour
    {
        private const float Grid = VoxelVolume.GridSize;
        // The player may pan 68 m in each direction and zoom out to 42 m.  This leaves
        // enough continuous snow around the 144 x 144 m build area for every camera view.
        private const float SnowfieldHalfExtent = 300f;

        private Material[] palette;
        private Material snowParticleMaterial;
        private Material snowCapMaterial;
        private DioramaPlacementGrid placementGrid;
        private Transform worldRoot;
        private readonly List<Light> warmLights = new List<Light>(6);
        private readonly List<Renderer> warmRenderers = new List<Renderer>(12);
        private bool initialized;

        public TimeOfDayController TimeOfDay { get; private set; }
        public LightingController Lighting { get; private set; }
        public QualityController Quality { get; private set; }
        public PathSystem Paths { get; private set; }
        public AmbientController Ambient { get; private set; }
        public Camera DioramaCamera { get; private set; }
        public Material[] Palette => palette;
        public Material SnowCapMaterial => snowCapMaterial;
        public Transform WorldRoot => worldRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureSceneController()
        {
            if (FindFirstObjectByType<SceneController>() != null)
            {
                return;
            }

            var root = new GameObject("Wintermarkt");
            root.AddComponent<SceneController>();
        }

        private void Awake()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            name = "Wintermarkt Diorama";
            Build();
        }

        public void Build()
        {
            TearDownBuiltWorld();
            palette = WinterMarketMaterialLibrary.LoadPalette();
            snowParticleMaterial = WinterMarketMaterialLibrary.LoadSnowParticles();
            snowCapMaterial = WinterMarketMaterialLibrary.CreateSnowCap();

            worldRoot = new GameObject("Procedural Voxel World").transform;
            worldRoot.SetParent(transform, false);
            placementGrid = new DioramaPlacementGrid(Grid);

            BuildGround();
            BuildSnowMounds();

            BuildCamera();
            BuildSystems();
        }

        private void BuildGround()
        {
            var ground = new VoxelVolume(Vector3Int.one);
            ground.SetVoxel(0, 0, 0, VoxelMaterialSlot.Slot0);
            VoxelAssetGenerator groundAsset =
                VoxelAssetGenerator.Create("Endlose Schneefläche", ground, palette, worldRoot, true);
            float snowfieldScale = SnowfieldHalfExtent * 2f / Grid;
            groundAsset.transform.position = new Vector3(-SnowfieldHalfExtent, 0f, -SnowfieldHalfExtent);
            groundAsset.transform.localScale = new Vector3(snowfieldScale, 2f, snowfieldScale);
            var groundRenderer = groundAsset.GetComponent<MeshRenderer>();
            groundRenderer.shadowCastingMode = ShadowCastingMode.Off;
            groundRenderer.receiveShadows = true;

            // The public main road is three construction cells wide. It is intentionally
            // axis-aligned and uses the same cleared-snow material as roads the player draws.
            const int pathWidth = 12;
            Vector3 pathCenter = new Vector3(0f, 0.305f, -2.4f);
            placementGrid.ReserveWalkway("Hauptweg", pathCenter, 180, pathWidth);
            var path = new VoxelVolume(new Vector3Int(180, 1, pathWidth));
            path.FillCuboid(0, 0, 0, 180, 1, pathWidth, VoxelMaterialSlot.Slot8);
            VoxelAssetGenerator pathAsset = CreateAsset(
                "Freigeräumter Hauptweg",
                path,
                pathCenter,
                true);

            ReserveClearedAccess("Geräumter Zugang Bäckerei", new Vector3(-4.2f, 0.305f, -1.2f), 12, 6);
            ReserveClearedAccess("Geräumter Zugang Getränke", new Vector3(4.2f, 0.305f, -1.2f), 12, 6);
        }

        private void ReserveClearedAccess(string objectName, Vector3 position, int width, int depth)
        {
            placementGrid.ReserveWalkway(objectName, position, width, depth);
        }

        private void BuildSnowMounds()
        {
            CreateMound("Schneehügel Nordwest", new Vector3(-6f, 0.31f, 3.6f), 30, 6, 13);
            CreateMound("Schneehügel Nordost", new Vector3(6f, 0.31f, 6f), 25, 5, 12);
            CreateMound("Schneekante vorne", new Vector3(0f, 0.31f, -4.2f), 42, 4, 7);
        }

        private void CreateMound(string objectName, Vector3 position, int width, int height, int depth)
        {
            var mound = new VoxelVolume(new Vector3Int(width, height, depth));
            placementGrid.ReserveObject(objectName, position, mound.Width, mound.Depth);
            for (int y = 0; y < height; y++)
            {
                int inset = y * 2;
                int layerWidth = Mathf.Max(1, width - inset * 2);
                int layerDepth = Mathf.Max(1, depth - inset);
                mound.FillCuboid(inset, y, y / 2, layerWidth, 1, layerDepth, y == 0
                    ? VoxelMaterialSlot.Slot1
                    : VoxelMaterialSlot.Slot0);
            }

            CreateAsset(objectName, mound, position, true);
        }

        private void BuildCamera()
        {
            var cameraObject = new GameObject("Diorama Camera");
            cameraObject.transform.SetParent(transform, false);
            Vector3 focusPoint = new Vector3(0f, 0.8f, -0.6f);
            cameraObject.transform.position = new Vector3(18f, 22f, -23f);
            cameraObject.transform.LookAt(focusPoint);

            DioramaCamera = cameraObject.AddComponent<Camera>();
            DioramaCamera.orthographic = true;
            DioramaCamera.orthographicSize = 22f;
            DioramaCamera.clearFlags = CameraClearFlags.SolidColor;
            DioramaCamera.backgroundColor = WinterPalette.Background;
            DioramaCamera.nearClipPlane = 0.1f;
            DioramaCamera.farClipPlane = 500f;
            DioramaCamera.allowHDR = true;
            DioramaCamera.allowMSAA = true;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<AdaptiveOrthoCamera>().Configure(22f);
        }

        private void BuildSystems()
        {
            TimeOfDay = gameObject.AddComponent<TimeOfDayController>();
            Quality = gameObject.AddComponent<QualityController>();
            Paths = gameObject.AddComponent<PathSystem>();
            ConfigurePaths();

            Light sunlight = CreateDirectionalLight();
            Lighting = gameObject.AddComponent<LightingController>();
            Lighting.Configure(TimeOfDay, sunlight, warmLights.ToArray(), warmRenderers.ToArray(), DioramaCamera);

            Ambient = gameObject.AddComponent<AmbientController>();
            Ambient.BuildRuntimeAtmosphere(snowParticleMaterial, DioramaCamera);
            Ambient.ApplyQuality(Quality.CurrentQuality == QualityLevel.High);
            ApplyLightingQuality(Quality.CurrentQuality);
            Quality.QualityChanged += level =>
            {
                Ambient.ApplyQuality(level == QualityLevel.High);
                ApplyLightingQuality(level);
            };

            var npcRoot = new GameObject("NPC Simulation");
            npcRoot.transform.SetParent(transform, false);
            var factory = npcRoot.AddComponent<NPCFactory>();
            factory.ConfigureMaterials(palette);
            var director = npcRoot.AddComponent<NPCDirector>();
            if (!LocalSettings.HasDeterministicSeed)
            {
                LocalSettings.SetDeterministicSeed(120725);
            }
            director.SetDeterministicSeed(LocalSettings.DeterministicSeed);
            director.SetRuntimeReferences(factory, Paths, TimeOfDay);
            var footsteps = npcRoot.AddComponent<NPCFootstepAudioLink>();
            footsteps.Configure(director, Ambient);

            // Market objects are data-driven from V0.3 onward. The controller keeps the
            // terrain, atmosphere and camera from V0.2, while this module restores and owns
            // movable stands, decoration, paths, dynamic lights and navigation metadata.
            var buildMode = gameObject.AddComponent<BuildModeController>();
            buildMode.Configure(this, Paths, Lighting);

            if (DioramaUIController.IsDebugUiRequested)
            {
                var ui = gameObject.AddComponent<DioramaUIController>();
                ui.Configure(Quality);
            }
            if (Debug.isDebugBuild || FrameDiagnostics.IsPerformanceCaptureRequested)
            {
                gameObject.AddComponent<FrameDiagnostics>();
            }
        }

        private void ApplyLightingQuality(QualityLevel level)
        {
            bool high = level == QualityLevel.High;
            if (DioramaCamera != null)
            {
                DioramaCamera.allowHDR = high;
                DioramaCamera.allowMSAA = high;
            }

            if (Lighting != null && Lighting.DirectionalLight != null)
            {
                Lighting.DirectionalLight.shadows = high ? LightShadows.Soft : LightShadows.Hard;
            }
            for (int i = 0; i < warmLights.Count; i++)
            {
                warmLights[i].enabled = high || i < 4;
            }
        }

        private void ConfigurePaths()
        {
            var points = new[]
            {
                new PathSystem.NamedWaypoint("west-entry", new Vector3(-10.4f, 0.55f, -2.7f)),
                new PathSystem.NamedWaypoint("west-path", new Vector3(-6.75f, 0.55f, -2.65f)),
                new PathSystem.NamedWaypoint("west-foreground", new Vector3(-4.15f, 0.55f, -3.35f)),
                new PathSystem.NamedWaypoint("tree-west", new Vector3(-1.7f, 0.55f, -2.15f)),
                new PathSystem.NamedWaypoint("tree-east", new Vector3(1.7f, 0.55f, -2.15f)),
                new PathSystem.NamedWaypoint("bakery", new Vector3(-4.35f, 0.55f, -1.2f)),
                new PathSystem.NamedWaypoint("bakery-queue", new Vector3(-3.75f, 0.55f, -1.8f)),
                new PathSystem.NamedWaypoint("drinks", new Vector3(4.35f, 0.55f, -1.4f)),
                new PathSystem.NamedWaypoint("drinks-queue", new Vector3(3.75f, 0.55f, -1.85f)),
                new PathSystem.NamedWaypoint("delivery", new Vector3(9.9f, 0.55f, 2.1f)),
                new PathSystem.NamedWaypoint("delivery-south", new Vector3(9.9f, 0.55f, 0.8f)),
                new PathSystem.NamedWaypoint("drinks-side", new Vector3(6.95f, 0.55f, -1.5f)),
                new PathSystem.NamedWaypoint("cleaning", new Vector3(0.8f, 0.55f, -1.35f)),
                new PathSystem.NamedWaypoint("east-foreground", new Vector3(4.15f, 0.55f, -3.35f)),
                new PathSystem.NamedWaypoint("east-path", new Vector3(6.75f, 0.55f, -2.65f)),
                new PathSystem.NamedWaypoint("east-entry", new Vector3(10.4f, 0.55f, -2.7f))
            };
            var routes = new[]
            {
                new PathSystem.NamedRoute(
                    "WalkThrough",
                    new[] { "west-entry", "west-path", "west-foreground", "tree-west", "tree-east", "east-path", "east-entry" }),
                new PathSystem.NamedRoute(
                    "WalkThroughReverse",
                    new[] { "east-entry", "east-path", "east-foreground", "tree-east", "tree-west", "west-path", "west-entry" }),
                new PathSystem.NamedRoute(
                    "MarketStalls",
                    new[] { "west-entry", "west-path", "tree-west", "bakery-queue", "bakery" }),
                new PathSystem.NamedRoute(
                    "MarketStallsReverse",
                    new[] { "east-entry", "east-path", "tree-east", "drinks-queue", "drinks" }),
                new PathSystem.NamedRoute(
                    "Delivery",
                    new[] { "east-entry", "delivery", "delivery-south", "drinks-side", "drinks-queue", "drinks" }),
                new PathSystem.NamedRoute(
                    "Cleaning",
                    new[] { "east-entry", "east-path", "cleaning", "bakery" })
            };
            // Editable objects contribute their current collision footprints after layout
            // restoration. Static routes remain here; their geometry is no longer hard-coded.
            Paths.Configure(points, routes, Array.Empty<PathSystem.NavigationObstacle>());
        }

        private VoxelAssetGenerator CreateAsset(
            string objectName,
            VoxelVolume volume,
            Vector3 center,
            bool isStatic)
        {
            VoxelAssetGenerator asset =
                VoxelAssetGenerator.Create(objectName, volume, palette, worldRoot, isStatic);
            Vector3 dimensions = new Vector3(volume.Width, volume.Height, volume.Depth) * Grid;
            asset.transform.position = center - new Vector3(dimensions.x * 0.5f, 0f, dimensions.z * 0.5f);
            var renderer = asset.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return asset;
        }

        private void CreateSnowCap(string objectName, VoxelVolume volume, Vector3 center)
        {
            VoxelAssetGenerator asset = VoxelAssetGenerator.Create(
                objectName,
                volume,
                new[] { snowCapMaterial },
                worldRoot,
                true);
            Vector3 dimensions = new Vector3(volume.Width, volume.Height, volume.Depth) * Grid;
            asset.transform.position = center - new Vector3(dimensions.x * 0.5f, 0f, dimensions.z * 0.5f);
            MeshRenderer renderer = asset.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private Light CreateDirectionalLight()
        {
            var lightObject = new GameObject("Winter Sun");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.9f, 0.95f, 1f);
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.48f;
            light.shadowBias = 0.035f;
            light.shadowNormalBias = 0.22f;
            return light;
        }

        private Light CreateWarmLight(string objectName, Vector3 position, float range, float intensity)
        {
            var lightObject = new GameObject(objectName);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = WinterPalette.Lantern;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
            return light;
        }

        private void TearDownBuiltWorld()
        {
            warmLights.Clear();
            warmRenderers.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                WinterMarketMaterialLibrary.DestroyOwned(transform.GetChild(i).gameObject);
            }

            DestroyOwnedComponent<LightingController>();
            DestroyOwnedComponent<TimeOfDayController>();
            DestroyOwnedComponent<QualityController>();
            DestroyOwnedComponent<PathSystem>();
            DestroyOwnedComponent<AmbientController>();
            DestroyOwnedComponent<DioramaUIController>();
            DestroyOwnedComponent<BuildModeController>();
            DestroyOwnedComponent<FrameDiagnostics>();

            WinterMarketMaterialLibrary.DestroyOwned(palette);
            WinterMarketMaterialLibrary.DestroyOwned(snowParticleMaterial);
            WinterMarketMaterialLibrary.DestroyOwned(snowCapMaterial);
            palette = null;
            snowParticleMaterial = null;
            snowCapMaterial = null;
            worldRoot = null;
            placementGrid = null;
            TimeOfDay = null;
            Lighting = null;
            Quality = null;
            Paths = null;
            Ambient = null;
            DioramaCamera = null;
        }

        private void DestroyOwnedComponent<T>() where T : Component
        {
            T component = GetComponent<T>();
            if (component != null)
            {
                WinterMarketMaterialLibrary.DestroyOwned(component);
            }
        }

        private void OnDestroy()
        {
            WinterMarketMaterialLibrary.DestroyOwned(palette);
            WinterMarketMaterialLibrary.DestroyOwned(snowParticleMaterial);
            WinterMarketMaterialLibrary.DestroyOwned(snowCapMaterial);
        }
    }

    /// <summary>
    /// Ground-plane reservation grid used while the procedural scene is assembled. It keeps
    /// pedestrian surfaces clear and turns accidental prop intersections into build errors.
    /// </summary>
    internal sealed class DioramaPlacementGrid
    {
        private readonly float cellSize;
        private readonly HashSet<Vector2Int> walkwayCells = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, string> objectCells = new Dictionary<Vector2Int, string>();

        public DioramaPlacementGrid(float gridCellSize)
        {
            cellSize = Mathf.Max(0.01f, gridCellSize);
        }

        public void ReserveWalkway(string objectName, Vector3 center, int widthCells, int depthCells)
        {
            foreach (Vector2Int cell in EnumerateFootprint(center, widthCells, depthCells))
            {
                if (objectCells.TryGetValue(cell, out string blockingObject))
                {
                    throw new InvalidOperationException(
                        $"Walkway '{objectName}' overlaps object '{blockingObject}' at {cell}.");
                }

                walkwayCells.Add(cell);
            }
        }

        public void ReserveObject(
            string objectName,
            Vector3 center,
            int widthCells,
            int depthCells,
            bool mayStandOnWalkway = false)
        {
            if (!TryReserveObject(objectName, center, widthCells, depthCells, mayStandOnWalkway))
            {
                throw new InvalidOperationException(
                    $"Object '{objectName}' overlaps an occupied grid footprint or cleared walkway.");
            }
        }

        public bool TryReserveObject(
            string objectName,
            Vector3 center,
            int widthCells,
            int depthCells,
            bool mayStandOnWalkway = false)
        {
            foreach (Vector2Int cell in EnumerateFootprint(center, widthCells, depthCells))
            {
                if (objectCells.ContainsKey(cell) || (!mayStandOnWalkway && walkwayCells.Contains(cell)))
                {
                    return false;
                }
            }

            foreach (Vector2Int cell in EnumerateFootprint(center, widthCells, depthCells))
            {
                objectCells.Add(cell, objectName);
            }

            return true;
        }

        private IEnumerable<Vector2Int> EnumerateFootprint(Vector3 center, int widthCells, int depthCells)
        {
            float halfWidth = Mathf.Max(1, widthCells) * cellSize * 0.5f;
            float halfDepth = Mathf.Max(1, depthCells) * cellSize * 0.5f;
            int minX = Mathf.FloorToInt((center.x - halfWidth) / cellSize);
            int maxX = Mathf.CeilToInt((center.x + halfWidth) / cellSize) - 1;
            int minZ = Mathf.FloorToInt((center.z - halfDepth) / cellSize);
            int maxZ = Mathf.CeilToInt((center.z + halfDepth) / cellSize) - 1;
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    yield return new Vector2Int(x, z);
                }
            }
        }
    }

}
