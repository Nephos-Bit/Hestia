using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WinterMarket
{
    /// <summary>
    /// Reusable managed buffers for allocation-free repeated mesh rebuilds after warm-up.
    /// A buffer instance must not be used concurrently.
    /// </summary>
    public sealed class VoxelMeshBuffers
    {
        internal readonly List<Vector3> Vertices = new List<Vector3>(256);
        internal readonly List<Vector3> Normals = new List<Vector3>(256);
        internal readonly List<Vector2> Uvs = new List<Vector2>(256);
        internal readonly List<int>[] Triangles = CreateTriangleLists();
        internal readonly int[] Dimensions = new int[3];
        internal readonly int[] Position = new int[3];
        internal readonly int[] DeltaU = new int[3];
        internal readonly int[] DeltaV = new int[3];
        internal int[] Mask = Array.Empty<int>();

        internal void Clear()
        {
            Vertices.Clear();
            Normals.Clear();
            Uvs.Clear();
            for (int i = 0; i < Triangles.Length; i++)
            {
                Triangles[i].Clear();
            }
        }

        internal void EnsureMaskCapacity(int count)
        {
            if (Mask.Length < count)
            {
                Mask = new int[count];
            }
        }

        private static List<int>[] CreateTriangleLists()
        {
            var lists = new List<int>[VoxelPalette.SlotCount];
            for (int i = 0; i < lists.Length; i++)
            {
                lists[i] = new List<int>(128);
            }

            return lists;
        }
    }

    /// <summary>
    /// Builds a surface mesh using three-axis greedy rectangle merging.
    /// Internal faces are omitted and only coplanar faces with the same slot and direction merge.
    /// </summary>
    public static class VoxelGreedyMesher
    {
        public static Mesh CreateMesh(
            VoxelVolume volume,
            string meshName = "Voxel Mesh",
            List<VoxelMaterialSlot> usedSlots = null,
            VoxelMeshBuffers buffers = null)
        {
            var mesh = new Mesh
            {
                name = string.IsNullOrWhiteSpace(meshName) ? "Voxel Mesh" : meshName
            };
            BuildMesh(volume, mesh, usedSlots, buffers);
            return mesh;
        }

        /// <summary>
        /// Replaces targetMesh contents. usedSlots receives the material slot for each submesh,
        /// in submesh order. Supplying reusable buffers avoids managed allocations after warm-up.
        /// </summary>
        public static void BuildMesh(
            VoxelVolume volume,
            Mesh targetMesh,
            List<VoxelMaterialSlot> usedSlots = null,
            VoxelMeshBuffers buffers = null)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            if (targetMesh == null)
            {
                throw new ArgumentNullException(nameof(targetMesh));
            }

            buffers ??= new VoxelMeshBuffers();
            buffers.Clear();
            usedSlots?.Clear();

            Vector3Int size = volume.Size;
            int[] dimensions = buffers.Dimensions;
            dimensions[0] = size.x;
            dimensions[1] = size.y;
            dimensions[2] = size.z;

            for (int axis = 0; axis < 3; axis++)
            {
                BuildAxis(volume, axis, dimensions, buffers);
            }

            ApplyToMesh(targetMesh, usedSlots, buffers);
        }

        private static void BuildAxis(
            VoxelVolume volume,
            int axis,
            int[] dimensions,
            VoxelMeshBuffers buffers)
        {
            int axisU = (axis + 1) % 3;
            int axisV = (axis + 2) % 3;
            int maskWidth = dimensions[axisU];
            int maskHeight = dimensions[axisV];
            int maskLength = maskWidth * maskHeight;
            if (maskLength == 0 || dimensions[axis] == 0)
            {
                return;
            }

            buffers.EnsureMaskCapacity(maskLength);
            int[] mask = buffers.Mask;
            int[] position = buffers.Position;
            position[0] = 0;
            position[1] = 0;
            position[2] = 0;
            position[axis] = -1;

            while (position[axis] < dimensions[axis])
            {
                int maskIndex = 0;
                for (position[axisV] = 0; position[axisV] < dimensions[axisV]; position[axisV]++)
                {
                    for (position[axisU] = 0; position[axisU] < dimensions[axisU]; position[axisU]++)
                    {
                        VoxelMaterialSlot back = position[axis] >= 0
                            ? GetVoxel(volume, position)
                            : VoxelMaterialSlot.Empty;
                        VoxelMaterialSlot front = position[axis] < dimensions[axis] - 1
                            ? GetVoxelOffset(volume, position, axis)
                            : VoxelMaterialSlot.Empty;

                        bool backSolid = IsSolid(back);
                        bool frontSolid = IsSolid(front);
                        mask[maskIndex++] = backSolid == frontSolid
                            ? 0
                            : backSolid ? (int)back : -(int)front;
                    }
                }

                position[axis]++;
                GreedyMergeMask(axis, axisU, axisV, maskWidth, maskHeight, position, buffers);
            }
        }

        private static void GreedyMergeMask(
            int axis,
            int axisU,
            int axisV,
            int maskWidth,
            int maskHeight,
            int[] position,
            VoxelMeshBuffers buffers)
        {
            int[] mask = buffers.Mask;
            int maskIndex = 0;

            for (int j = 0; j < maskHeight; j++)
            {
                for (int i = 0; i < maskWidth;)
                {
                    int face = mask[maskIndex];
                    if (face == 0)
                    {
                        i++;
                        maskIndex++;
                        continue;
                    }

                    int width = 1;
                    while (i + width < maskWidth && mask[maskIndex + width] == face)
                    {
                        width++;
                    }

                    int height = 1;
                    bool canGrow = true;
                    while (j + height < maskHeight && canGrow)
                    {
                        int row = maskIndex + height * maskWidth;
                        for (int k = 0; k < width; k++)
                        {
                            if (mask[row + k] != face)
                            {
                                canGrow = false;
                                break;
                            }
                        }

                        if (canGrow)
                        {
                            height++;
                        }
                    }

                    position[axisU] = i;
                    position[axisV] = j;
                    AddQuad(axis, axisU, axisV, position, width, height, face, buffers);

                    for (int y = 0; y < height; y++)
                    {
                        int row = maskIndex + y * maskWidth;
                        for (int x = 0; x < width; x++)
                        {
                            mask[row + x] = 0;
                        }
                    }

                    i += width;
                    maskIndex += width;
                }
            }
        }

        private static void AddQuad(
            int axis,
            int axisU,
            int axisV,
            int[] position,
            int width,
            int height,
            int signedSlot,
            VoxelMeshBuffers buffers)
        {
            int[] deltaU = buffers.DeltaU;
            int[] deltaV = buffers.DeltaV;
            deltaU[0] = 0;
            deltaU[1] = 0;
            deltaU[2] = 0;
            deltaV[0] = 0;
            deltaV[1] = 0;
            deltaV[2] = 0;
            deltaU[axisU] = width;
            deltaV[axisV] = height;

            Vector3 origin = ToWorld(position);
            Vector3 u = ToWorld(deltaU);
            Vector3 v = ToWorld(deltaV);
            bool positive = signedSlot > 0;
            Vector3 normal = Vector3.zero;
            normal[axis] = positive ? 1f : -1f;

            int firstVertex = buffers.Vertices.Count;
            if (positive)
            {
                buffers.Vertices.Add(origin);
                buffers.Vertices.Add(origin + u);
                buffers.Vertices.Add(origin + u + v);
                buffers.Vertices.Add(origin + v);
            }
            else
            {
                buffers.Vertices.Add(origin);
                buffers.Vertices.Add(origin + v);
                buffers.Vertices.Add(origin + u + v);
                buffers.Vertices.Add(origin + u);
            }

            for (int i = 0; i < 4; i++)
            {
                buffers.Normals.Add(normal);
            }

            buffers.Uvs.Add(Vector2.zero);
            if (positive)
            {
                buffers.Uvs.Add(new Vector2(width, 0f));
                buffers.Uvs.Add(new Vector2(width, height));
                buffers.Uvs.Add(new Vector2(0f, height));
            }
            else
            {
                buffers.Uvs.Add(new Vector2(0f, height));
                buffers.Uvs.Add(new Vector2(width, height));
                buffers.Uvs.Add(new Vector2(width, 0f));
            }

            int materialIndex = Math.Abs(signedSlot) - 1;
            List<int> triangles = buffers.Triangles[materialIndex];
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 3);
        }

        private static void ApplyToMesh(
            Mesh mesh,
            List<VoxelMaterialSlot> usedSlots,
            VoxelMeshBuffers buffers)
        {
            mesh.Clear(false);
            mesh.indexFormat = buffers.Vertices.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            mesh.SetVertices(buffers.Vertices);
            mesh.SetNormals(buffers.Normals);
            mesh.SetUVs(0, buffers.Uvs);

            int subMeshCount = 0;
            for (int i = 0; i < buffers.Triangles.Length; i++)
            {
                if (buffers.Triangles[i].Count > 0)
                {
                    subMeshCount++;
                }
            }

            mesh.subMeshCount = subMeshCount;
            int subMesh = 0;
            for (int i = 0; i < buffers.Triangles.Length; i++)
            {
                List<int> triangles = buffers.Triangles[i];
                if (triangles.Count == 0)
                {
                    continue;
                }

                mesh.SetTriangles(triangles, subMesh++, false);
                usedSlots?.Add((VoxelMaterialSlot)(i + 1));
            }

            if (buffers.Vertices.Count == 0)
            {
                mesh.bounds = new Bounds(Vector3.zero, Vector3.zero);
            }
            else
            {
                mesh.RecalculateBounds();
            }
        }

        private static VoxelMaterialSlot GetVoxel(VoxelVolume volume, int[] position)
        {
            return volume.GetVoxelUnchecked(position[0], position[1], position[2]);
        }

        private static VoxelMaterialSlot GetVoxelOffset(VoxelVolume volume, int[] position, int axis)
        {
            int x = position[0];
            int y = position[1];
            int z = position[2];
            if (axis == 0)
            {
                x++;
            }
            else if (axis == 1)
            {
                y++;
            }
            else
            {
                z++;
            }

            return volume.GetVoxelUnchecked(x, y, z);
        }

        private static bool IsSolid(VoxelMaterialSlot slot)
        {
            int value = (int)slot;
            return value >= (int)VoxelMaterialSlot.Slot0
                && value <= (int)VoxelMaterialSlot.Slot15;
        }

        private static Vector3 ToWorld(int[] cellPosition)
        {
            return new Vector3(
                cellPosition[0] * VoxelVolume.GridSize,
                cellPosition[1] * VoxelVolume.GridSize,
                cellPosition[2] * VoxelVolume.GridSize);
        }
    }
}
