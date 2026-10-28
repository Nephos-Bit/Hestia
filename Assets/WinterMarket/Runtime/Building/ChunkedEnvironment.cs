using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WinterMarket
{
    /// <summary>Describes one confirmed layout mutation without changing persisted layout data.</summary>
    internal struct LayoutDelta
    {
        public readonly PlacedBuildObjectData Before;
        public readonly PlacedBuildObjectData After;

        public LayoutDelta(PlacedBuildObjectData before, PlacedBuildObjectData after)
        {
            Before = before;
            After = after;
        }
    }

    /// <summary>
    /// Keeps raised snow in independently rebuildable chunks. A construction edit only changes
    /// chunks overlapping its footprint and clearance instead of meshing the full build area.
    /// </summary>
    internal sealed class ChunkedDeepSnowController
    {
        private const int ChunkSizeCells = 48;
        private const int ClearanceCells = 2;
        private const int SnowHeightVoxels = 2;
        private static readonly int MinX = Mathf.RoundToInt(BuildModeController.BuildMinX / BuildModeController.GridSize);
        private static readonly int MaxX = Mathf.RoundToInt(BuildModeController.BuildMaxX / BuildModeController.GridSize);
        private static readonly int MinZ = Mathf.RoundToInt(BuildModeController.BuildMinZ / BuildModeController.GridSize);
        private static readonly int MaxZ = Mathf.RoundToInt(BuildModeController.BuildMaxZ / BuildModeController.GridSize);

        private readonly Transform root;
        private readonly Material[] palette;
        private readonly Dictionary<Vector2Int, SnowChunk> chunks = new Dictionary<Vector2Int, SnowChunk>();
        private readonly HashSet<Vector2Int> clearedCells = new HashSet<Vector2Int>();
        private bool initialized;

        public ChunkedDeepSnowController(Transform parent, Material[] sourcePalette)
        {
            root = new GameObject("Tiefschnee Chunks").transform;
            root.SetParent(parent, false);
            palette = sourcePalette;
        }

        public void ApplyLayoutChanges(BuildLayoutData layout, IReadOnlyList<LayoutDelta> changes)
        {
            if (layout == null || root == null) return;
            RebuildClearCells(layout);

            if (!initialized)
            {
                for (int z = 0; z < ChunkCountZ; z++)
                {
                    for (int x = 0; x < ChunkCountX; x++)
                    {
                        RebuildChunk(new Vector2Int(x, z));
                    }
                }
                initialized = true;
                return;
            }

            if (changes == null || changes.Count == 0) return;
            var dirty = new HashSet<Vector2Int>();
            for (int i = 0; i < changes.Count; i++)
            {
                MarkAffectedChunks(changes[i].Before, ClearanceCells, dirty);
                MarkAffectedChunks(changes[i].After, ClearanceCells, dirty);
            }
            foreach (Vector2Int chunk in dirty) RebuildChunk(chunk);
        }

        public void Clear()
        {
            foreach (SnowChunk chunk in chunks.Values) chunk.Dispose();
            chunks.Clear();
            initialized = false;
        }

        private int ChunkCountX => (MaxX - MinX) / ChunkSizeCells + 1;
        private int ChunkCountZ => (MaxZ - MinZ) / ChunkSizeCells + 1;

        private void RebuildClearCells(BuildLayoutData layout)
        {
            clearedCells.Clear();
            for (int x = MinX; x <= MaxX; x++)
            {
                for (int z = -5 - ClearanceCells; z <= -3 + ClearanceCells; z++)
                {
                    clearedCells.Add(new Vector2Int(x, z));
                }
            }

            for (int index = 0; index < layout.objects.Count; index++)
            {
                PlacedBuildObjectData item = layout.objects[index];
                if (item == null) continue;
                AddClearedFootprint(item);
            }
        }

        private void AddClearedFootprint(PlacedBuildObjectData data)
        {
            BuildObjectDefinition definition = BuildObjectLibrary.Get(data.type);
            Vector2Int size = definition.RotatedFootprint(data.rotation);
            int centerX = Mathf.RoundToInt(data.x / BuildModeController.GridSize);
            int centerZ = Mathf.RoundToInt(data.z / BuildModeController.GridSize);
            int minX = Mathf.Max(MinX, centerX - size.x / 2 - ClearanceCells);
            int maxX = Mathf.Min(MaxX, centerX + (size.x - 1) / 2 + ClearanceCells);
            int minZ = Mathf.Max(MinZ, centerZ - size.y / 2 - ClearanceCells);
            int maxZ = Mathf.Min(MaxZ, centerZ + (size.y - 1) / 2 + ClearanceCells);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++) clearedCells.Add(new Vector2Int(x, z));
            }
        }

        private void MarkAffectedChunks(PlacedBuildObjectData data, int padding, HashSet<Vector2Int> output)
        {
            if (data == null) return;
            BuildObjectDefinition definition = BuildObjectLibrary.Get(data.type);
            Vector2Int size = definition.RotatedFootprint(data.rotation);
            int centerX = Mathf.RoundToInt(data.x / BuildModeController.GridSize);
            int centerZ = Mathf.RoundToInt(data.z / BuildModeController.GridSize);
            int minX = centerX - size.x / 2 - padding;
            int maxX = centerX + (size.x - 1) / 2 + padding;
            int minZ = centerZ - size.y / 2 - padding;
            int maxZ = centerZ + (size.y - 1) / 2 + padding;
            int firstX = Mathf.Clamp((minX - MinX) / ChunkSizeCells, 0, ChunkCountX - 1);
            int lastX = Mathf.Clamp((maxX - MinX) / ChunkSizeCells, 0, ChunkCountX - 1);
            int firstZ = Mathf.Clamp((minZ - MinZ) / ChunkSizeCells, 0, ChunkCountZ - 1);
            int lastZ = Mathf.Clamp((maxZ - MinZ) / ChunkSizeCells, 0, ChunkCountZ - 1);
            for (int z = firstZ; z <= lastZ; z++)
            {
                for (int x = firstX; x <= lastX; x++) output.Add(new Vector2Int(x, z));
            }
        }

        private void RebuildChunk(Vector2Int key)
        {
            int startX = MinX + key.x * ChunkSizeCells;
            int startZ = MinZ + key.y * ChunkSizeCells;
            int width = Mathf.Min(ChunkSizeCells, MaxX - startX + 1);
            int depth = Mathf.Min(ChunkSizeCells, MaxZ - startZ + 1);
            if (width <= 0 || depth <= 0) return;

            if (!chunks.TryGetValue(key, out SnowChunk chunk))
            {
                chunk = new SnowChunk(root, palette, key);
                chunks.Add(key, chunk);
            }
            chunk.Rebuild(startX, startZ, width, depth, clearedCells);
        }

        private static VoxelMaterialSlot GetSnowVariation(Vector2Int cell)
        {
            int patchX = Mathf.FloorToInt(cell.x / 3f);
            int patchZ = Mathf.FloorToInt(cell.y / 3f);
            unchecked
            {
                int hash = patchX * 92837111 ^ patchZ * 689287499;
                // Broad, sparse patches retain the calm snowfield while making slopes and
                // footsteps read as layered snow rather than an unbroken white plane.
                return (hash & 3) == 0 ? VoxelMaterialSlot.Slot1 : VoxelMaterialSlot.Slot0;
            }
        }

        private sealed class SnowChunk
        {
            private readonly Transform root;
            private readonly Material[] palette;
            private readonly Vector2Int key;
            private VoxelAssetGenerator generator;
            private VoxelVolume volume;

            public SnowChunk(Transform parent, Material[] sourcePalette, Vector2Int chunkKey)
            {
                root = parent;
                palette = sourcePalette;
                key = chunkKey;
            }

            public void Rebuild(int startX, int startZ, int width, int depth, HashSet<Vector2Int> clearedCells)
            {
                if (volume == null || volume.Width != width || volume.Depth != depth)
                {
                    volume = new VoxelVolume(new Vector3Int(width, SnowHeightVoxels, depth));
                }
                else
                {
                    volume.Clear();
                }

                for (int z = 0; z < depth; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Vector2Int cell = new Vector2Int(startX + x, startZ + z);
                        if (clearedCells.Contains(cell)) continue;
                        volume.FillCuboid(x, 0, z, 1, SnowHeightVoxels, 1, GetSnowVariation(cell));
                    }
                }

                if (generator == null)
                {
                    generator = VoxelAssetGenerator.Create(
                        "Tiefschnee Chunk " + key.x + "-" + key.y,
                        volume,
                        palette,
                        root,
                        true);
                    generator.transform.localPosition = new Vector3(
                        startX * BuildModeController.GridSize - BuildModeController.GridSize * 0.5f,
                        0.30f,
                        startZ * BuildModeController.GridSize - BuildModeController.GridSize * 0.5f);
                    generator.transform.localScale = new Vector3(4f, 1f, 4f);
                    MeshRenderer renderer = generator.GetComponent<MeshRenderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }
                else
                {
                    generator.SetData(volume, palette);
                    generator.Rebuild();
                }
            }

            public void Dispose()
            {
                if (generator != null) UnityEngine.Object.Destroy(generator.gameObject);
                generator = null;
            }
        }
    }

    /// <summary>
    /// Batches deterministic forest trees into shared material meshes. It deliberately has no
    /// per-tree GameObjects, colliders, or renderers.
    /// </summary>
    internal sealed class ChunkedForestController
    {
        private const int ChunkSizeCells = 48;
        private const int TreeInfluencePaddingCells = 20;
        // Trees are still batched by chunk, so this gives the horizon a fuller silhouette
        // without introducing per-tree renderers or GameObjects.
        private const int StandardHashDivisor = 44;
        private const int HighHashDivisor = 28;
        private static readonly int MinX = Mathf.RoundToInt(BuildModeController.BuildMinX / BuildModeController.GridSize);
        private static readonly int MaxX = Mathf.RoundToInt(BuildModeController.BuildMaxX / BuildModeController.GridSize);
        private static readonly int MinZ = Mathf.RoundToInt(BuildModeController.BuildMinZ / BuildModeController.GridSize);
        private static readonly int MaxZ = Mathf.RoundToInt(BuildModeController.BuildMaxZ / BuildModeController.GridSize);
        private static readonly VoxelMaterialSlot[] ForestSlots =
        {
            VoxelMaterialSlot.Slot0,
            VoxelMaterialSlot.Slot3,
            VoxelMaterialSlot.Slot15
        };

        private readonly Transform root;
        private readonly Material[] palette;
        private readonly Dictionary<Vector2Int, ForestChunk> chunks = new Dictionary<Vector2Int, ForestChunk>();
        private readonly List<ForestExclusion> exclusions = new List<ForestExclusion>();
        private readonly List<ForestTreeInstance> scratchTrees = new List<ForestTreeInstance>(96);
        private TreeTemplate detailedTemplate;
        private TreeTemplate simplifiedTemplate;
        private bool highDetail;
        private bool initialized;

        public ChunkedForestController(Transform parent, Material[] sourcePalette)
        {
            root = new GameObject("Dynamischer Wald Chunks").transform;
            root.SetParent(parent, false);
            palette = sourcePalette;
        }

        public void SetHighDetail(bool value, BuildLayoutData layout)
        {
            if (highDetail == value) return;
            highDetail = value;
            if (initialized && layout != null)
            {
                RebuildExclusions(layout);
                RebuildAll();
            }
        }

        public void ApplyLayoutChanges(BuildLayoutData layout, IReadOnlyList<LayoutDelta> changes)
        {
            if (layout == null || root == null) return;
            EnsureTemplates();
            RebuildExclusions(layout);
            if (!initialized)
            {
                RebuildAll();
                initialized = true;
                return;
            }

            if (changes == null || changes.Count == 0) return;
            var dirty = new HashSet<Vector2Int>();
            for (int i = 0; i < changes.Count; i++)
            {
                MarkAffectedChunks(changes[i].Before, dirty);
                MarkAffectedChunks(changes[i].After, dirty);
            }
            foreach (Vector2Int chunk in dirty) RebuildChunk(chunk);
        }

        public void Clear()
        {
            foreach (ForestChunk chunk in chunks.Values) chunk.Dispose();
            chunks.Clear();
            DestroyTemplate(detailedTemplate);
            DestroyTemplate(simplifiedTemplate);
            detailedTemplate = null;
            simplifiedTemplate = null;
            initialized = false;
        }

        private int ChunkCountX => (MaxX - MinX) / ChunkSizeCells + 1;
        private int ChunkCountZ => (MaxZ - MinZ) / ChunkSizeCells + 1;

        private void EnsureTemplates()
        {
            if (detailedTemplate == null) detailedTemplate = CreateTemplate(true);
            if (simplifiedTemplate == null) simplifiedTemplate = CreateTemplate(false);
        }

        private void RebuildAll()
        {
            for (int z = 0; z < ChunkCountZ; z++)
            {
                for (int x = 0; x < ChunkCountX; x++) RebuildChunk(new Vector2Int(x, z));
            }
        }

        private void RebuildExclusions(BuildLayoutData layout)
        {
            exclusions.Clear();
            Vector2Int treeSize = BuildObjectLibrary.Get(BuildObjectType.SmallFir).Footprint;
            for (int i = 0; i < layout.objects.Count; i++)
            {
                PlacedBuildObjectData item = layout.objects[i];
                if (item == null) continue;
                Vector2Int size = BuildObjectLibrary.Get(item.type).RotatedFootprint(item.rotation);
                int centerX = Mathf.RoundToInt(item.x / BuildModeController.GridSize);
                int centerZ = Mathf.RoundToInt(item.z / BuildModeController.GridSize);
                int paddingX = Mathf.CeilToInt((treeSize.x + size.x) * 0.5f) + 1;
                int paddingZ = Mathf.CeilToInt((treeSize.y + size.y) * 0.5f) + 1;
                exclusions.Add(new ForestExclusion(
                    centerX - paddingX,
                    centerX + paddingX,
                    centerZ - paddingZ,
                    centerZ + paddingZ));
            }
        }

        private void MarkAffectedChunks(PlacedBuildObjectData data, HashSet<Vector2Int> output)
        {
            if (data == null) return;
            BuildObjectDefinition definition = BuildObjectLibrary.Get(data.type);
            Vector2Int size = definition.RotatedFootprint(data.rotation);
            int centerX = Mathf.RoundToInt(data.x / BuildModeController.GridSize);
            int centerZ = Mathf.RoundToInt(data.z / BuildModeController.GridSize);
            int minX = centerX - size.x / 2 - TreeInfluencePaddingCells;
            int maxX = centerX + (size.x - 1) / 2 + TreeInfluencePaddingCells;
            int minZ = centerZ - size.y / 2 - TreeInfluencePaddingCells;
            int maxZ = centerZ + (size.y - 1) / 2 + TreeInfluencePaddingCells;
            int firstX = Mathf.Clamp((minX - MinX) / ChunkSizeCells, 0, ChunkCountX - 1);
            int lastX = Mathf.Clamp((maxX - MinX) / ChunkSizeCells, 0, ChunkCountX - 1);
            int firstZ = Mathf.Clamp((minZ - MinZ) / ChunkSizeCells, 0, ChunkCountZ - 1);
            int lastZ = Mathf.Clamp((maxZ - MinZ) / ChunkSizeCells, 0, ChunkCountZ - 1);
            for (int z = firstZ; z <= lastZ; z++)
            {
                for (int x = firstX; x <= lastX; x++) output.Add(new Vector2Int(x, z));
            }
        }

        private void RebuildChunk(Vector2Int key)
        {
            int startX = MinX + key.x * ChunkSizeCells;
            int startZ = MinZ + key.y * ChunkSizeCells;
            int width = Mathf.Min(ChunkSizeCells, MaxX - startX + 1);
            int depth = Mathf.Min(ChunkSizeCells, MaxZ - startZ + 1);
            if (width <= 0 || depth <= 0) return;

            scratchTrees.Clear();
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2Int cell = new Vector2Int(startX + x, startZ + z);
                    if (!ShouldPlaceTree(cell)) continue;
                    int hash = StableHash(cell);
                    bool detailed = cell.sqrMagnitude <= 65 * 65;
                    TreeTemplate template = detailed ? detailedTemplate : simplifiedTemplate;
                    float widthScale = 0.78f + (StableHash(cell + new Vector2Int(17, 31)) % 43) * 0.01f;
                    float heightScale = 0.78f + (StableHash(cell + new Vector2Int(43, 11)) % 78) * 0.01f;
                    Matrix4x4 transform = Matrix4x4.TRS(
                        new Vector3(cell.x * BuildModeController.GridSize, VoxelVolume.GridSize * 2f,
                            cell.y * BuildModeController.GridSize),
                        Quaternion.Euler(0f, (hash & 3) * 90f, 0f),
                        new Vector3(widthScale, heightScale, widthScale)) *
                        Matrix4x4.Translate(-template.HalfExtents);
                    scratchTrees.Add(new ForestTreeInstance(template, transform));
                }
            }

            if (!chunks.TryGetValue(key, out ForestChunk chunk))
            {
                chunk = new ForestChunk(root, palette, key);
                chunks.Add(key, chunk);
            }
            chunk.Rebuild(scratchTrees);
        }

        private bool ShouldPlaceTree(Vector2Int cell)
        {
            if (cell.y >= -7 && cell.y <= -1) return false;
            if (IsCentralMarketOpening(cell)) return false;
            for (int i = 0; i < exclusions.Count; i++)
            {
                if (exclusions[i].Contains(cell)) return false;
            }
            int divisor = highDetail ? HighHashDivisor : StandardHashDivisor;
            return StableHash(cell) % divisor == 0;
        }

        private static bool IsCentralMarketOpening(Vector2Int cell)
        {
            float normalizedX = cell.x / 23f;
            // Keep the market floor clear, but allow a closer backdrop behind the stalls.
            // This removes the featureless snow field that previously surrounded the scene.
            float normalizedZ = (cell.y - 2f) / 13f;
            return normalizedX * normalizedX + normalizedZ * normalizedZ < 1f;
        }

        private static int StableHash(Vector2Int cell)
        {
            unchecked
            {
                int hash = cell.x * 73856093 ^ cell.y * 19349663;
                return hash == int.MinValue ? 0 : Mathf.Abs(hash);
            }
        }

        private static TreeTemplate CreateTemplate(bool detailed)
        {
            int size = detailed ? 17 : 13;
            int height = detailed ? 20 : 14;
            int levels = detailed ? 8 : 4;
            var volume = new VoxelVolume(new Vector3Int(size, height, size));
            int center = size / 2;
            volume.FillCuboid(center - 1, 0, center - 1, 3, detailed ? 5 : 4, 3, VoxelMaterialSlot.Slot3);
            for (int i = 0; i < levels; i++)
            {
                int radius = Mathf.Max(1, (levels - i + 1) / 2);
                int side = radius * 2 + 1;
                int y = 4 + i * (detailed ? 2 : 2);
                volume.FillCuboid(center - radius, y, center - radius, side, 1, side, VoxelMaterialSlot.Slot15);
                volume.FillCuboid(center - radius, y + 1, center - radius, side, 1, side, VoxelMaterialSlot.Slot0);
            }

            var usedSlots = new List<VoxelMaterialSlot>(3);
            Mesh mesh = VoxelGreedyMesher.CreateMesh(volume, detailed ? "Forest Tree Detail" : "Forest Tree Silhouette", usedSlots);
            // VoxelMaterialSlot is one-based because zero represents Empty. Slot15 is
            // therefore value 16 and needs an addressable entry at index 16.
            var subMeshes = new int[(int)VoxelMaterialSlot.Slot15 + 1];
            for (int i = 0; i < subMeshes.Length; i++) subMeshes[i] = -1;
            for (int i = 0; i < usedSlots.Count; i++) subMeshes[(int)usedSlots[i]] = i;
            return new TreeTemplate(
                mesh,
                subMeshes,
                new Vector3(size * VoxelVolume.GridSize, 0f, size * VoxelVolume.GridSize) * 0.5f);
        }

        private static void DestroyTemplate(TreeTemplate template)
        {
            if (template != null && template.Mesh != null) UnityEngine.Object.Destroy(template.Mesh);
        }

        private readonly struct ForestExclusion
        {
            private readonly int minX;
            private readonly int maxX;
            private readonly int minZ;
            private readonly int maxZ;

            public ForestExclusion(int minimumX, int maximumX, int minimumZ, int maximumZ)
            {
                minX = minimumX;
                maxX = maximumX;
                minZ = minimumZ;
                maxZ = maximumZ;
            }

            public bool Contains(Vector2Int cell)
            {
                return cell.x >= minX && cell.x <= maxX && cell.y >= minZ && cell.y <= maxZ;
            }
        }

        private sealed class TreeTemplate
        {
            public readonly Mesh Mesh;
            public readonly int[] SubMeshBySlot;
            public readonly Vector3 HalfExtents;

            public TreeTemplate(Mesh mesh, int[] subMeshBySlot, Vector3 halfExtents)
            {
                Mesh = mesh;
                SubMeshBySlot = subMeshBySlot;
                HalfExtents = halfExtents;
            }
        }

        private readonly struct ForestTreeInstance
        {
            public readonly TreeTemplate Template;
            public readonly Matrix4x4 Transform;

            public ForestTreeInstance(TreeTemplate template, Matrix4x4 transform)
            {
                Template = template;
                Transform = transform;
            }
        }

        private sealed class ForestChunk
        {
            private readonly Transform root;
            private readonly Material[] palette;
            private readonly Vector2Int key;
            private readonly List<CombineInstance>[] combinations =
            {
                new List<CombineInstance>(128),
                new List<CombineInstance>(128),
                new List<CombineInstance>(128)
            };
            private readonly Mesh[] meshes = new Mesh[3];
            private readonly MeshFilter[] filters = new MeshFilter[3];
            private readonly MeshRenderer[] renderers = new MeshRenderer[3];

            public ForestChunk(Transform parent, Material[] sourcePalette, Vector2Int chunkKey)
            {
                root = new GameObject("Wald Chunk " + chunkKey.x + "-" + chunkKey.y).transform;
                root.SetParent(parent, false);
                palette = sourcePalette;
                key = chunkKey;
                for (int i = 0; i < ForestSlots.Length; i++)
                {
                    var part = new GameObject(ForestSlots[i].ToString());
                    part.transform.SetParent(root, false);
                    filters[i] = part.AddComponent<MeshFilter>();
                    renderers[i] = part.AddComponent<MeshRenderer>();
                    int materialIndex = VoxelPalette.ToMaterialIndex(ForestSlots[i]);
                    renderers[i].sharedMaterial = materialIndex >= 0 && materialIndex < palette.Length
                        ? palette[materialIndex]
                        : null;
                    renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                    renderers[i].receiveShadows = false;
                    meshes[i] = new Mesh { name = "Wald Chunk Mesh " + key.x + "-" + key.y + " " + i };
                    meshes[i].indexFormat = IndexFormat.UInt32;
                    filters[i].sharedMesh = meshes[i];
                }
            }

            public void Rebuild(List<ForestTreeInstance> trees)
            {
                for (int i = 0; i < combinations.Length; i++) combinations[i].Clear();
                for (int treeIndex = 0; treeIndex < trees.Count; treeIndex++)
                {
                    ForestTreeInstance tree = trees[treeIndex];
                    for (int slotIndex = 0; slotIndex < ForestSlots.Length; slotIndex++)
                    {
                        int subMesh = tree.Template.SubMeshBySlot[(int)ForestSlots[slotIndex]];
                        if (subMesh < 0) continue;
                        combinations[slotIndex].Add(new CombineInstance
                        {
                            mesh = tree.Template.Mesh,
                            subMeshIndex = subMesh,
                            transform = tree.Transform
                        });
                    }
                }

                for (int i = 0; i < meshes.Length; i++)
                {
                    meshes[i].Clear();
                    if (combinations[i].Count > 0)
                    {
                        meshes[i].CombineMeshes(combinations[i].ToArray(), true, true, false);
                        renderers[i].enabled = true;
                    }
                    else
                    {
                        renderers[i].enabled = false;
                    }
                }
            }

            public void Dispose()
            {
                for (int i = 0; i < meshes.Length; i++)
                {
                    if (meshes[i] != null) UnityEngine.Object.Destroy(meshes[i]);
                }
                if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            }
        }
    }
}
