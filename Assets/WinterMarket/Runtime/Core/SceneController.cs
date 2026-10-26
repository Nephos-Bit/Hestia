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

        private Material[] palette;
        private Material snowParticleMaterial;
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

            worldRoot = new GameObject("Procedural Voxel World").transform;
            worldRoot.SetParent(transform, false);

            BuildGround();
            BuildStall("Bäckerei", new Vector3(-2.85f, 0f, 0.85f), VoxelMaterialSlot.Slot4, true);
            BuildStall("Heisse Getränke", new Vector3(2.85f, 0f, 0.35f), VoxelMaterialSlot.Slot9, false);
            BuildTree(new Vector3(0.05f, 0f, -0.55f));
            BuildLamp("Laterne West", new Vector3(-4.35f, 0f, -2.15f));
            BuildLamp("Laterne Ost", new Vector3(4.35f, 0f, -2.15f));
            BuildBench(new Vector3(-3.75f, 0f, -1.35f));
            BuildCrates(new Vector3(3.65f, 0f, 2.45f));
            BuildCart(new Vector3(4.4f, 0f, 2.2f));
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
            groundAsset.transform.position = new Vector3(-40f, 0f, -40f);
            groundAsset.transform.localScale = new Vector3(534f, 2f, 534f);
            var groundRenderer = groundAsset.GetComponent<MeshRenderer>();
            groundRenderer.shadowCastingMode = ShadowCastingMode.Off;
            groundRenderer.receiveShadows = true;

            var path = new VoxelVolume(new Vector3Int(175, 1, 17));
            path.FillCuboid(0, 0, 0, 175, 1, 17, VoxelMaterialSlot.Slot8);
            VoxelAssetGenerator pathAsset = CreateAsset(
                "Verschneiter Fussweg",
                path,
                new Vector3(0f, 0.305f, -2.5f),
                true);
            pathAsset.transform.rotation = Quaternion.Euler(0f, -3f, 0f);

            var pathDusting = new VoxelVolume(new Vector3Int(175, 1, 17));
            for (int x = 0; x < 175; x += 11)
            {
                int width = 3 + (x % 5);
                pathDusting.FillCuboid(x, 0, x % 12, width, 1, 2, VoxelMaterialSlot.Slot1);
            }

            VoxelAssetGenerator dusting = CreateAsset(
                "Schneespuren auf dem Weg",
                pathDusting,
                new Vector3(0f, 0.46f, -2.5f),
                true);
            dusting.transform.rotation = pathAsset.transform.rotation;
        }

        private void BuildStall(string objectName, Vector3 position, VoxelMaterialSlot roof, bool bakery)
        {
            var volume = new VoxelVolume(new Vector3Int(25, 24, 17));
            VoxelMaterialSlot wood = bakery ? VoxelMaterialSlot.Slot2 : VoxelMaterialSlot.Slot3;

            volume.FillCuboid(1, 0, 1, 23, 2, 15, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(2, 2, 2, 2, 16, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(21, 2, 2, 2, 16, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(2, 2, 13, 2, 16, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(21, 2, 13, 2, 16, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(1, 2, 14, 23, 15, 2, wood);
            volume.FillCuboid(1, 2, 2, 2, 15, 12, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(22, 2, 2, 2, 15, 12, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(3, 8, 1, 19, 2, 3, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(3, 7, 1, 19, 1, 1, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(4, 3, 13, 17, 11, 1, VoxelMaterialSlot.Slot6);
            volume.FillCuboid(3, 3, 13, 1, 12, 1, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(21, 3, 13, 1, 12, 1, VoxelMaterialSlot.Slot2);

            for (int z = 0; z < 17; z++)
            {
                int distance = Mathf.Abs(z - 8);
                int roofY = 17 + Mathf.Max(0, 7 - distance) / 2;
                volume.FillCuboid(0, roofY, z, 25, 2, 1, roof);
                volume.FillCuboid(0, roofY + 2, z, 25, 1, 1, VoxelMaterialSlot.Slot0);
                if (z == 0 || z == 16)
                {
                    volume.FillCuboid(0, roofY - 1, z, 25, 1, 1, VoxelMaterialSlot.Slot3);
                }
            }

            volume.FillCuboid(0, 18, 0, 25, 1, 1, VoxelMaterialSlot.Slot0);
            volume.FillCuboid(0, 18, 16, 25, 1, 1, VoxelMaterialSlot.Slot0);

            if (bakery)
            {
                for (int x = 5; x <= 18; x += 4)
                {
                    volume.FillCuboid(x, 10, 2, 2, 1, 2, VoxelMaterialSlot.Slot12);
                    volume.FillCuboid(x, 11, 2, 2, 1, 2, VoxelMaterialSlot.Slot6);
                }
            }
            else
            {
                for (int x = 6; x <= 18; x += 4)
                {
                    volume.FillCuboid(x, 10, 2, 2, 2, 2, VoxelMaterialSlot.Slot14);
                    volume.FillCuboid(x, 12, 2, 2, 1, 2, VoxelMaterialSlot.Slot6);
                }
            }

            VoxelAssetGenerator asset = CreateAsset(objectName, volume, position, true);
            warmRenderers.Add(asset.GetComponent<Renderer>());
            Vector3 lightPosition = position + new Vector3(0f, 1.7f, -0.25f);
            warmLights.Add(CreateWarmLight(objectName + " Innenlicht", lightPosition, 3.6f, 1.85f));

            var sign = new VoxelVolume(new Vector3Int(11, 5, 2));
            sign.FillCuboid(0, 0, 1, 11, 5, 1, bakery ? VoxelMaterialSlot.Slot4 : VoxelMaterialSlot.Slot5);
            sign.FillCuboid(1, 1, 1, 9, 3, 1, VoxelMaterialSlot.Slot6);
            sign.FillCuboid(5, 0, 0, 1, 5, 1, VoxelMaterialSlot.Slot3);
            VoxelAssetGenerator signAsset = CreateAsset(
                objectName + " Schild",
                sign,
                position + new Vector3(0f, 2.55f, -1.35f),
                true);
            warmRenderers.Add(signAsset.GetComponent<Renderer>());
        }

        private void BuildTree(Vector3 position)
        {
            var tree = new VoxelVolume(new Vector3Int(15, 22, 15));
            tree.FillCuboid(6, 0, 6, 3, 7, 3, VoxelMaterialSlot.Slot3);

            for (int y = 5; y < 18; y++)
            {
                int radius = Mathf.Clamp(6 - (y - 5) / 2, 2, 6);
                if ((y - 5) % 2 == 0)
                {
                    radius = Mathf.Min(6, radius + 1);
                }

                int start = 7 - radius;
                int size = radius * 2 + 1;
                tree.FillCuboid(start, y, start, size, 1, size, VoxelMaterialSlot.Slot5);
            }

            int[,] lights =
            {
                { 1, 7, 7 }, { 12, 8, 8 }, { 4, 10, 1 }, { 10, 11, 12 },
                { 2, 13, 7 }, { 11, 14, 6 }, { 6, 16, 3 }, { 8, 17, 10 }
            };
            for (int i = 0; i < lights.GetLength(0); i++)
            {
                tree.SetVoxel(lights[i, 0], lights[i, 1], lights[i, 2], VoxelMaterialSlot.Slot6);
            }

            tree.FillCuboid(6, 18, 6, 3, 3, 3, VoxelMaterialSlot.Slot6);
            tree.FillCuboid(5, 19, 7, 5, 1, 1, VoxelMaterialSlot.Slot6);
            tree.FillCuboid(7, 17, 7, 1, 5, 1, VoxelMaterialSlot.Slot6);

            VoxelAssetGenerator asset = CreateAsset("Kleiner Weihnachtsbaum", tree, position, true);
            warmRenderers.Add(asset.GetComponent<Renderer>());
            warmLights.Add(CreateWarmLight("Baumlichter", position + new Vector3(0f, 1.75f, 0f), 3.5f, 1.25f));
        }

        private void BuildLamp(string objectName, Vector3 position)
        {
            var lamp = new VoxelVolume(new Vector3Int(5, 22, 5));
            lamp.FillCuboid(2, 0, 2, 1, 17, 1, VoxelMaterialSlot.Slot13);
            lamp.FillCuboid(1, 16, 1, 3, 5, 3, VoxelMaterialSlot.Slot13);
            lamp.FillCuboid(2, 17, 2, 1, 3, 1, VoxelMaterialSlot.Slot6);
            lamp.FillCuboid(0, 21, 0, 5, 1, 5, VoxelMaterialSlot.Slot0);
            VoxelAssetGenerator asset = CreateAsset(objectName, lamp, position, true);
            warmRenderers.Add(asset.GetComponent<Renderer>());
            warmLights.Add(CreateWarmLight(objectName + " Licht", position + new Vector3(0f, 2.75f, 0f), 3.2f, 1.15f));
        }

        private void BuildBench(Vector3 position)
        {
            var bench = new VoxelVolume(new Vector3Int(22, 9, 6));
            bench.FillCuboid(1, 4, 1, 20, 2, 4, VoxelMaterialSlot.Slot2);
            bench.FillCuboid(1, 7, 5, 20, 2, 1, VoxelMaterialSlot.Slot2);
            bench.FillCuboid(2, 0, 1, 2, 5, 2, VoxelMaterialSlot.Slot3);
            bench.FillCuboid(18, 0, 1, 2, 5, 2, VoxelMaterialSlot.Slot3);
            bench.FillCuboid(0, 6, 4, 22, 1, 2, VoxelMaterialSlot.Slot0);
            CreateAsset("Holzbank", bench, position, true);
        }

        private void BuildCrates(Vector3 position)
        {
            var crates = new VoxelVolume(new Vector3Int(18, 10, 12));
            FillCrate(crates, 0, 0, 0);
            FillCrate(crates, 8, 0, 2);
            FillCrate(crates, 4, 5, 1);
            CreateAsset("Warenkisten", crates, position, true);
        }

        private static void FillCrate(VoxelVolume volume, int x, int y, int z)
        {
            volume.FillCuboid(x, y, z, 7, 5, 7, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(x + 1, y + 1, z, 5, 3, 1, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(x, y + 2, z, 7, 1, 1, VoxelMaterialSlot.Slot3);
        }

        private void BuildCart(Vector3 position)
        {
            var cart = new VoxelVolume(new Vector3Int(24, 12, 12));
            cart.FillCuboid(2, 5, 1, 15, 5, 10, VoxelMaterialSlot.Slot2);
            cart.FillCuboid(3, 6, 2, 13, 3, 8, VoxelMaterialSlot.Slot3);
            cart.FillCuboid(16, 7, 5, 8, 1, 2, VoxelMaterialSlot.Slot3);
            cart.FillCuboid(4, 1, 1, 4, 4, 2, VoxelMaterialSlot.Slot13);
            cart.FillCuboid(12, 1, 1, 4, 4, 2, VoxelMaterialSlot.Slot13);
            cart.FillCuboid(4, 1, 9, 4, 4, 2, VoxelMaterialSlot.Slot13);
            cart.FillCuboid(12, 1, 9, 4, 4, 2, VoxelMaterialSlot.Slot13);
            cart.FillCuboid(1, 10, 0, 17, 1, 12, VoxelMaterialSlot.Slot0);
            CreateAsset("Liefer-Handkarren", cart, position, true);
        }

        private void BuildSnowMounds()
        {
            CreateMound("Schneehügel Nordwest", new Vector3(-6.3f, 0.31f, 3.5f), 30, 6, 13);
            CreateMound("Schneehügel Nordost", new Vector3(6.2f, 0.31f, 3.9f), 25, 5, 12);
            CreateMound("Schneekante vorne", new Vector3(0.2f, 0.31f, -4.35f), 42, 4, 7);
        }

        private void CreateMound(string objectName, Vector3 position, int width, int height, int depth)
        {
            var mound = new VoxelVolume(new Vector3Int(width, height, depth));
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
            cameraObject.transform.position = new Vector3(9.4f, 8.6f, -9.4f);
            cameraObject.transform.LookAt(new Vector3(0f, 1.05f, -0.1f));

            DioramaCamera = cameraObject.AddComponent<Camera>();
            DioramaCamera.orthographic = true;
            DioramaCamera.orthographicSize = 13.6f;
            DioramaCamera.clearFlags = CameraClearFlags.SolidColor;
            DioramaCamera.backgroundColor = WinterPalette.Background;
            DioramaCamera.nearClipPlane = 0.1f;
            DioramaCamera.farClipPlane = 80f;
            DioramaCamera.allowHDR = true;
            DioramaCamera.allowMSAA = true;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<AdaptiveOrthoCamera>().Configure(13.6f);
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
            Ambient.BuildRuntimeAtmosphere(snowParticleMaterial);
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

            var ui = gameObject.AddComponent<DioramaUIController>();
            ui.Configure(Quality);
            gameObject.AddComponent<FrameDiagnostics>();
        }

        private void ApplyLightingQuality(QualityLevel level)
        {
            bool high = level == QualityLevel.High;
            for (int i = 0; i < warmLights.Count; i++)
            {
                warmLights[i].enabled = high || i < 3;
            }
        }

        private void ConfigurePaths()
        {
            var points = new[]
            {
                new PathSystem.NamedWaypoint("west-entry", new Vector3(-9.5f, 0.55f, -2.6f)),
                new PathSystem.NamedWaypoint("west-path", new Vector3(-4.8f, 0.55f, -2.5f)),
                new PathSystem.NamedWaypoint("tree", new Vector3(0f, 0.55f, -1.6f)),
                new PathSystem.NamedWaypoint("bakery", new Vector3(-2.85f, 0.55f, -0.85f)),
                new PathSystem.NamedWaypoint("drinks", new Vector3(2.85f, 0.55f, -1.25f)),
                new PathSystem.NamedWaypoint("delivery", new Vector3(3.9f, 0.55f, 2.0f)),
                new PathSystem.NamedWaypoint("cleaning", new Vector3(0.8f, 0.55f, -0.8f)),
                new PathSystem.NamedWaypoint("east-path", new Vector3(4.8f, 0.55f, -2.6f)),
                new PathSystem.NamedWaypoint("east-entry", new Vector3(9.5f, 0.55f, -2.7f))
            };
            var routes = new[]
            {
                new PathSystem.NamedRoute(
                    "WalkThrough",
                    new[] { "west-entry", "west-path", "tree", "east-path", "east-entry" }),
                new PathSystem.NamedRoute(
                    "WalkThroughReverse",
                    new[] { "east-entry", "east-path", "tree", "west-path", "west-entry" }),
                new PathSystem.NamedRoute(
                    "MarketStalls",
                    new[] { "west-entry", "west-path", "tree", "bakery" }),
                new PathSystem.NamedRoute(
                    "MarketStallsReverse",
                    new[] { "east-entry", "east-path", "tree", "drinks" }),
                new PathSystem.NamedRoute(
                    "Delivery",
                    new[] { "east-entry", "delivery", "drinks" }),
                new PathSystem.NamedRoute(
                    "Cleaning",
                    new[] { "east-entry", "east-path", "cleaning", "bakery" })
            };
            Paths.Configure(points, routes);
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
            light.shadowStrength = 0.38f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.28f;
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
            DestroyOwnedComponent<FrameDiagnostics>();

            WinterMarketMaterialLibrary.DestroyOwned(palette);
            WinterMarketMaterialLibrary.DestroyOwned(snowParticleMaterial);
            palette = null;
            snowParticleMaterial = null;
            worldRoot = null;
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
        }
    }
}
