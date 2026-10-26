using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WinterMarket
{
    [Serializable]
    public struct LightingLook
    {
        [Range(0f, 24f)] public float hour;
        public Vector3 directionalEulerAngles;
        [ColorUsage(false, true)] public Color directionalColor;
        [Min(0f)] public float directionalIntensity;
        [ColorUsage(false, true)] public Color ambientColor;
        [Min(0f)] public float ambientIntensity;
        [Range(0f, 1f)] public float reflectionIntensity;
        [ColorUsage(false, true)] public Color localLightColor;
        [Min(0f)] public float localLightIntensityMultiplier;
        [Min(0f)] public float rendererEmissionIntensity;
        [ColorUsage(false, false)] public Color backgroundColor;
        [ColorUsage(false, false)] public Color fogColor;
        [Min(0f)] public float fogDensity;
    }

    [DisallowMultipleComponent]
    public sealed class LightingController : MonoBehaviour
    {
        private const float HoursPerDay = 24f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Clock")]
        [SerializeField] private TimeOfDayController timeOfDay;

        [Header("Scene references")]
        [SerializeField] private Light directionalLight;
        [SerializeField] private Camera dioramaCamera;
        [SerializeField] private Light[] localWarmLights = Array.Empty<Light>();
        [SerializeField] private Renderer[] warmRenderers = Array.Empty<Renderer>();

        [Header("Looks")]
        [SerializeField] private LightingLook night = new LightingLook
        {
            hour = 1f,
            directionalEulerAngles = new Vector3(8f, -20f, 0f),
            directionalColor = new Color(0.48f, 0.58f, 0.76f, 1f),
            directionalIntensity = 0.2f,
            ambientColor = new Color(0.3f, 0.38f, 0.5f, 1f),
            ambientIntensity = 1.05f,
            reflectionIntensity = 0.32f,
            localLightColor = new Color(1f, 0.58f, 0.28f, 1f),
            localLightIntensityMultiplier = 0.82f,
            rendererEmissionIntensity = 1.15f,
            backgroundColor = new Color(0.45f, 0.53f, 0.64f, 1f),
            fogColor = new Color(0.45f, 0.53f, 0.64f, 1f),
            fogDensity = 0.018f
        };
        [SerializeField] private LightingLook morning = new LightingLook
        {
            hour = 7.5f,
            directionalEulerAngles = new Vector3(38f, -48f, 0f),
            directionalColor = new Color(0.88f, 0.94f, 1f, 1f),
            directionalIntensity = 1.35f,
            ambientColor = new Color(0.86f, 0.9f, 0.95f, 1f),
            ambientIntensity = 1.2f,
            reflectionIntensity = 0.7f,
            localLightColor = new Color(1f, 0.68f, 0.38f, 1f),
            localLightIntensityMultiplier = 0.35f,
            rendererEmissionIntensity = 0.45f,
            backgroundColor = new Color(0.96f, 0.97f, 0.99f, 1f),
            fogColor = new Color(0.96f, 0.97f, 0.99f, 1f),
            fogDensity = 0.004f
        };
        [SerializeField] private LightingLook day = new LightingLook
        {
            hour = 14.5f,
            directionalEulerAngles = new Vector3(52f, 128f, 0f),
            directionalColor = new Color(1f, 0.98f, 0.93f, 1f),
            directionalIntensity = 1.28f,
            ambientColor = new Color(0.8f, 0.86f, 0.92f, 1f),
            ambientIntensity = 1.1f,
            reflectionIntensity = 0.78f,
            localLightColor = new Color(1f, 0.7f, 0.4f, 1f),
            localLightIntensityMultiplier = 0.06f,
            rendererEmissionIntensity = 0.1f,
            backgroundColor = new Color(0.98f, 0.99f, 1f, 1f),
            fogColor = new Color(0.97f, 0.98f, 0.99f, 1f),
            fogDensity = 0.008f
        };
        [SerializeField] private LightingLook evening = new LightingLook
        {
            hour = 19f,
            directionalEulerAngles = new Vector3(12f, 214f, 0f),
            directionalColor = new Color(0.55f, 0.64f, 0.82f, 1f),
            directionalIntensity = 0.28f,
            ambientColor = new Color(0.3f, 0.38f, 0.5f, 1f),
            ambientIntensity = 0.98f,
            reflectionIntensity = 0.42f,
            localLightColor = new Color(1f, 0.54f, 0.24f, 1f),
            localLightIntensityMultiplier = 1.15f,
            rendererEmissionIntensity = 1.2f,
            backgroundColor = new Color(0.52f, 0.6f, 0.7f, 1f),
            fogColor = new Color(0.5f, 0.58f, 0.68f, 1f),
            fogDensity = 0.016f
        };

        private float[] localLightBaseIntensities = Array.Empty<float>();
        private MaterialPropertyBlock propertyBlock;
        private bool subscribed;

        public TimeOfDayController TimeOfDay => timeOfDay;
        public Light DirectionalLight => directionalLight;

        public void Configure(
            TimeOfDayController clock,
            Light mainLight,
            Light[] warmLights,
            Renderer[] emissiveRenderers,
            Camera camera = null)
        {
            Unsubscribe();
            EnsureLookDefaults();
            timeOfDay = clock;
            directionalLight = mainLight;
            dioramaCamera = camera;
            localWarmLights = warmLights ?? Array.Empty<Light>();
            warmRenderers = emissiveRenderers ?? Array.Empty<Renderer>();
            CacheLocalLightIntensities();
            Subscribe();
            ApplyAtHour(timeOfDay != null ? timeOfDay.Hour : GetLocalHour());
        }

        private void Awake()
        {
            EnsureLookDefaults();
            CacheLocalLightIntensities();
            if (warmRenderers == null)
            {
                warmRenderers = Array.Empty<Renderer>();
            }

            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (timeOfDay == null)
            {
                timeOfDay = FindFirstObjectByType<TimeOfDayController>();
            }

            Subscribe();
            ApplyAtHour(timeOfDay != null ? timeOfDay.Hour : GetLocalHour());
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void ApplyAtHour(float hour)
        {
            LightingLook from;
            LightingLook to;
            float interpolation;
            GetLookPair(NormalizeHour(hour), out from, out to, out interpolation);

            if (directionalLight != null)
            {
                directionalLight.transform.rotation = Quaternion.Slerp(
                    Quaternion.Euler(from.directionalEulerAngles),
                    Quaternion.Euler(to.directionalEulerAngles),
                    interpolation);
                directionalLight.color = Color.Lerp(from.directionalColor, to.directionalColor, interpolation);
                directionalLight.intensity = Mathf.Lerp(
                    from.directionalIntensity,
                    to.directionalIntensity,
                    interpolation);
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(from.ambientColor, to.ambientColor, interpolation);
            RenderSettings.ambientIntensity = Mathf.Lerp(
                from.ambientIntensity,
                to.ambientIntensity,
                interpolation);
            RenderSettings.reflectionIntensity = Mathf.Lerp(
                from.reflectionIntensity,
                to.reflectionIntensity,
                interpolation);

            Color warmColor = Color.Lerp(from.localLightColor, to.localLightColor, interpolation);
            float localIntensity = Mathf.Lerp(
                from.localLightIntensityMultiplier,
                to.localLightIntensityMultiplier,
                interpolation);
            float emissionIntensity = Mathf.Lerp(
                from.rendererEmissionIntensity,
                to.rendererEmissionIntensity,
                interpolation);
            Color background = SanitizeAtmosphereColor(
                Color.Lerp(from.backgroundColor, to.backgroundColor, interpolation),
                WinterPalette.Background);
            Color fog = SanitizeAtmosphereColor(
                Color.Lerp(from.fogColor, to.fogColor, interpolation),
                background);
            float fogDensity = Mathf.Max(
                0.002f,
                Mathf.Lerp(SafeFogDensity(from.fogDensity), SafeFogDensity(to.fogDensity), interpolation));

            if (dioramaCamera != null)
            {
                dioramaCamera.backgroundColor = background;
            }

            bool useFog = NormalizeHour(hour) < 6.75f || NormalizeHour(hour) >= 16.5f;
            RenderSettings.fog = useFog;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = useFog ? fogDensity : 0f;

            ApplyLocalLights(warmColor, localIntensity);
            ApplyWarmRenderers(warmColor * emissionIntensity);
        }

        private void ApplyLocalLights(Color color, float intensityMultiplier)
        {
            if (localLightBaseIntensities.Length != localWarmLights.Length)
            {
                CacheLocalLightIntensities();
            }

            for (int i = 0; i < localWarmLights.Length; i++)
            {
                Light localLight = localWarmLights[i];
                if (localLight == null)
                {
                    continue;
                }

                localLight.color = color;
                localLight.intensity = localLightBaseIntensities[i] * intensityMultiplier;
            }
        }

        private void ApplyWarmRenderers(Color emissionColor)
        {
            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            for (int i = 0; i < warmRenderers.Length; i++)
            {
                Renderer targetRenderer = warmRenderers[i];
                if (targetRenderer == null)
                {
                    continue;
                }

                targetRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(EmissionColorId, emissionColor);
                targetRenderer.SetPropertyBlock(propertyBlock);
                propertyBlock.Clear();
            }
        }

        private void CacheLocalLightIntensities()
        {
            if (localWarmLights == null)
            {
                localWarmLights = Array.Empty<Light>();
            }

            localLightBaseIntensities = new float[localWarmLights.Length];
            for (int i = 0; i < localWarmLights.Length; i++)
            {
                localLightBaseIntensities[i] =
                    localWarmLights[i] != null ? Mathf.Max(0f, localWarmLights[i].intensity) : 0f;
            }
        }

        private void GetLookPair(float hour, out LightingLook from, out LightingLook to, out float interpolation)
        {
            if (hour < night.hour)
            {
                from = evening;
                to = night;
                interpolation = Mathf.InverseLerp(evening.hour, night.hour + HoursPerDay, hour + HoursPerDay);
                return;
            }

            if (hour < morning.hour)
            {
                from = night;
                to = morning;
                interpolation = Mathf.InverseLerp(night.hour, morning.hour, hour);
                return;
            }

            if (hour < day.hour)
            {
                from = morning;
                to = day;
                interpolation = Mathf.InverseLerp(morning.hour, day.hour, hour);
                return;
            }

            if (hour < evening.hour)
            {
                from = day;
                to = evening;
                interpolation = Mathf.InverseLerp(day.hour, evening.hour, hour);
                return;
            }

            from = evening;
            to = night;
            interpolation = Mathf.InverseLerp(evening.hour, night.hour + HoursPerDay, hour);
        }

        private void Subscribe()
        {
            if (!subscribed && timeOfDay != null)
            {
                timeOfDay.TimeChanged += ApplyAtHour;
                subscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (subscribed && timeOfDay != null)
            {
                timeOfDay.TimeChanged -= ApplyAtHour;
            }

            subscribed = false;
        }

        private static Color SanitizeAtmosphereColor(Color color, Color fallback)
        {
            if (color.maxColorComponent < 0.08f || color.a < 0.05f)
            {
                return fallback;
            }

            return color;
        }

        private static float SafeFogDensity(float density)
        {
            return density > 0.0001f ? density : 0.006f;
        }

        private static float GetLocalHour()
        {
            DateTime now = DateTime.Now;
            return now.Hour + now.Minute / 60f + now.Second / 3600f;
        }

        private static float NormalizeHour(float hour)
        {
            float normalized = hour % HoursPerDay;
            return normalized < 0f ? normalized + HoursPerDay : normalized;
        }

        private void EnsureLookDefaults()
        {
            night = new LightingLook
            {
                hour = 1f,
                directionalEulerAngles = new Vector3(8f, -20f, 0f),
                directionalColor = new Color(0.48f, 0.58f, 0.76f, 1f),
                directionalIntensity = 0.2f,
                ambientColor = new Color(0.3f, 0.38f, 0.5f, 1f),
                ambientIntensity = 1.05f,
                reflectionIntensity = 0.32f,
                localLightColor = new Color(1f, 0.58f, 0.28f, 1f),
                localLightIntensityMultiplier = 0.82f,
                rendererEmissionIntensity = 1.15f,
                backgroundColor = new Color(0.45f, 0.53f, 0.64f, 1f),
                fogColor = new Color(0.45f, 0.53f, 0.64f, 1f),
                fogDensity = 0.018f
            };
            morning = new LightingLook
            {
                hour = 7.5f,
                directionalEulerAngles = new Vector3(38f, -48f, 0f),
                directionalColor = new Color(0.88f, 0.94f, 1f, 1f),
                directionalIntensity = 1.35f,
                ambientColor = new Color(0.86f, 0.9f, 0.95f, 1f),
                ambientIntensity = 1.2f,
                reflectionIntensity = 0.7f,
                localLightColor = new Color(1f, 0.68f, 0.38f, 1f),
                localLightIntensityMultiplier = 0.35f,
                rendererEmissionIntensity = 0.45f,
                backgroundColor = new Color(0.96f, 0.97f, 0.99f, 1f),
                fogColor = new Color(0.96f, 0.97f, 0.99f, 1f),
                fogDensity = 0.004f
            };
            day = new LightingLook
            {
                hour = 14.5f,
                directionalEulerAngles = new Vector3(52f, 128f, 0f),
                directionalColor = new Color(1f, 0.98f, 0.93f, 1f),
                directionalIntensity = 1.28f,
                ambientColor = new Color(0.8f, 0.86f, 0.92f, 1f),
                ambientIntensity = 1.1f,
                reflectionIntensity = 0.78f,
                localLightColor = new Color(1f, 0.7f, 0.4f, 1f),
                localLightIntensityMultiplier = 0.06f,
                rendererEmissionIntensity = 0.1f,
                backgroundColor = new Color(0.98f, 0.99f, 1f, 1f),
                fogColor = new Color(0.97f, 0.98f, 0.99f, 1f),
                fogDensity = 0.008f
            };
            evening = new LightingLook
            {
                hour = 19f,
                directionalEulerAngles = new Vector3(12f, 214f, 0f),
                directionalColor = new Color(0.55f, 0.64f, 0.82f, 1f),
                directionalIntensity = 0.28f,
                ambientColor = new Color(0.3f, 0.38f, 0.5f, 1f),
                ambientIntensity = 0.98f,
                reflectionIntensity = 0.42f,
                localLightColor = new Color(1f, 0.54f, 0.24f, 1f),
                localLightIntensityMultiplier = 1.15f,
                rendererEmissionIntensity = 1.2f,
                backgroundColor = new Color(0.52f, 0.6f, 0.7f, 1f),
                fogColor = new Color(0.5f, 0.58f, 0.68f, 1f),
                fogDensity = 0.016f
            };
        }

        private void OnValidate()
        {
            if (localWarmLights == null)
            {
                localWarmLights = Array.Empty<Light>();
            }

            if (warmRenderers == null)
            {
                warmRenderers = Array.Empty<Renderer>();
            }

            EnsureLookDefaults();
        }
    }
}
