using System;
using UnityEngine;

namespace WinterMarket
{
    /// <summary>
    /// Material identifier stored by a voxel. Empty is not a renderable palette slot.
    /// Material arrays use zero-based indexing: Slot0 maps to materials[0].
    /// </summary>
    [Serializable]
    public enum VoxelMaterialSlot : byte
    {
        Empty = 0,
        Slot0 = 1,
        Slot1 = 2,
        Slot2 = 3,
        Slot3 = 4,
        Slot4 = 5,
        Slot5 = 6,
        Slot6 = 7,
        Slot7 = 8,
        Slot8 = 9,
        Slot9 = 10,
        Slot10 = 11,
        Slot11 = 12,
        Slot12 = 13,
        Slot13 = 14,
        Slot14 = 15,
        Slot15 = 16
    }

    /// <summary>
    /// Serializable material palette. Entries map directly to Slot0 through Slot15.
    /// </summary>
    [Serializable]
    public sealed class VoxelPalette
    {
        public const int SlotCount = 16;

        [SerializeField]
        private Material[] materials = new Material[SlotCount];

        public Material[] Materials => materials;

        public Material GetMaterial(VoxelMaterialSlot slot)
        {
            int index = ToMaterialIndex(slot);
            return index >= 0 && materials != null && index < materials.Length
                ? materials[index]
                : null;
        }

        public void SetMaterial(VoxelMaterialSlot slot, Material material)
        {
            int index = ToMaterialIndex(slot);
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Empty is not a material slot.");
            }

            EnsureCapacity();
            materials[index] = material;
        }

        public static int ToMaterialIndex(VoxelMaterialSlot slot)
        {
            int value = (int)slot;
            return value >= (int)VoxelMaterialSlot.Slot0 && value <= (int)VoxelMaterialSlot.Slot15
                ? value - 1
                : -1;
        }

        private void EnsureCapacity()
        {
            if (materials != null && materials.Length == SlotCount)
            {
                return;
            }

            Material[] resized = new Material[SlotCount];
            if (materials != null)
            {
                Array.Copy(materials, resized, Math.Min(materials.Length, resized.Length));
            }

            materials = resized;
        }
    }
}
