#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace WinterMarket.Editor
{
    public static class WinterMarketProjectBuilder
    {
        private const string ScenePath = "Assets/WinterMarket/Scenes/WinterMarket.unity";
        private const string SettingsFolder = "Assets/WinterMarket/Settings";
        private const string MaterialsFolder = "Assets/WinterMarket/Resources/WinterMarketMaterials";
        private const string PrefabsFolder = "Assets/WinterMarket/Prefabs";
        private const string RendererPath = SettingsFolder + "/WinterMarketRenderer.asset";
        private const string PipelinePath = SettingsFolder + "/WinterMarketURP.asset";

        [MenuItem("Wintermarkt/Prepare Project")]
        public static void PrepareProject()
        {
            EnsureFolder("Assets/WinterMarket", "Scenes");
            EnsureFolder("Assets/WinterMarket", "Settings");
            EnsureFolder("Assets/WinterMarket", "Resources");
            EnsureFolder("Assets/WinterMarket/Resources", "WinterMarketMaterials");
            EnsureFolder("Assets/WinterMarket", "Prefabs");
            ConfigureRendering();
            CreateMaterialAssets();
            ConfigurePlayer();
            CreateScene();
            CreatePrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("WINTERMARKT_PREPARE_PASS scene=" + ScenePath);
        }

        [MenuItem("Wintermarkt/Capture Four Times")]
        public static void CaptureFourTimes()
        {
            PrepareProject();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SceneController controller = UnityEngine.Object.FindFirstObjectByType<SceneController>();
            if (controller == null)
            {
                throw new InvalidOperationException("SceneController is missing.");
            }

            controller.Build();
            Capture(controller, 7.5f, "morning-0730.png");
            Capture(controller, 14f, "afternoon-1400.png");
            Capture(controller, 19f, "evening-1900.png");
            Capture(controller, 22.5f, "closing-2230.png");
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("WINTERMARKT_CAPTURE_PASS count=4");
        }

        [MenuItem("Wintermarkt/Build Android APK")]
        public static void BuildAndroid()
        {
            PrepareProject();
            string root = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve project root.");
            string output = Path.Combine(root, "Builds", "Android", "Wintermarkt-v0.2.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? root);
            string shortGradleHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "wm-g");
            Directory.CreateDirectory(shortGradleHome);
            Environment.SetEnvironmentVariable("GRADLE_USER_HOME", shortGradleHome);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Android build failed: {report.summary.result}, errors={report.summary.totalErrors}");
            }

            Debug.Log(
                $"WINTERMARKT_ANDROID_BUILD_PASS path={output} bytes={report.summary.totalSize} " +
                $"seconds={report.summary.totalTime.TotalSeconds:F1}");
        }

        private static void ConfigureRendering()
        {
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.name = "WinterMarket Renderer";
                rendererData.renderingMode = RenderingMode.Forward;
                AssetDatabase.CreateAsset(rendererData, RendererPath);

                var ambientOcclusion = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ambientOcclusion.name = "Subtle Mobile SSAO";
                ambientOcclusion.SetActive(true);
                rendererData.rendererFeatures.Add(ambientOcclusion);
                AssetDatabase.AddObjectToAsset(ambientOcclusion, rendererData);
                EditorUtility.SetDirty(rendererData);
            }

            // Existing V0.1 projects already have the feature asset. Re-enable it here as
            // well so Prepare Project consistently restores subtle contact shading.
            for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
            {
                if (rendererData.rendererFeatures[i] is ScreenSpaceAmbientOcclusion ambientOcclusion)
                {
                    ambientOcclusion.SetActive(true);
                    EditorUtility.SetDirty(ambientOcclusion);
                }
            }

            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                pipeline.name = "WinterMarket Mobile URP";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            pipeline.supportsHDR = true;
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.msaaSampleCount = 2;
            pipeline.renderScale = 0.9f;
            pipeline.shadowDistance = 36f;
            pipeline.mainLightShadowmapResolution = 1024;
            pipeline.shadowCascadeCount = 2;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(rendererData);
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Felix Seltmann";
            PlayerSettings.productName = "Wintermarkt";
            PlayerSettings.bundleVersion = "0.2.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.felixseltmann.wintermarkt");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = false;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        }

        private static void CreateScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Wintermarkt Diorama");
            root.AddComponent<SceneController>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void CreateMaterialAssets()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            EnsureMaterial("Snow", lit, WinterPalette.Snow, 0.02f);
            EnsureMaterial("SnowShadow", lit, WinterPalette.SnowShadow, 0.01f);
            EnsureMaterial("LightWood", lit, WinterPalette.LightWood, 0.05f);
            EnsureMaterial("DarkWood", lit, WinterPalette.DarkWood, 0.04f);
            EnsureMaterial("MutedRed", lit, WinterPalette.ChristmasRed, 0.03f);
            EnsureMaterial("FirGreen", lit, WinterPalette.FirGreen, 0.02f);
            EnsureMaterial("WarmEmission", lit, WinterPalette.Lantern, 0.08f, true);
            EnsureMaterial("NightBlue", lit, WinterPalette.Night, 0.03f);
            EnsureMaterial("SnowPath", lit, WinterPalette.Path, 0.01f);
            EnsureMaterial("BlueGreyRoof", lit, WinterPalette.RoofBlue, 0.03f);
            EnsureMaterial("Skin", lit, WinterPalette.Skin, 0.02f);
            EnsureMaterial("CoatBlue", lit, WinterPalette.CoatBlue, 0.02f);
            EnsureMaterial("CoatOchre", lit, WinterPalette.CoatOchre, 0.02f);
            EnsureMaterial("Metal", lit, new Color(0.22f, 0.25f, 0.27f), 0.26f);
            EnsureMaterial("Cream", lit, new Color(0.91f, 0.87f, 0.8f), 0.03f);
            EnsureMaterial("DeepGreen", lit, new Color(0.19f, 0.28f, 0.23f), 0.02f);
            EnsureMaterial("SnowParticles", particles, new Color(1f, 1f, 1f, 0.75f), 0f);
            EnsureSnowCapMaterial();
            AssetDatabase.SaveAssets();
        }

        private static void EnsureMaterial(
            string assetName,
            Shader shader,
            Color color,
            float smoothness,
            bool emission = false)
        {
            string path = $"{MaterialsFolder}/{assetName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", assetName == "Metal" ? 0.28f : 0f);
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.1f);
            }
            EditorUtility.SetDirty(material);
        }

        private static void EnsureSnowCapMaterial()
        {
            Shader shader = Shader.Find("WinterMarket/SnowCap");
            if (shader == null)
            {
                throw new InvalidOperationException("WinterMarket/SnowCap shader is missing.");
            }

            string path = $"{MaterialsFolder}/SnowCap.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "SnowCap" };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetColor("_TopColor", WinterPalette.SnowCap);
            material.SetColor("_SideColor", WinterPalette.SnowCapShade);
            EditorUtility.SetDirty(material);
        }

        private static void CreatePrefabs()
        {
            var bootstrap = new GameObject("Wintermarkt Diorama");
            bootstrap.AddComponent<SceneController>();
            PrefabUtility.SaveAsPrefabAsset(bootstrap, PrefabsFolder + "/WintermarktDiorama.prefab");
            UnityEngine.Object.DestroyImmediate(bootstrap);

            var factoryObject = new GameObject("NPC Prefab Factory");
            NPCFactory factory = factoryObject.AddComponent<NPCFactory>();
            factory.ConfigureMaterials(LoadPaletteAssets());
            NPCBehaviour npc = factory.BuildNPC(null);
            npc.gameObject.SetActive(true);
            npc.gameObject.name = "Voxel NPC";
            PrefabUtility.SaveAsPrefabAsset(npc.gameObject, PrefabsFolder + "/VoxelNPC.prefab");
            UnityEngine.Object.DestroyImmediate(npc.gameObject);
            UnityEngine.Object.DestroyImmediate(factoryObject);
        }

        private static Material[] LoadPaletteAssets()
        {
            string[] names =
            {
                "Snow", "SnowShadow", "LightWood", "DarkWood", "MutedRed", "FirGreen",
                "WarmEmission", "NightBlue", "SnowPath", "BlueGreyRoof", "Skin", "CoatBlue",
                "CoatOchre", "Metal", "Cream", "DeepGreen"
            };
            var materials = new Material[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                materials[i] = AssetDatabase.LoadAssetAtPath<Material>(
                    $"{MaterialsFolder}/{names[i]}.mat");
            }
            return materials;
        }

        private static void Capture(SceneController controller, float hour, string fileName)
        {
            controller.TimeOfDay.SetDebugTime(hour);
            controller.Lighting.ApplyAtHour(hour);

            const int width = 1080;
            const int height = 2340;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
                name = "Wintermarkt Capture"
            };
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = controller.DioramaCamera.targetTexture;

            try
            {
                controller.DioramaCamera.targetTexture = target;
                controller.DioramaCamera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);

                string root = Directory.GetParent(Application.dataPath)?.FullName
                    ?? throw new InvalidOperationException("Could not resolve project root.");
                string folder = Path.Combine(root, "Artifacts", "Screenshots");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, fileName), texture.EncodeToPNG());
            }
            finally
            {
                controller.DioramaCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
#endif
