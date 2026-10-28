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

        private void BuildStall(string objectName, Vector3 position, VoxelMaterialSlot roof, bool bakery)
        {
            // The snow plane ends at y=0.30. Lift the complete booth above it so the wooden
            // floor never shares a depth plane with snow in the open service area.
            Vector3 raisedPosition = position + Vector3.up * 0.315f;
            var volume = new VoxelVolume(new Vector3Int(29, 28, 19));
            placementGrid.ReserveObject(objectName, position, volume.Width, volume.Depth);
            var snowCap = new VoxelVolume(new Vector3Int(29, 28, 19));
            var glow = new VoxelVolume(new Vector3Int(29, 28, 19));
            VoxelMaterialSlot wood = bakery ? VoxelMaterialSlot.Slot2 : VoxelMaterialSlot.Slot3;

            // The front faces the camera. A few large parts make each booth legible without
            // turning it into texture-like visual noise.
            volume.FillCuboid(1, 0, 1, 27, 2, 17, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(2, 2, 2, 2, 18, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(25, 2, 2, 2, 18, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(2, 2, 15, 2, 18, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(25, 2, 15, 2, 18, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(1, 2, 16, 27, 16, 2, wood);
            volume.FillCuboid(1, 2, 2, 2, 15, 14, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(26, 2, 2, 2, 15, 14, VoxelMaterialSlot.Slot3);

            // Counter, service opening, and a deep lintel provide a readable handmade frame.
            volume.FillCuboid(3, 8, 0, 23, 2, 4, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(3, 7, 1, 23, 1, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(3, 16, 1, 23, 2, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(4, 4, 14, 21, 10, 1, VoxelMaterialSlot.Slot14);
            volume.FillCuboid(3, 3, 14, 2, 14, 1, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(24, 3, 14, 2, 14, 1, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(3, 18, 1, 23, 1, 2, bakery ? VoxelMaterialSlot.Slot4 : VoxelMaterialSlot.Slot5);

            for (int x = 5; x <= 22; x += 5)
            {
                volume.FillCuboid(x, 18, 0, 2, 1, 2, (x / 5) % 2 == 0
                    ? VoxelMaterialSlot.Slot4
                    : VoxelMaterialSlot.Slot5);
            }

            for (int z = 0; z < 19; z++)
            {
                int distance = Mathf.Abs(z - 9);
                int roofY = 20 + Mathf.Max(0, 8 - distance) / 2;
                volume.FillCuboid(0, roofY, z, 29, 2, 1, roof);
                snowCap.FillCuboid(0, roofY + 2, z, 29, 1, 1, VoxelMaterialSlot.Slot0);
                if (z == 0 || z == 18)
                {
                    volume.FillCuboid(0, roofY - 1, z, 29, 1, 1, VoxelMaterialSlot.Slot3);
                }
            }

            if (bakery)
            {
                for (int x = 6; x <= 20; x += 5)
                {
                    volume.FillCuboid(x, 10, 2, 3, 1, 2, VoxelMaterialSlot.Slot12);
                    volume.FillCuboid(x + 1, 11, 2, 1, 1, 2, VoxelMaterialSlot.Slot14);
                }
            }
            else
            {
                for (int x = 6; x <= 20; x += 5)
                {
                    volume.FillCuboid(x, 10, 2, 2, 2, 2, VoxelMaterialSlot.Slot14);
                    volume.FillCuboid(x, 12, 2, 2, 1, 2, VoxelMaterialSlot.Slot4);
                }
            }

            // Separate glowing voxels keep the wooden structure from becoming uniformly emissive.
            glow.FillCuboid(5, 14, 13, 19, 1, 1, VoxelMaterialSlot.Slot6);
            glow.FillCuboid(7, 12, 13, 2, 1, 1, VoxelMaterialSlot.Slot6);
            glow.FillCuboid(19, 12, 13, 2, 1, 1, VoxelMaterialSlot.Slot6);

            VoxelAssetGenerator asset = CreateAsset(objectName, volume, raisedPosition, true);
            CreateSnowCap(objectName + " Dachschnee", snowCap, raisedPosition);
            VoxelAssetGenerator glowAsset = CreateAsset(objectName + " Fensterlicht", glow, raisedPosition, true);
            warmRenderers.Add(glowAsset.GetComponent<Renderer>());
            Vector3 lightPosition = raisedPosition + new Vector3(0f, 1.85f, -0.45f);
            warmLights.Add(CreateWarmLight(objectName + " Innenlicht", lightPosition, 3.8f, 1.55f));

            var sign = new VoxelVolume(new Vector3Int(13, 6, 2));
            sign.FillCuboid(0, 0, 1, 13, 6, 1, bakery ? VoxelMaterialSlot.Slot4 : VoxelMaterialSlot.Slot5);
            sign.FillCuboid(1, 1, 1, 11, 3, 1, VoxelMaterialSlot.Slot14);
            sign.FillCuboid(6, 0, 0, 1, 6, 1, VoxelMaterialSlot.Slot3);
            VoxelAssetGenerator signAsset = CreateAsset(
                objectName + " Schild",
                sign,
                raisedPosition + new Vector3(0f, 2.8f, -1.45f),
                true);
        }

        private void BuildTree(Vector3 position)
        {
            var tree = new VoxelVolume(new Vector3Int(19, 30, 19));
            placementGrid.ReserveObject("Weihnachtsbaum", position, tree.Width, tree.Depth);
            var snowCap = new VoxelVolume(new Vector3Int(19, 30, 19));
            var decorations = new VoxelVolume(new Vector3Int(19, 30, 19));
            var treeLights = new VoxelVolume(new Vector3Int(19, 30, 19));
            tree.FillCuboid(8, 0, 8, 3, 8, 3, VoxelMaterialSlot.Slot3);

            int[] radii = { 8, 8, 7, 7, 6, 6, 5, 5, 4, 4, 3, 3, 2, 1 };
            for (int layer = 0; layer < radii.Length; layer++)
            {
                int y = 6 + layer;
                int radius = radii[layer];
                int start = 9 - radius;
                int size = radius * 2 + 1;
                tree.FillCuboid(start, y, start, size, 1, size, VoxelMaterialSlot.Slot5);
                if (layer < radii.Length - 1 && radius > radii[layer + 1])
                {
                    // Snow only occupies the exposed rim above a step, never the green
                    // voxels of the next tier. This keeps the separate snow mesh z-fight free.
                    int innerStart = 9 - radii[layer + 1];
                    int innerSize = radii[layer + 1] * 2 + 1;
                    snowCap.FillCuboid(start, y + 1, start, size, 1, 1, VoxelMaterialSlot.Slot0);
                    snowCap.FillCuboid(start, y + 1, start + size - 1, size, 1, 1, VoxelMaterialSlot.Slot0);
                    snowCap.FillCuboid(start, y + 1, innerStart, 1, 1, innerSize, VoxelMaterialSlot.Slot0);
                    snowCap.FillCuboid(start + size - 1, y + 1, innerStart, 1, 1, innerSize, VoxelMaterialSlot.Slot0);
                }
            }

            int[,] lightPositions =
            {
                { 2, 7, 9 }, { 15, 8, 11 }, { 5, 10, 2 }, { 13, 11, 15 },
                { 3, 13, 9 }, { 14, 15, 7 }, { 7, 17, 4 }, { 11, 18, 13 }
            };
            for (int i = 0; i < lightPositions.GetLength(0); i++)
            {
                treeLights.SetVoxel(
                    lightPositions[i, 0],
                    lightPositions[i, 1],
                    lightPositions[i, 2],
                    VoxelMaterialSlot.Slot6);
                tree.SetVoxel(
                    lightPositions[i, 0],
                    lightPositions[i, 1],
                    lightPositions[i, 2],
                    VoxelMaterialSlot.Empty);
            }

            decorations.SetVoxel(9, 20, 9, VoxelMaterialSlot.Slot4);
            decorations.SetVoxel(8, 16, 13, VoxelMaterialSlot.Slot4);
            decorations.SetVoxel(12, 13, 4, VoxelMaterialSlot.Slot4);
            tree.SetVoxel(8, 16, 13, VoxelMaterialSlot.Empty);
            tree.SetVoxel(12, 13, 4, VoxelMaterialSlot.Empty);
            decorations.FillCuboid(8, 20, 9, 3, 3, 1, VoxelMaterialSlot.Slot6);
            decorations.FillCuboid(9, 19, 8, 1, 5, 3, VoxelMaterialSlot.Slot6);

            CreateAsset("Gestufter Weihnachtsbaum", tree, position, true);
            CreateSnowCap("Weihnachtsbaum Schneeschichten", snowCap, position);
            CreateAsset("Baumschmuck", decorations, position, true);
            VoxelAssetGenerator lightAsset = CreateAsset("Baumlichter", treeLights, position, true);
            warmRenderers.Add(lightAsset.GetComponent<Renderer>());
            warmLights.Add(CreateWarmLight("Baumlichter Licht", position + new Vector3(0f, 2.1f, 0f), 3.4f, 0.95f));
        }

        private void BuildLamp(string objectName, Vector3 position)
        {
            var lamp = new VoxelVolume(new Vector3Int(5, 22, 5));
            placementGrid.ReserveObject(objectName, position, lamp.Width, lamp.Depth, true);
            lamp.FillCuboid(2, 0, 2, 1, 17, 1, VoxelMaterialSlot.Slot13);
            lamp.FillCuboid(1, 16, 1, 3, 5, 3, VoxelMaterialSlot.Slot13);
            lamp.FillCuboid(2, 17, 2, 1, 3, 1, VoxelMaterialSlot.Slot6);
            lamp.FillCuboid(0, 21, 0, 5, 1, 5, VoxelMaterialSlot.Slot0);
            CreateAsset(objectName, lamp, position, true);
            var glow = new VoxelVolume(new Vector3Int(5, 22, 5));
            glow.FillCuboid(2, 17, 2, 1, 3, 1, VoxelMaterialSlot.Slot6);
            VoxelAssetGenerator glowAsset = CreateAsset(objectName + " Glut", glow, position, true);
            warmRenderers.Add(glowAsset.GetComponent<Renderer>());
            warmLights.Add(CreateWarmLight(objectName + " Licht", position + new Vector3(0f, 2.75f, 0f), 3.2f, 1.15f));
        }

        private void BuildBench(Vector3 position)
        {
            var bench = new VoxelVolume(new Vector3Int(22, 9, 6));
            placementGrid.ReserveObject("Holzbank", position, bench.Width, bench.Depth);
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
            placementGrid.ReserveObject("Warenkisten", position, crates.Width, crates.Depth);
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
            placementGrid.ReserveObject("Liefer-Handkarren", position, cart.Width, cart.Depth);
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
            CreateMound("Schneehügel Nordwest", new Vector3(-6f, 0.31f, 3.6f), 30, 6, 13);
            CreateMound("Schneehügel Nordost", new Vector3(6f, 0.31f, 6f), 25, 5, 12);
            CreateMound("Schneekante vorne", new Vector3(0f, 0.31f, -4.2f), 42, 4, 7);
        }

        private void BuildBoundaryFirs()
        {
            BuildEnclosingForest();
        }

        private void BuildEnclosingForest()
        {
            const int candidateCount = 260;
            const int targetTreeCount = 62;
            int placed = 0;
            uint state = 0x91e10da5u;

            for (int i = 0; i < candidateCount && placed < targetTreeCount; i++)
            {
                float x = Mathf.Lerp(-13f, 13f, NextForestRandom(ref state));
                float z = Mathf.Lerp(-8.5f, 9f, NextForestRandom(ref state));
                float normalizedX = x / 7.4f;
                float normalizedZ = (z - 0.4f) / 5.1f;
                if (normalizedX * normalizedX + normalizedZ * normalizedZ < 1f)
                {
                    continue;
                }

                Vector3 position = new Vector3(x, 0f, z);
                bool foreground = z < -3.25f;
                Transform tree = BuildBoundaryFir(
                    $"Waldgürtel {i + 1}",
                    position,
                    foreground,
                    i,
                    true);
                if (tree != null)
                {
                    placed++;
                }
            }
        }

        private static float NextForestRandom(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00ffffffu) * (1f / 16777216f);
        }

        private Transform BuildBoundaryFir(
            string objectName,
            Vector3 position,
            bool foreground,
            int variation = 0,
            bool skipIfBlocked = false,
            bool tallBoundaryTree = false)
        {
            int baseRadius = tallBoundaryTree ? 5 : foreground ? 4 : 5;
            int radius = tallBoundaryTree ? 5 : Mathf.Clamp(baseRadius + variation % 3 - 1, 3, 5);
            int height = tallBoundaryTree ? 31 + variation % 5 : foreground ? 12 + variation % 3 : 17 + variation % 4;
            int tiers = tallBoundaryTree ? 9 : foreground ? 3 : 4 + variation % 2;
            int width = radius * 2 + 1;
            var tree = new VoxelVolume(new Vector3Int(width, height, width));
            if (skipIfBlocked)
            {
                if (!placementGrid.TryReserveObject(objectName, position, tree.Width, tree.Depth))
                {
                    return null;
                }
            }
            else
            {
                placementGrid.ReserveObject(objectName, position, tree.Width, tree.Depth);
            }

            int center = radius;
            tree.FillCuboid(center - 1, 0, center - 1, 3, 6, 3, VoxelMaterialSlot.Slot3);
            for (int tier = 0; tier < tiers; tier++)
            {
                int tierRadius = Mathf.Max(1, radius - tier);
                int size = tierRadius * 2 + 1;
                int y = 4 + tier * 3;
                int offsetX = tier == 0 ? 0 : ((variation + tier * 5) % 3) - 1;
                int offsetZ = tier == 0 ? 0 : ((variation + tier * 7) % 3) - 1;
                VoxelMaterialSlot foliage = (variation + tier) % 3 == 0
                    ? VoxelMaterialSlot.Slot15
                    : VoxelMaterialSlot.Slot5;
                tree.FillCuboid(
                    center - tierRadius + offsetX,
                    y,
                    center - tierRadius + offsetZ,
                    size,
                    2,
                    size,
                    foliage);
                tree.FillCuboid(
                    center - tierRadius + offsetX,
                    y + 2,
                    center - tierRadius + offsetZ + 1,
                    size,
                    1,
                    Mathf.Max(1, size - 2),
                    VoxelMaterialSlot.Slot0);
            }

            return CreateAsset(objectName, tree, position, true).transform;
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
            DestroyOwnedComponent<DioramaBoundaryForest>();
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

    [DisallowMultipleComponent]
    internal sealed class DioramaBoundaryForest : MonoBehaviour
    {
        private Camera dioramaCamera;
        private Transform[] rearTrees = Array.Empty<Transform>();
        private Transform[] foregroundTrees = Array.Empty<Transform>();
        private int lastWidth;
        private int lastHeight;
        private float lastOrthographicSize;

        public void Configure(Camera camera, Transform[] rear, Transform[] foreground)
        {
            dioramaCamera = camera;
            rearTrees = rear ?? Array.Empty<Transform>();
            foregroundTrees = foreground ?? Array.Empty<Transform>();
            ApplyLayout();
        }

        private void LateUpdate()
        {
            if (dioramaCamera == null)
            {
                return;
            }

            if (lastWidth != Screen.width || lastHeight != Screen.height ||
                !Mathf.Approximately(lastOrthographicSize, dioramaCamera.orthographicSize))
            {
                ApplyLayout();
            }
        }

        public static Vector3[] CalculateRow(Camera camera, int count, bool rear)
        {
            int safeCount = Mathf.Max(1, count);
            var positions = new Vector3[safeCount];
            if (camera == null)
            {
                return positions;
            }

            float lowestGroundViewport = FindLowestGroundViewport(camera);
            Vector3 topCenter = GetGroundPoint(camera, 0.5f, 1f);
            Vector3 bottomCenter = GetGroundPoint(camera, 0.5f, lowestGroundViewport);
            Vector3 outward = rear ? topCenter - bottomCenter : bottomCenter - topCenter;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.forward;
            // Rear roots sit high in the view and use tall trees whose crowns continue to the
            // top edge. Front roots sit directly at the lower edge, leaving the path visible.
            float viewportY = rear ? 0.82f : lowestGroundViewport;
            float outsideMargin = rear ? 0.2f : 0f;

            for (int i = 0; i < safeCount; i++)
            {
                float interpolation = safeCount == 1 ? 0.5f : i / (float)(safeCount - 1);
                // The endpoints extend beyond the visible width, covering wide landscape
                // previews as well as narrow phone displays.
                float viewportX = Mathf.Lerp(-0.12f, 1.12f, interpolation);
                positions[i] = GetGroundPoint(camera, viewportX, viewportY) + outward * outsideMargin;
                positions[i].y = 0f;
            }

            return positions;
        }

        private void ApplyLayout()
        {
            ApplyRow(rearTrees, CalculateRow(dioramaCamera, rearTrees.Length, true));
            ApplyRow(foregroundTrees, CalculateRow(dioramaCamera, foregroundTrees.Length, false));
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            lastOrthographicSize = dioramaCamera != null ? dioramaCamera.orthographicSize : 0f;
        }

        private static void ApplyRow(Transform[] trees, Vector3[] positions)
        {
            int count = Mathf.Min(trees != null ? trees.Length : 0, positions != null ? positions.Length : 0);
            for (int i = 0; i < count; i++)
            {
                if (trees[i] != null)
                {
                    trees[i].position = positions[i];
                }
            }
        }

        private static Vector3 GetGroundPoint(Camera camera, float viewportX, float viewportY)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
            var ground = new Plane(Vector3.up, Vector3.zero);
            return ground.Raycast(ray, out float distance) ? ray.GetPoint(distance) : Vector3.zero;
        }

        private static float FindLowestGroundViewport(Camera camera)
        {
            const int samples = 48;
            var ground = new Plane(Vector3.up, Vector3.zero);
            for (int i = 0; i <= samples; i++)
            {
                float viewportY = i / (float)samples;
                Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, viewportY, 0f));
                if (ground.Raycast(ray, out _))
                {
                    return viewportY;
                }
            }

            return 0.2f;
        }
    }
}
