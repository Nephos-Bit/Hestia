using UnityEngine;

namespace WinterMarket
{
    internal static class WinterMarketMaterialLibrary
    {
        private const string ResourceFolder = "WinterMarketMaterials/";

        public static Material[] LoadPalette()
        {
            Shader lit = FindLitShader();
            var palette = new Material[VoxelPalette.SlotCount];
            palette[0] = LoadOrCreate("Snow", "Snow", lit, WinterPalette.Snow, 0.02f);
            palette[1] = LoadOrCreate("SnowShadow", "Snow Shadow", lit, WinterPalette.SnowShadow, 0.01f);
            palette[2] = LoadOrCreate("LightWood", "Light Wood", lit, WinterPalette.LightWood, 0.05f);
            palette[3] = LoadOrCreate("DarkWood", "Dark Wood", lit, WinterPalette.DarkWood, 0.04f);
            palette[4] = LoadOrCreate("MutedRed", "Muted Red", lit, WinterPalette.ChristmasRed, 0.03f);
            palette[5] = LoadOrCreate("FirGreen", "Fir Green", lit, WinterPalette.FirGreen, 0.02f);
            palette[6] = LoadOrCreate("WarmEmission", "Warm Emission", lit, WinterPalette.Lantern, 0.08f, true);
            palette[7] = LoadOrCreate("NightBlue", "Night Blue", lit, WinterPalette.Night, 0.03f);
            palette[8] = LoadOrCreate("SnowPath", "Snow Path", lit, WinterPalette.Path, 0.01f);
            palette[9] = LoadOrCreate("BlueGreyRoof", "Blue Grey Roof", lit, WinterPalette.RoofBlue, 0.03f);
            palette[10] = LoadOrCreate("Skin", "Skin", lit, WinterPalette.Skin, 0.02f);
            palette[11] = LoadOrCreate("CoatBlue", "Coat Blue", lit, WinterPalette.CoatBlue, 0.02f);
            palette[12] = LoadOrCreate("CoatOchre", "Coat Ochre", lit, WinterPalette.CoatOchre, 0.02f);
            palette[13] = LoadOrCreate("Metal", "Metal", lit, new Color(0.22f, 0.25f, 0.27f), 0.26f, false, 0.28f);
            palette[14] = LoadOrCreate("Cream", "Cream", lit, new Color(0.91f, 0.87f, 0.8f), 0.03f);
            palette[15] = LoadOrCreate("DeepGreen", "Deep Green", lit, new Color(0.19f, 0.28f, 0.23f), 0.02f);
            return palette;
        }

        public static Material LoadSnowParticles()
        {
            Material source = Resources.Load<Material>(ResourceFolder + "SnowParticles");
            if (source != null && source.shader != null)
            {
                return new Material(source) { name = "Snow Particles (Runtime)" };
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Sprites/Default");
            var material = shader != null
                ? new Material(shader)
                : new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Unlit/Color"));
            material.name = "Snow Particles (Runtime)";
            SetColor(material, new Color(1f, 1f, 1f, 0.75f));
            return material;
        }

        public static void DestroyOwned(Material[] materials)
        {
            if (materials == null)
            {
                return;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                DestroyOwned(materials[i]);
            }
        }

        public static void DestroyOwned(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static Material LoadOrCreate(
            string resourceName,
            string materialName,
            Shader shader,
            Color color,
            float smoothness,
            bool emission = false,
            float metallic = 0f)
        {
            Material source = Resources.Load<Material>(ResourceFolder + resourceName);
            if (source != null && source.shader != null)
            {
                return new Material(source) { name = materialName + " (Runtime)" };
            }

            Shader usable = shader != null ? shader : FindLitShader();
            var material = usable != null
                ? new Material(usable)
                : new Material(Shader.Find("Unlit/Color"));
            material.name = materialName + " (Runtime)";
            SetColor(material, color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.15f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            return material;
        }

        private static Shader FindLitShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                return shader;
            }

            Material snow = Resources.Load<Material>(ResourceFolder + "Snow");
            if (snow != null && snow.shader != null)
            {
                return snow.shader;
            }

            return Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
        }

        private static void SetColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }
    }
}
