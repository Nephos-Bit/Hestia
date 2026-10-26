using UnityEngine;
using UnityEngine.Rendering;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class NPCFactory : MonoBehaviour
    {
        [Header("Shared materials")]
        [SerializeField] private Material skinMaterial;
        [SerializeField] private Material trousersMaterial;
        [SerializeField] private Material bootsMaterial;
        [SerializeField] private Material crateMaterial;
        [SerializeField] private Material[] coatMaterials = new Material[5];
        [SerializeField] private Material[] accentMaterials = new Material[5];

        [Header("Scale")]
        [SerializeField, Min(0.1f)] private float personScale = 1f;

        public void ConfigureMaterials(Material[] palette)
        {
            if (palette == null || palette.Length < 15)
            {
                return;
            }

            skinMaterial = palette[10];
            trousersMaterial = palette[7];
            bootsMaterial = palette[13];
            crateMaterial = palette[2];
            coatMaterials = new[] { palette[11], palette[12], palette[4], palette[5], palette[9] };
            accentMaterials = new[] { palette[4], palette[5], palette[12], palette[9], palette[14] };
        }

        public NPCBehaviour BuildNPC(Transform parent)
        {
            GameObject root = new GameObject("Pooled NPC");
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * personScale;

            NPCBehaviour behaviour = root.AddComponent<NPCBehaviour>();
            NPCVisual visual = root.AddComponent<NPCVisual>();

            Transform bodyPivot = CreatePivot("Body", root.transform, new Vector3(0f, 1.02f, 0f));
            Renderer torso = CreateVoxelBox(
                "Coat",
                bodyPivot,
                new Vector3(0f, 0.06f, 0f),
                new Vector3Int(3, 5, 2),
                skinMaterial);

            Transform head = CreateVoxelBox(
                "Head",
                bodyPivot,
                new Vector3(0f, 0.63f, 0f),
                new Vector3Int(2, 2, 2),
                skinMaterial).transform;

            Transform leftArmPivot = CreatePivot("Left Arm", bodyPivot, new Vector3(-0.3f, 0.28f, 0f));
            Renderer leftArm = CreateVoxelBox(
                "Left Sleeve",
                leftArmPivot,
                new Vector3(0f, -0.225f, 0f),
                new Vector3Int(1, 4, 1),
                skinMaterial);

            Transform rightArmPivot = CreatePivot("Right Arm", bodyPivot, new Vector3(0.3f, 0.28f, 0f));
            Renderer rightArm = CreateVoxelBox(
                "Right Sleeve",
                rightArmPivot,
                new Vector3(0f, -0.225f, 0f),
                new Vector3Int(1, 4, 1),
                skinMaterial);

            Transform leftLegPivot = CreatePivot("Left Leg", bodyPivot, new Vector3(-0.12f, -0.28f, 0f));
            Renderer leftLeg = CreateVoxelBox(
                "Left Boot",
                leftLegPivot,
                new Vector3(0f, -0.3f, 0.03f),
                new Vector3Int(1, 4, 2),
                bootsMaterial);

            Transform rightLegPivot = CreatePivot("Right Leg", bodyPivot, new Vector3(0.12f, -0.28f, 0f));
            Renderer rightLeg = CreateVoxelBox(
                "Right Boot",
                rightLegPivot,
                new Vector3(0f, -0.3f, 0.03f),
                new Vector3Int(1, 4, 2),
                bootsMaterial);

            Renderer hat = CreateVoxelBox(
                "Winter Hat",
                bodyPivot,
                new Vector3(0f, 0.87f, 0f),
                new Vector3Int(2, 1, 2),
                skinMaterial);
            Renderer hatBrim = CreateVoxelBox(
                "Hat Brim",
                bodyPivot,
                new Vector3(0f, 0.78f, 0f),
                new Vector3Int(3, 1, 3),
                skinMaterial);
            Renderer scarf = CreateVoxelBox(
                "Scarf",
                bodyPivot,
                new Vector3(0.03f, 0.42f, 0.03f),
                new Vector3Int(4, 1, 3),
                skinMaterial);

            Renderer crate = CreateVoxelBox(
                "Crate",
                rightArmPivot,
                new Vector3(0.18f, -0.48f, 0.12f),
                new Vector3Int(3, 2, 3),
                crateMaterial);
            crate.gameObject.SetActive(false);

            visual.Configure(
                this,
                torso,
                leftArm,
                rightArm,
                leftLeg,
                rightLeg,
                head.GetComponent<Renderer>(),
                hat,
                hatBrim,
                scarf,
                crate);
            behaviour.ConfigureRig(
                visual,
                bodyPivot,
                head,
                leftArmPivot,
                rightArmPivot,
                leftLegPivot,
                rightLegPivot,
                crate.transform);
            behaviour.Deactivate();
            return behaviour;
        }

        internal void ApplyRoleMaterials(NPCVisual target, NPCRole role)
        {
            int index = (int)role;
            Material coat = GetMaterial(coatMaterials, index);
            Material accent = GetMaterial(accentMaterials, index);
            target.ApplyMaterials(
                coat,
                accent,
                skinMaterial,
                trousersMaterial,
                bootsMaterial,
                crateMaterial);
        }

        private static Transform CreatePivot(string objectName, Transform parent, Vector3 localPosition)
        {
            GameObject pivot = new GameObject(objectName);
            Transform pivotTransform = pivot.transform;
            pivotTransform.SetParent(parent, false);
            pivotTransform.localPosition = localPosition;
            return pivotTransform;
        }

        private static Renderer CreateVoxelBox(
            string objectName,
            Transform parent,
            Vector3 localCenter,
            Vector3Int cells,
            Material material)
        {
            Material usable = material;
            if (usable == null)
            {
                Material[] fallback = WinterMarketMaterialLibrary.LoadPalette();
                usable = fallback[10];
            }

            var volume = new VoxelVolume(cells);
            volume.FillCuboid(0, 0, 0, cells.x, cells.y, cells.z, VoxelMaterialSlot.Slot0);
            VoxelAssetGenerator asset = VoxelAssetGenerator.Create(
                objectName,
                volume,
                new[] { usable },
                parent,
                false);
            Vector3 size = new Vector3(cells.x, cells.y, cells.z) * VoxelVolume.GridSize;
            asset.transform.localPosition = localCenter - size * 0.5f;
            MeshRenderer renderer = asset.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }

        private static Material GetMaterial(Material[] materials, int index)
        {
            if (materials == null || materials.Length == 0)
            {
                return null;
            }

            return materials[Mathf.Abs(index) % materials.Length];
        }
    }

    internal sealed class NPCVisual : MonoBehaviour
    {
        private NPCFactory owner;
        private Renderer torso;
        private Renderer leftArm;
        private Renderer rightArm;
        private Renderer leftLeg;
        private Renderer rightLeg;
        private Renderer head;
        private Renderer hat;
        private Renderer hatBrim;
        private Renderer scarf;
        private Renderer crate;

        internal void Configure(
            NPCFactory factory,
            Renderer torsoRenderer,
            Renderer leftArmRenderer,
            Renderer rightArmRenderer,
            Renderer leftLegRenderer,
            Renderer rightLegRenderer,
            Renderer headRenderer,
            Renderer hatRenderer,
            Renderer brimRenderer,
            Renderer scarfRenderer,
            Renderer crateRenderer)
        {
            owner = factory;
            torso = torsoRenderer;
            leftArm = leftArmRenderer;
            rightArm = rightArmRenderer;
            leftLeg = leftLegRenderer;
            rightLeg = rightLegRenderer;
            head = headRenderer;
            hat = hatRenderer;
            hatBrim = brimRenderer;
            scarf = scarfRenderer;
            crate = crateRenderer;
        }

        internal void ApplyRole(NPCRole role)
        {
            owner?.ApplyRoleMaterials(this, role);
        }

        internal void ApplyMaterials(
            Material coat,
            Material accent,
            Material skin,
            Material trousers,
            Material boots,
            Material crateMaterial)
        {
            SetMaterial(torso, coat);
            SetMaterial(leftArm, coat);
            SetMaterial(rightArm, coat);
            SetMaterial(leftLeg, trousers != null ? trousers : boots);
            SetMaterial(rightLeg, trousers != null ? trousers : boots);
            SetMaterial(head, skin);
            SetMaterial(hat, accent);
            SetMaterial(hatBrim, accent);
            SetMaterial(scarf, accent);
            SetMaterial(crate, crateMaterial);
        }

        private static void SetMaterial(Renderer target, Material material)
        {
            if (target != null && material != null)
            {
                target.sharedMaterial = material;
            }
        }
    }
}
