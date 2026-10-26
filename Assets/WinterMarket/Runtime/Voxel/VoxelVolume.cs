using System;
using UnityEngine;

namespace WinterMarket
{
    /// <summary>
    /// Compact, serializable voxel occupancy grid. One byte is stored per 0.15 metre cell.
    /// </summary>
    [Serializable]
    public sealed class VoxelVolume : ISerializationCallbackReceiver
    {
        public const float GridSize = 0.15f;

        [SerializeField]
        private Vector3Int size = Vector3Int.one;

        [SerializeField]
        private byte[] voxels = new byte[1];

        public VoxelVolume()
        {
        }

        public VoxelVolume(Vector3Int size)
        {
            Resize(size, false);
        }

        public Vector3Int Size => size;
        public int Width => size.x;
        public int Height => size.y;
        public int Depth => size.z;
        public int CellCount => voxels != null ? voxels.Length : 0;

        public VoxelMaterialSlot this[int x, int y, int z]
        {
            get => GetVoxel(x, y, z);
            set => SetVoxel(x, y, z, value);
        }

        public bool IsInside(int x, int y, int z)
        {
            return (uint)x < (uint)size.x
                && (uint)y < (uint)size.y
                && (uint)z < (uint)size.z;
        }

        /// <summary>Returns Empty for coordinates outside the volume.</summary>
        public VoxelMaterialSlot GetVoxel(int x, int y, int z)
        {
            EnsureStorage();
            return IsInside(x, y, z)
                ? (VoxelMaterialSlot)voxels[ToIndexUnchecked(x, y, z)]
                : VoxelMaterialSlot.Empty;
        }

        /// <summary>Sets a cell and returns false when the coordinate is outside the volume.</summary>
        public bool SetVoxel(int x, int y, int z, VoxelMaterialSlot slot)
        {
            ValidateSlot(slot);
            EnsureStorage();
            if (!IsInside(x, y, z))
            {
                return false;
            }

            voxels[ToIndexUnchecked(x, y, z)] = (byte)slot;
            return true;
        }

        public bool SetVoxel(Vector3Int position, VoxelMaterialSlot slot)
        {
            return SetVoxel(position.x, position.y, position.z, slot);
        }

        /// <summary>
        /// Fills the intersection of a cuboid and this volume. The size is measured in cells.
        /// Returns the number of cells written.
        /// </summary>
        public int FillCuboid(Vector3Int minInclusive, Vector3Int cuboidSize, VoxelMaterialSlot slot)
        {
            ValidateSlot(slot);
            EnsureStorage();
            if (cuboidSize.x <= 0 || cuboidSize.y <= 0 || cuboidSize.z <= 0)
            {
                return 0;
            }

            int minX = Mathf.Clamp(minInclusive.x, 0, size.x);
            int minY = Mathf.Clamp(minInclusive.y, 0, size.y);
            int minZ = Mathf.Clamp(minInclusive.z, 0, size.z);
            int maxX = ClampToDimension((long)minInclusive.x + cuboidSize.x, size.x);
            int maxY = ClampToDimension((long)minInclusive.y + cuboidSize.y, size.y);
            int maxZ = ClampToDimension((long)minInclusive.z + cuboidSize.z, size.z);

            if (maxX <= minX || maxY <= minY || maxZ <= minZ)
            {
                return 0;
            }

            byte value = (byte)slot;
            for (int z = minZ; z < maxZ; z++)
            {
                for (int y = minY; y < maxY; y++)
                {
                    int index = ToIndexUnchecked(minX, y, z);
                    int end = index + (maxX - minX);
                    for (; index < end; index++)
                    {
                        voxels[index] = value;
                    }
                }
            }

            return (maxX - minX) * (maxY - minY) * (maxZ - minZ);
        }

        public int FillCuboid(
            int x,
            int y,
            int z,
            int width,
            int height,
            int depth,
            VoxelMaterialSlot slot)
        {
            return FillCuboid(
                new Vector3Int(x, y, z),
                new Vector3Int(width, height, depth),
                slot);
        }

        public void Clear()
        {
            EnsureStorage();
            Array.Clear(voxels, 0, voxels.Length);
        }

        /// <summary>Resizes the grid, optionally preserving cells inside the overlapping region.</summary>
        public void Resize(Vector3Int newSize, bool preserveContents = true)
        {
            ValidateSize(newSize);
            int newCount = GetCellCount(newSize);
            byte[] resized = new byte[newCount];

            if (preserveContents && voxels != null)
            {
                int copyX = Math.Min(size.x, newSize.x);
                int copyY = Math.Min(size.y, newSize.y);
                int copyZ = Math.Min(size.z, newSize.z);
                for (int z = 0; z < copyZ; z++)
                {
                    for (int y = 0; y < copyY; y++)
                    {
                        int oldIndex = (z * size.y + y) * size.x;
                        int newIndex = (z * newSize.y + y) * newSize.x;
                        Array.Copy(voxels, oldIndex, resized, newIndex, copyX);
                    }
                }
            }

            size = newSize;
            voxels = resized;
        }

        public void OnBeforeSerialize()
        {
            EnsureStorage();
        }

        public void OnAfterDeserialize()
        {
            if (size.x < 0 || size.y < 0 || size.z < 0)
            {
                size = new Vector3Int(
                    Math.Max(0, size.x),
                    Math.Max(0, size.y),
                    Math.Max(0, size.z));
            }

            EnsureStorage();
            for (int i = 0; i < voxels.Length; i++)
            {
                if (voxels[i] > (byte)VoxelMaterialSlot.Slot15)
                {
                    voxels[i] = (byte)VoxelMaterialSlot.Empty;
                }
            }
        }

        internal VoxelMaterialSlot GetVoxelUnchecked(int x, int y, int z)
        {
            return (VoxelMaterialSlot)voxels[ToIndexUnchecked(x, y, z)];
        }

        private int ToIndexUnchecked(int x, int y, int z)
        {
            return (z * size.y + y) * size.x + x;
        }

        private void EnsureStorage()
        {
            int expected = GetCellCount(size);
            if (voxels != null && voxels.Length == expected)
            {
                return;
            }

            byte[] repaired = new byte[expected];
            if (voxels != null)
            {
                Array.Copy(voxels, repaired, Math.Min(voxels.Length, repaired.Length));
            }

            voxels = repaired;
        }

        private static int ClampToDimension(long value, int dimension)
        {
            if (value <= 0)
            {
                return 0;
            }

            return value >= dimension ? dimension : (int)value;
        }

        private static int GetCellCount(Vector3Int dimensions)
        {
            ValidateSize(dimensions);
            if (dimensions.x == 0 || dimensions.y == 0 || dimensions.z == 0)
            {
                return 0;
            }

            long area = (long)dimensions.x * dimensions.y;
            if (area > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimensions),
                    dimensions,
                    "Voxel volume is too large for a managed array.");
            }

            long count = area * dimensions.z;
            if (count > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimensions),
                    dimensions,
                    "Voxel volume is too large for a managed array.");
            }

            return (int)count;
        }

        private static void ValidateSize(Vector3Int dimensions)
        {
            if (dimensions.x < 0 || dimensions.y < 0 || dimensions.z < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimensions),
                    dimensions,
                    "Voxel dimensions cannot be negative.");
            }
        }

        private static void ValidateSlot(VoxelMaterialSlot slot)
        {
            if ((byte)slot > (byte)VoxelMaterialSlot.Slot15)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown voxel material slot.");
            }
        }
    }
}
