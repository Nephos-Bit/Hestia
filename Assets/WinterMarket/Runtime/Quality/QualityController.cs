using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WinterMarket
{
    public enum QualityLevel
    {
        Standard = 0,
        High = 1
    }

    [DisallowMultipleComponent]
    public sealed class QualityController : MonoBehaviour
    {
        [Serializable]
        private struct QualityProfile
        {
            [Min(1)] public int targetFrameRate;
            [Min(0f)] public float shadowDistance;
            [Min(256)] public int mainLightShadowResolution;
            [Range(0.1f, 2f)] public float renderScale;
            [Min(0)] public int snowParticleLimit;
            [Range(1, 8)] public int msaaSamples;
            [Range(0, 8)] public int additionalLightsPerObject;
            [Range(0, 16)] public int warmLightBudget;
            public bool useHdr;
            public bool useDepthTexture;
            public bool useSsao;
        }

        [SerializeField] private QualityLevel fallbackQuality = QualityLevel.Standard;
        [SerializeField] private QualityProfile standard = new QualityProfile
        {
            targetFrameRate = 30,
            shadowDistance = 22f,
            mainLightShadowResolution = 512,
            renderScale = 0.75f,
            snowParticleLimit = 4000,
            msaaSamples = 1,
            additionalLightsPerObject = 2,
            warmLightBudget = 4,
            useHdr = false,
            useDepthTexture = false,
            useSsao = false
        };
        [SerializeField] private QualityProfile high = new QualityProfile
        {
            targetFrameRate = 60,
            shadowDistance = 36f,
            mainLightShadowResolution = 1024,
            renderScale = 0.9f,
            snowParticleLimit = 10000,
            msaaSamples = 2,
            additionalLightsPerObject = 4,
            warmLightBudget = 8,
            useHdr = true,
            useDepthTexture = true,
            useSsao = true
        };

        private QualityLevel currentQuality;
        private int currentSnowParticleLimit;
        private int currentWarmLightBudget;
        private ScreenSpaceAmbientOcclusion[] cachedAmbientOcclusionFeatures;

        public event Action<QualityLevel> QualityChanged;
        public event Action<int> SnowParticleLimitChanged;

        public QualityLevel CurrentQuality => currentQuality;
        public int CurrentSnowParticleLimit => currentSnowParticleLimit;
        public int CurrentWarmLightBudget => currentWarmLightBudget;

        private void Awake()
        {
            ApplyQuality(LocalSettings.GetQuality(fallbackQuality), false);
        }

        private void Start()
        {
            QualityChanged?.Invoke(currentQuality);
            SnowParticleLimitChanged?.Invoke(currentSnowParticleLimit);
        }

        public void SetQuality(QualityLevel quality)
        {
            ApplyQuality(quality, true);
        }

        public void ApplyQuality(QualityLevel quality, bool persist)
        {
            QualityLevel safeQuality = quality == QualityLevel.High
                ? QualityLevel.High
                : QualityLevel.Standard;
            QualityProfile profile = safeQuality == QualityLevel.High ? high : standard;

            Application.targetFrameRate = Mathf.Max(1, profile.targetFrameRate);
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            QualitySettings.shadowDistance = Mathf.Max(0f, profile.shadowDistance);
            QualitySettings.shadowResolution = ToShadowResolution(profile.mainLightShadowResolution);
            QualitySettings.shadowProjection = ShadowProjection.StableFit;

            UniversalRenderPipelineAsset pipelineAsset =
                GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipelineAsset != null)
            {
                pipelineAsset.shadowDistance = Mathf.Max(0f, profile.shadowDistance);
                pipelineAsset.mainLightShadowmapResolution =
                    SanitizeShadowResolution(profile.mainLightShadowResolution);
                pipelineAsset.renderScale = Mathf.Clamp(profile.renderScale, 0.1f, 2f);
                pipelineAsset.shadowCascadeCount = safeQuality == QualityLevel.High ? 2 : 1;
                pipelineAsset.supportsHDR = profile.useHdr;
                pipelineAsset.supportsCameraDepthTexture = profile.useDepthTexture;
                pipelineAsset.supportsCameraOpaqueTexture = false;
                pipelineAsset.msaaSampleCount = SanitizeMsaa(profile.msaaSamples);
                pipelineAsset.maxAdditionalLightsCount = Mathf.Clamp(profile.additionalLightsPerObject, 0, 8);
                pipelineAsset.upscalingFilter = profile.renderScale < 0.999f
                    ? UpscalingFilterSelection.FSR
                    : UpscalingFilterSelection.Auto;
                pipelineAsset.fsrOverrideSharpness = profile.renderScale < 0.999f;
                pipelineAsset.fsrSharpness = 0.55f;
            }

            EnsureAmbientOcclusionFeatures();
            for (int i = 0; i < cachedAmbientOcclusionFeatures.Length; i++)
            {
                if (cachedAmbientOcclusionFeatures[i] != null)
                {
                    cachedAmbientOcclusionFeatures[i].SetActive(profile.useSsao);
                }
            }

            bool qualityChanged = currentQuality != safeQuality;
            bool snowLimitChanged = currentSnowParticleLimit != profile.snowParticleLimit;
            bool lightBudgetChanged = currentWarmLightBudget != profile.warmLightBudget;
            currentQuality = safeQuality;
            currentSnowParticleLimit = Mathf.Max(0, profile.snowParticleLimit);
            currentWarmLightBudget = Mathf.Max(0, profile.warmLightBudget);

            if (persist)
            {
                LocalSettings.SetQuality(currentQuality);
            }

            if (qualityChanged || lightBudgetChanged)
            {
                QualityChanged?.Invoke(currentQuality);
            }

            if (snowLimitChanged)
            {
                SnowParticleLimitChanged?.Invoke(currentSnowParticleLimit);
            }
        }

        public void ReloadPersistedQuality()
        {
            ApplyQuality(LocalSettings.GetQuality(fallbackQuality), false);
        }

        private static int SanitizeShadowResolution(int resolution)
        {
            if (resolution >= 4096)
            {
                return 4096;
            }

            if (resolution >= 2048)
            {
                return 2048;
            }

            if (resolution >= 1024)
            {
                return 1024;
            }

            if (resolution >= 512)
            {
                return 512;
            }

            return 256;
        }

        private static UnityEngine.ShadowResolution ToShadowResolution(int resolution)
        {
            if (resolution >= 4096)
            {
                return UnityEngine.ShadowResolution.VeryHigh;
            }

            if (resolution >= 2048)
            {
                return UnityEngine.ShadowResolution.High;
            }

            if (resolution >= 1024)
            {
                return UnityEngine.ShadowResolution.Medium;
            }

            return UnityEngine.ShadowResolution.Low;
        }

        private static int SanitizeMsaa(int samples)
        {
            if (samples >= 8) return 8;
            if (samples >= 4) return 4;
            if (samples >= 2) return 2;
            return 1;
        }

        private void EnsureAmbientOcclusionFeatures()
        {
            if (cachedAmbientOcclusionFeatures == null)
            {
                cachedAmbientOcclusionFeatures =
                    Resources.FindObjectsOfTypeAll<ScreenSpaceAmbientOcclusion>();
            }
        }

        private void OnValidate()
        {
            standard.targetFrameRate = Mathf.Max(1, standard.targetFrameRate);
            high.targetFrameRate = Mathf.Max(1, high.targetFrameRate);
            standard.shadowDistance = Mathf.Max(0f, standard.shadowDistance);
            high.shadowDistance = Mathf.Max(0f, high.shadowDistance);
            standard.mainLightShadowResolution =
                SanitizeShadowResolution(standard.mainLightShadowResolution);
            high.mainLightShadowResolution =
                SanitizeShadowResolution(high.mainLightShadowResolution);
            standard.renderScale = Mathf.Clamp(standard.renderScale, 0.1f, 2f);
            high.renderScale = Mathf.Clamp(high.renderScale, 0.1f, 2f);
            standard.snowParticleLimit = Mathf.Max(0, standard.snowParticleLimit);
            high.snowParticleLimit = Mathf.Max(0, high.snowParticleLimit);
            standard.msaaSamples = SanitizeMsaa(standard.msaaSamples);
            high.msaaSamples = SanitizeMsaa(high.msaaSamples);
            standard.additionalLightsPerObject = Mathf.Clamp(standard.additionalLightsPerObject, 0, 8);
            high.additionalLightsPerObject = Mathf.Clamp(high.additionalLightsPerObject, 0, 8);
            standard.warmLightBudget = Mathf.Max(0, standard.warmLightBudget);
            high.warmLightBudget = Mathf.Max(0, high.warmLightBudget);
        }
    }
}
