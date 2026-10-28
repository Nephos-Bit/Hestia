using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WinterMarket
{
    /// <summary>A single grid-edge contact between two separate fence placements.</summary>
    public struct FenceJoin
    {
        public readonly Vector2Int Cell;
        public readonly Vector2Int Direction;

        public FenceJoin(Vector2Int cell, Vector2Int direction)
        {
            Cell = cell;
            Direction = direction;
        }
    }

    [DisallowMultipleComponent]
    public sealed class MarketBuildObject : MonoBehaviour
    {
        private readonly List<Light> warmLights = new List<Light>(1);
        private readonly List<Renderer> warmRenderers = new List<Renderer>(2);
        private Material[] previewMaterials = Array.Empty<Material>();

        public PlacedBuildObjectData Data { get; private set; }
        public BuildObjectDefinition Definition { get; private set; }
        public IReadOnlyList<Light> WarmLights => warmLights;
        public IReadOnlyList<Renderer> WarmRenderers => warmRenderers;

        public void Configure(PlacedBuildObjectData data, BuildObjectDefinition definition, Material[] ghosts)
        {
            Data = data;
            Definition = definition;
            previewMaterials = ghosts ?? Array.Empty<Material>();
        }

        public void AddWarmLight(Light light)
        {
            if (light != null) warmLights.Add(light);
        }

        public void AddWarmRenderer(Renderer renderer)
        {
            if (renderer != null) warmRenderers.Add(renderer);
        }

        private void OnDestroy()
        {
            WinterMarketMaterialLibrary.DestroyOwned(previewMaterials);
        }
    }

    /// <summary>Compact procedural voxel models for every entry in the construction catalogue.</summary>
    public static class BuildObjectVisual
    {
        private const float Voxel = VoxelVolume.GridSize;
        public static MarketBuildObject Create(
            PlacedBuildObjectData data,
            Material[] sourcePalette,
            Transform parent,
            bool preview = false,
            Material snowCapMaterial = null,
            IReadOnlyList<FenceJoin> fenceJoins = null)
        {
            BuildObjectDefinition definition = BuildObjectLibrary.Get(data.type);
            Material[] palette = preview ? CreatePreviewPalette(sourcePalette) : sourcePalette;
            var root = new GameObject(preview ? "Platzierungsvorschau" : definition.DisplayName);
            root.transform.SetParent(parent, false);
            root.transform.position = data.Position;
            root.transform.rotation = data.Rotation;
            root.isStatic = !preview;
            MarketBuildObject result = root.AddComponent<MarketBuildObject>();
            result.Configure(data, definition, preview ? palette : null);

            switch (data.type)
            {
                case BuildObjectType.BakeryStall:
                    CreateStall(result, palette, VoxelMaterialSlot.Slot4, "Bäckerei", false, true, snowCapMaterial);
                    break;
                case BuildObjectType.DrinksStall:
                    CreateStall(result, palette, VoxelMaterialSlot.Slot9, "Heisse Getränke", false, false, snowCapMaterial);
                    break;
                case BuildObjectType.CocoaStall:
                    CreateStall(result, palette, VoxelMaterialSlot.Slot4, "Kakao", true, false, snowCapMaterial);
                    break;
                case BuildObjectType.SmallFir:
                    CreateFir(result, palette, false, snowCapMaterial);
                    break;
                case BuildObjectType.Snowman:
                    CreateSnowman(result, palette);
                    break;
                case BuildObjectType.Bench:
                    CreateBench(result, palette);
                    break;
                case BuildObjectType.Lantern:
                    CreateLantern(result, palette);
                    break;
                case BuildObjectType.FireBowl:
                    CreateFireBowl(result, palette);
                    break;
                case BuildObjectType.Crates:
                    CreateCrates(result, palette);
                    break;
                case BuildObjectType.Fence:
                    CreateFence(result, palette, fenceJoins);
                    break;
                case BuildObjectType.StringLights:
                    CreateStringLights(result, palette);
                    break;
                case BuildObjectType.ChristmasTree:
                    CreateFir(result, palette, true, snowCapMaterial);
                    break;
                case BuildObjectType.Handcart:
                    CreateHandcart(result, palette);
                    break;
                case BuildObjectType.PathTile:
                    CreatePath(result, palette);
                    break;
            }

            AddFootprintCollider(result, preview);
            return result;
        }

        private static void CreateStall(
            MarketBuildObject result,
            Material[] palette,
            VoxelMaterialSlot roof,
            string signLabel,
            bool compact,
            bool bakery,
            Material snowCapMaterial)
        {
            if (!compact)
            {
                CreateLegacyStall(result, palette, roof, bakery, snowCapMaterial);
                return;
            }
            int width = compact ? 12 : 16;
            int depth = compact ? 8 : 10;
            int height = compact ? 17 : 19;
            var body = new VoxelVolume(new Vector3Int(width, height, depth));
            body.FillCuboid(0, 0, 0, width, 2, depth, VoxelMaterialSlot.Slot3);
            body.FillCuboid(1, 2, depth - 2, width - 2, 11, 2, VoxelMaterialSlot.Slot2);
            body.FillCuboid(1, 2, 1, 2, 14, 2, VoxelMaterialSlot.Slot3);
            body.FillCuboid(width - 3, 2, 1, 2, 14, 2, VoxelMaterialSlot.Slot3);
            body.FillCuboid(1, 2, depth - 3, 2, 14, 2, VoxelMaterialSlot.Slot3);
            body.FillCuboid(width - 3, 2, depth - 3, 2, 14, 2, VoxelMaterialSlot.Slot3);
            body.FillCuboid(1, 7, 0, width - 2, 2, 3, VoxelMaterialSlot.Slot2);
            body.FillCuboid(1, 15, 1, width - 2, 2, depth - 2, roof);
            body.FillCuboid(0, 17, 0, width, 2, depth, roof);
            body.FillCuboid(0, 19, 0, width, 1, depth, VoxelMaterialSlot.Slot0);
            AddVolume(result, "Budenholz", body, palette, Vector3.up * 0.01f);

            var glow = new VoxelVolume(new Vector3Int(width, 4, 1));
            glow.FillCuboid(2, 1, 0, width - 4, 1, 1, VoxelMaterialSlot.Slot6);
            Renderer renderer = AddVolume(result, "Budenlicht", glow, palette,
                new Vector3(0f, 1.55f, -depth * Voxel * 0.48f));
            result.AddWarmRenderer(renderer);
            CreateWarmLight(result, new Vector3(0f, 1.65f, -depth * Voxel * 0.3f), 3.4f, 1.05f);

            // A small coloured sign is deliberately symbolic; it reads on phones without text
            // meshes or texture assets.
            var sign = new VoxelVolume(new Vector3Int(Mathf.Max(5, width - 4), 4, 1));
            sign.FillCuboid(0, 0, 0, sign.Width, sign.Height, 1, roof);
            sign.FillCuboid(1, 1, 0, sign.Width - 2, 2, 1, VoxelMaterialSlot.Slot14);
            AddVolume(result, signLabel + " Schild", sign, palette,
                new Vector3(0f, 2.55f, -depth * Voxel * 0.55f));
        }

        private static void CreateFir(
            MarketBuildObject result,
            Material[] palette,
            bool decorated,
            Material snowCapMaterial)
        {
            if (decorated)
            {
                CreateLegacyChristmasTree(result, palette, snowCapMaterial);
                return;
            }
            const int size = 17;
            const int height = 20;
            var tree = new VoxelVolume(new Vector3Int(size, height, size));
            int center = size / 2;
            tree.FillCuboid(center - 1, 0, center - 1, 3, 5, 3, VoxelMaterialSlot.Slot3);
            const int levels = 8;
            for (int i = 0; i < levels; i++)
            {
                int radius = Mathf.Max(1, (levels - i + 1) / 2);
                int side = radius * 2 + 1;
                int y = 4 + i;
                tree.FillCuboid(center - radius, y, center - radius, side, 1, side, VoxelMaterialSlot.Slot5);
                if (i % 2 == 0 && radius > 1)
                {
                    tree.FillCuboid(center - radius, y + 1, center - radius, side, 1, 1, VoxelMaterialSlot.Slot0);
                    tree.FillCuboid(center - radius, y + 1, center + radius, side, 1, 1, VoxelMaterialSlot.Slot0);
                }
            }
            AddVolume(result, "Kleine Tanne", tree, palette, Vector3.zero);
        }

        private static void CreateLegacyChristmasTree(
            MarketBuildObject result,
            Material[] palette,
            Material snowCapMaterial)
        {
            var tree = new VoxelVolume(new Vector3Int(19, 30, 19));
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
                treeLights.SetVoxel(lightPositions[i, 0], lightPositions[i, 1], lightPositions[i, 2], VoxelMaterialSlot.Slot6);
                tree.SetVoxel(lightPositions[i, 0], lightPositions[i, 1], lightPositions[i, 2], VoxelMaterialSlot.Empty);
            }
            decorations.SetVoxel(9, 20, 9, VoxelMaterialSlot.Slot4);
            decorations.SetVoxel(8, 16, 13, VoxelMaterialSlot.Slot4);
            decorations.SetVoxel(12, 13, 4, VoxelMaterialSlot.Slot4);
            tree.SetVoxel(8, 16, 13, VoxelMaterialSlot.Empty);
            tree.SetVoxel(12, 13, 4, VoxelMaterialSlot.Empty);
            decorations.FillCuboid(8, 20, 9, 3, 3, 1, VoxelMaterialSlot.Slot6);
            decorations.FillCuboid(9, 19, 8, 1, 5, 3, VoxelMaterialSlot.Slot6);

            AddVolume(result, "Gestufter Weihnachtsbaum", tree, palette, Vector3.zero);
            Material[] capPalette = result.name.Contains("Vorschau") || snowCapMaterial == null
                ? palette
                : new[] { snowCapMaterial };
            Renderer snowRenderer = AddVolume(result, "Weihnachtsbaum Schneeschichten", snowCap, capPalette, Vector3.zero);
            snowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            snowRenderer.receiveShadows = false;
            AddVolume(result, "Baumschmuck", decorations, palette, Vector3.zero);
            Renderer lights = AddVolume(result, "Baumlichter", treeLights, palette, Vector3.zero);
            result.AddWarmRenderer(lights);
            CreateWarmLight(result, new Vector3(0f, 2.1f, 0f), 3.4f, 0.95f);
        }

        /// <summary>
        /// Exact V0.2 booth proportions and layering. The editable rendition deliberately
        /// reuses the former support posts, counters, roof rhythm, snow cap and light layout
        /// rather than replacing a known market structure with a smaller generic prop.
        /// </summary>
        private static void CreateLegacyStall(
            MarketBuildObject result,
            Material[] palette,
            VoxelMaterialSlot roof,
            bool bakery,
            Material snowCapMaterial)
        {
            var volume = new VoxelVolume(new Vector3Int(29, 28, 19));
            var snowCap = new VoxelVolume(new Vector3Int(29, 28, 19));
            var glow = new VoxelVolume(new Vector3Int(29, 28, 19));
            VoxelMaterialSlot wood = bakery ? VoxelMaterialSlot.Slot2 : VoxelMaterialSlot.Slot3;

            volume.FillCuboid(1, 0, 1, 27, 2, 17, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(2, 2, 2, 2, 18, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(25, 2, 2, 2, 18, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(2, 2, 15, 2, 18, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(25, 2, 15, 2, 18, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(1, 2, 16, 27, 16, 2, wood);
            volume.FillCuboid(1, 2, 2, 2, 15, 14, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(26, 2, 2, 2, 15, 14, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(3, 8, 0, 23, 2, 4, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(3, 7, 1, 23, 1, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(3, 16, 1, 23, 2, 2, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(4, 4, 14, 21, 10, 1, VoxelMaterialSlot.Slot14);
            volume.FillCuboid(3, 3, 14, 2, 14, 1, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(24, 3, 14, 2, 14, 1, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(3, 18, 1, 23, 1, 2, bakery ? VoxelMaterialSlot.Slot4 : VoxelMaterialSlot.Slot5);
            for (int x = 5; x <= 22; x += 5)
            {
                volume.FillCuboid(x, 18, 0, 2, 1, 2,
                    (x / 5) % 2 == 0 ? VoxelMaterialSlot.Slot4 : VoxelMaterialSlot.Slot5);
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
            glow.FillCuboid(5, 14, 13, 19, 1, 1, VoxelMaterialSlot.Slot6);
            glow.FillCuboid(7, 12, 13, 2, 1, 1, VoxelMaterialSlot.Slot6);
            glow.FillCuboid(19, 12, 13, 2, 1, 1, VoxelMaterialSlot.Slot6);

            Vector3 raised = Vector3.up * 0.315f;
            AddVolume(result, bakery ? "Bäckerei" : "Heisse Getränke", volume, palette, raised);
            Material[] capPalette = result.name.Contains("Vorschau") || snowCapMaterial == null
                ? palette
                : new[] { snowCapMaterial };
            Renderer capRenderer = AddVolume(result, "Buden-Dachschnee", snowCap, capPalette, raised);
            capRenderer.shadowCastingMode = ShadowCastingMode.Off;
            capRenderer.receiveShadows = false;
            Renderer glowRenderer = AddVolume(result, "Buden-Fensterlicht", glow, palette, raised);
            result.AddWarmRenderer(glowRenderer);
            CreateWarmLight(result, new Vector3(0f, 1.85f, -0.45f), 3.8f, 1.55f);

            var sign = new VoxelVolume(new Vector3Int(13, 6, 2));
            sign.FillCuboid(0, 0, 1, 13, 6, 1, bakery ? VoxelMaterialSlot.Slot4 : VoxelMaterialSlot.Slot5);
            sign.FillCuboid(1, 1, 1, 11, 3, 1, VoxelMaterialSlot.Slot14);
            sign.FillCuboid(6, 0, 0, 1, 6, 1, VoxelMaterialSlot.Slot3);
            AddVolume(result, bakery ? "Bäckerei Schild" : "Getränke Schild", sign, palette,
                raised + new Vector3(0f, 2.8f, -1.45f));
        }

        private static void CreateSnowman(MarketBuildObject result, Material[] palette)
        {
            var snowman = new VoxelVolume(new Vector3Int(7, 14, 7));
            snowman.FillCuboid(1, 0, 1, 5, 5, 5, VoxelMaterialSlot.Slot0);
            snowman.FillCuboid(2, 5, 2, 3, 4, 3, VoxelMaterialSlot.Slot0);
            snowman.FillCuboid(2, 9, 2, 3, 3, 3, VoxelMaterialSlot.Slot0);
            snowman.FillCuboid(2, 12, 2, 3, 1, 3, VoxelMaterialSlot.Slot7);
            snowman.FillCuboid(1, 13, 1, 5, 1, 5, VoxelMaterialSlot.Slot7);
            snowman.SetVoxel(2, 10, 1, VoxelMaterialSlot.Slot13);
            snowman.SetVoxel(4, 10, 1, VoxelMaterialSlot.Slot13);
            snowman.FillCuboid(3, 9, 0, 1, 1, 2, VoxelMaterialSlot.Slot4);
            AddVolume(result, "Schneemann", snowman, palette, Vector3.zero);
        }

        private static void CreateBench(MarketBuildObject result, Material[] palette)
        {
            var bench = new VoxelVolume(new Vector3Int(22, 9, 6));
            bench.FillCuboid(1, 4, 1, 20, 2, 4, VoxelMaterialSlot.Slot2);
            bench.FillCuboid(1, 7, 5, 20, 2, 1, VoxelMaterialSlot.Slot2);
            bench.FillCuboid(2, 0, 1, 2, 5, 2, VoxelMaterialSlot.Slot3);
            bench.FillCuboid(18, 0, 1, 2, 5, 2, VoxelMaterialSlot.Slot3);
            bench.FillCuboid(0, 6, 4, 22, 1, 2, VoxelMaterialSlot.Slot0);
            AddVolume(result, "Holzbank", bench, palette, Vector3.zero);
        }

        private static void CreateLantern(MarketBuildObject result, Material[] palette)
        {
            var lamp = new VoxelVolume(new Vector3Int(5, 22, 5));
            lamp.FillCuboid(2, 0, 2, 1, 17, 1, VoxelMaterialSlot.Slot13);
            lamp.FillCuboid(1, 16, 1, 3, 5, 3, VoxelMaterialSlot.Slot13);
            lamp.FillCuboid(2, 17, 2, 1, 3, 1, VoxelMaterialSlot.Slot6);
            lamp.FillCuboid(0, 21, 0, 5, 1, 5, VoxelMaterialSlot.Slot0);
            AddVolume(result, "Laterne", lamp, palette, Vector3.zero);
            var glow = new VoxelVolume(new Vector3Int(5, 22, 5));
            glow.FillCuboid(2, 17, 2, 1, 3, 1, VoxelMaterialSlot.Slot6);
            Renderer renderer = AddVolume(result, "Laternenlicht", glow, palette, Vector3.zero);
            result.AddWarmRenderer(renderer);
            CreateWarmLight(result, new Vector3(0f, 2.75f, 0f), 3.2f, 1.15f);
        }

        private static void CreateFireBowl(MarketBuildObject result, Material[] palette)
        {
            var bowl = new VoxelVolume(new Vector3Int(8, 10, 8));
            bowl.FillCuboid(1, 0, 1, 6, 2, 6, VoxelMaterialSlot.Slot13);
            bowl.FillCuboid(2, 2, 2, 4, 2, 4, VoxelMaterialSlot.Slot3);
            AddVolume(result, "Feuerkorb", bowl, palette, Vector3.zero);
            var glow = new VoxelVolume(new Vector3Int(8, 10, 8));
            glow.FillCuboid(2, 4, 2, 4, 2, 4, VoxelMaterialSlot.Slot6);
            glow.FillCuboid(3, 6, 3, 2, 3, 2, VoxelMaterialSlot.Slot6);
            Renderer renderer = AddVolume(result, "Feuerkorb Glut", glow, palette, Vector3.zero);
            result.AddWarmRenderer(renderer);
            CreateWarmLight(result, new Vector3(0f, 1.15f, 0f), 3.2f, 1.0f);
        }

        private static void CreateCrates(MarketBuildObject result, Material[] palette)
        {
            var crates = new VoxelVolume(new Vector3Int(18, 10, 12));
            FillCrate(crates, 0, 0, 0);
            FillCrate(crates, 8, 0, 2);
            FillCrate(crates, 4, 5, 1);
            AddVolume(result, "Holzkisten", crates, palette, Vector3.zero);
        }

        private static void FillCrate(VoxelVolume volume, int x, int y, int z)
        {
            volume.FillCuboid(x, y, z, 7, 5, 7, VoxelMaterialSlot.Slot2);
            volume.FillCuboid(x + 1, y + 1, z, 5, 3, 1, VoxelMaterialSlot.Slot3);
            volume.FillCuboid(x, y + 2, z, 7, 1, 1, VoxelMaterialSlot.Slot3);
        }

        private static void CreateFence(
            MarketBuildObject result,
            Material[] palette,
            IReadOnlyList<FenceJoin> joins)
        {
            // Match the complete 3 x 1 construction footprint (1.80 x 0.60 m).  The old
            // 1.35 x 0.30 m model left visible snow gaps between otherwise adjacent fences.
            const int length = 12;
            const int depth = 4;
            const int height = 8;
            var fence = new VoxelVolume(new Vector3Int(length, height, depth));
            fence.FillCuboid(0, 2, 1, length, 2, 2, VoxelMaterialSlot.Slot2);
            fence.FillCuboid(0, 5, 1, length, 1, 2, VoxelMaterialSlot.Slot2);
            AddFencePost(fence, 0, height);
            AddFencePost(fence, length / 2 - 1, height);
            AddFencePost(fence, length - 1, height);
            AddFenceCornerJoins(fence, result.transform, joins, length, depth, height);
            AddVolume(result, "Zaunelement", fence, palette, Vector3.zero);
        }

        private static void AddFencePost(VoxelVolume fence, int x, int height)
        {
            fence.FillCuboid(Mathf.Clamp(x, 0, fence.Width - 1), 0, 1, 1, height, 2, VoxelMaterialSlot.Slot3);
        }

        private static void AddFenceCornerJoins(
            VoxelVolume fence,
            Transform fenceTransform,
            IReadOnlyList<FenceJoin> joins,
            int length,
            int depth,
            int height)
        {
            if (joins == null) return;

            for (int index = 0; index < joins.Count; index++)
            {
                FenceJoin join = joins[index];
                Vector3 worldCell = new Vector3(
                    join.Cell.x * BuildModeController.GridSize,
                    0f,
                    join.Cell.y * BuildModeController.GridSize);
                Vector3 localCell = fenceTransform.InverseTransformPoint(worldCell);
                Vector3 localDirection = Quaternion.Inverse(fenceTransform.rotation) *
                    new Vector3(join.Direction.x, 0f, join.Direction.y);

                // End-to-end rails already meet because the base mesh spans the complete
                // footprint. Only sideways contacts need a short branch at their exact cell,
                // so L and T corners become one continuous fence rather than two touching props.
                if (Mathf.Abs(localDirection.x) >= Mathf.Abs(localDirection.z)) continue;

                int centerX = Mathf.RoundToInt((localCell.x + length * Voxel * 0.5f) / Voxel);
                int postX = Mathf.Clamp(centerX - 1, 0, length - 2);
                int railX = Mathf.Clamp(centerX - 1, 0, length - 2);
                int railZ = localDirection.z > 0f ? depth / 2 : 0;
                fence.FillCuboid(railX, 2, railZ, 2, 2, depth / 2, VoxelMaterialSlot.Slot2);
                fence.FillCuboid(railX, 5, railZ, 2, 1, depth / 2, VoxelMaterialSlot.Slot2);
                fence.FillCuboid(postX, 0, 1, 2, height, 2, VoxelMaterialSlot.Slot3);
            }
        }

        private static void CreateStringLights(MarketBuildObject result, Material[] palette)
        {
            var lights = new VoxelVolume(new Vector3Int(10, 8, 2));
            lights.FillCuboid(0, 7, 0, 10, 1, 1, VoxelMaterialSlot.Slot3);
            AddVolume(result, "Lichterdekoration", lights, palette, Vector3.zero);
            var glow = new VoxelVolume(new Vector3Int(10, 8, 2));
            for (int x = 1; x < 10; x += 3) glow.FillCuboid(x, 4, 0, 1, 2, 1, VoxelMaterialSlot.Slot6);
            Renderer renderer = AddVolume(result, "Lichterdekoration Glut", glow, palette, Vector3.zero);
            result.AddWarmRenderer(renderer);
        }

        private static void CreateHandcart(MarketBuildObject result, Material[] palette)
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
            AddVolume(result, "Handkarren", cart, palette, Vector3.zero);
        }

        private static void CreatePath(MarketBuildObject result, Material[] palette)
        {
            var path = new VoxelVolume(new Vector3Int(4, 1, 4));
            path.FillCuboid(0, 0, 0, 4, 1, 4, VoxelMaterialSlot.Slot8);
            Renderer renderer = AddVolume(result, "Geräumter Schneepfad", path, palette, new Vector3(0f, 0.305f, 0f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Renderer AddVolume(
            MarketBuildObject result,
            string name,
            VoxelVolume volume,
            Material[] palette,
            Vector3 localCenter)
        {
            VoxelAssetGenerator asset = VoxelAssetGenerator.Create(name, volume, palette, result.transform, !result.name.Contains("Vorschau"));
            Vector3 dimensions = new Vector3(volume.Width, volume.Height, volume.Depth) * Voxel;
            asset.transform.localPosition = localCenter - new Vector3(dimensions.x, 0f, dimensions.z) * 0.5f;
            MeshRenderer renderer = asset.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = result.name.Contains("Vorschau") ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !result.name.Contains("Vorschau");
            return renderer;
        }

        private static void CreateWarmLight(MarketBuildObject result, Vector3 localPosition, float range, float intensity)
        {
            var lightObject = new GameObject("Warmes Licht");
            lightObject.transform.SetParent(result.transform, false);
            lightObject.transform.localPosition = localPosition;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = WinterPalette.Lantern;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            result.AddWarmLight(light);
        }

        private static void AddFootprintCollider(MarketBuildObject result, bool preview)
        {
            // Roads are a generated strip, not individually movable props. Omitting tile
            // colliders prevents a tap on a road from accidentally detaching one tile.
            if (preview || result.Data.type == BuildObjectType.PathTile) return;
            Vector2Int footprint = result.Definition.RotatedFootprint(result.Data.rotation);
            BoxCollider collider = result.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.8f, 0f);
            collider.size = new Vector3(
                footprint.x * BuildModeController.GridSize,
                1.6f,
                footprint.y * BuildModeController.GridSize);
        }

        private static Material[] CreatePreviewPalette(Material[] source)
        {
            if (source == null) return Array.Empty<Material>();
            var ghost = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null) continue;
                ghost[i] = new Material(source[i]) { name = source[i].name + " Vorschau" };
                if (ghost[i].HasProperty("_BaseColor"))
                {
                    Color color = ghost[i].GetColor("_BaseColor");
                    color.a = 0.48f;
                    ghost[i].SetColor("_BaseColor", color);
                }
                if (ghost[i].HasProperty("_Surface")) ghost[i].SetFloat("_Surface", 1f);
                if (ghost[i].HasProperty("_Blend")) ghost[i].SetFloat("_Blend", 0f);
                ghost[i].renderQueue = (int)RenderQueue.Transparent;
            }
            return ghost;
        }
    }
}
