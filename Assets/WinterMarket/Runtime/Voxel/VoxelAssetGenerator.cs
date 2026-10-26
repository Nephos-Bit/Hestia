using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterMarket
{
    /// <summary>
    /// Owns one generated mesh and binds its used palette slots to one MeshRenderer.
    /// Material array entry zero corresponds to VoxelMaterialSlot.Slot0.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class VoxelAssetGenerator : MonoBehaviour
    {
        [SerializeField]
        private VoxelVolume volume = new VoxelVolume(Vector3Int.one);

        [SerializeField]
        private Material[] materials = new Material[VoxelPalette.SlotCount];

        [NonSerialized]
        private Mesh ownedMesh;

        [NonSerialized]
        private VoxelMeshBuffers meshBuffers;

        [NonSerialized]
        private List<VoxelMaterialSlot> usedSlots;

        [NonSerialized]
        private Material[] usedMaterials;

        public VoxelVolume Volume => volume;
        public Material[] Materials => materials;
        public Mesh GeneratedMesh => ownedMesh;

        /// <summary>
        /// Creates a GameObject containing exactly one MeshFilter, one MeshRenderer,
        /// and this generator component. The returned component owns the generated mesh.
        /// </summary>
        public static VoxelAssetGenerator Create(
            string objectName,
            VoxelVolume sourceVolume,
            Material[] paletteMaterials,
            Transform parent = null,
            bool markStatic = false)
        {
            if (sourceVolume == null)
            {
                throw new ArgumentNullException(nameof(sourceVolume));
            }

            string safeName = string.IsNullOrWhiteSpace(objectName) ? "Voxel Asset" : objectName;
            var gameObject = new GameObject(safeName);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            gameObject.isStatic = markStatic;
            gameObject.AddComponent<MeshFilter>();
            gameObject.AddComponent<MeshRenderer>();
            VoxelAssetGenerator generator = gameObject.AddComponent<VoxelAssetGenerator>();
            generator.SetData(sourceVolume, paletteMaterials);
            generator.Rebuild();
            return generator;
        }

        public static VoxelAssetGenerator Create(
            string objectName,
            VoxelVolume sourceVolume,
            VoxelPalette palette,
            Transform parent = null,
            bool markStatic = false)
        {
            return Create(
                objectName,
                sourceVolume,
                palette != null ? palette.Materials : null,
                parent,
                markStatic);
        }

        /// <summary>Replaces the source references without rebuilding.</summary>
        public void SetData(VoxelVolume sourceVolume, Material[] paletteMaterials)
        {
            volume = sourceVolume ?? throw new ArgumentNullException(nameof(sourceVolume));
            materials = paletteMaterials ?? Array.Empty<Material>();
        }

        public void SetData(VoxelVolume sourceVolume, VoxelPalette palette)
        {
            SetData(sourceVolume, palette != null ? palette.Materials : null);
        }

        /// <summary>Rebuilds the owned mesh and updates renderer material/submesh alignment.</summary>
        [ContextMenu("Rebuild Voxel Mesh")]
        public void Rebuild()
        {
            if (volume == null)
            {
                throw new InvalidOperationException("A VoxelVolume is required before rebuilding.");
            }

            EnsureRuntimeState();
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

            if (ownedMesh == null)
            {
                ownedMesh = new Mesh
                {
                    name = $"{gameObject.name} Voxel Mesh"
                };
            }

            VoxelGreedyMesher.BuildMesh(volume, ownedMesh, usedSlots, meshBuffers);
            meshFilter.sharedMesh = ownedMesh;
            ApplyUsedMaterials(meshRenderer);
        }

        /// <summary>
        /// Releases the generated mesh. Source volume and material references are retained.
        /// </summary>
        public void ReleaseMesh()
        {
            if (ownedMesh == null)
            {
                return;
            }

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh == ownedMesh)
            {
                meshFilter.sharedMesh = null;
            }

            DestroyOwnedObject(ownedMesh);
            ownedMesh = null;
        }

        private void OnDestroy()
        {
            ReleaseMesh();
        }

        private void EnsureRuntimeState()
        {
            meshBuffers ??= new VoxelMeshBuffers();
            usedSlots ??= new List<VoxelMaterialSlot>(VoxelPalette.SlotCount);
        }

        private void ApplyUsedMaterials(MeshRenderer meshRenderer)
        {
            int count = usedSlots.Count;
            if (usedMaterials == null || usedMaterials.Length != count)
            {
                usedMaterials = new Material[count];
            }

            for (int i = 0; i < count; i++)
            {
                int materialIndex = VoxelPalette.ToMaterialIndex(usedSlots[i]);
                usedMaterials[i] = materialIndex >= 0
                    && materials != null
                    && materialIndex < materials.Length
                        ? materials[materialIndex]
                        : null;
            }

            meshRenderer.sharedMaterials = usedMaterials;
        }

        private static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
