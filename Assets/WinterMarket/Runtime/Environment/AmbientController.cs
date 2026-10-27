using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class AmbientController : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] snowLayers = Array.Empty<ParticleSystem>();
        [SerializeField] private AudioSource windSource;
        [SerializeField] private AudioSource oneShotSource;
        [SerializeField, Range(0f, 1f)] private float windVolume = 0.075f;

        private AudioClip[] footsteps;
        private Camera dioramaCamera;

        public void BuildRuntimeAtmosphere(Material snowMaterial, Camera camera = null)
        {
            dioramaCamera = camera != null ? camera : Camera.main;
            if (snowLayers.Length == 0)
            {
                snowLayers = new ParticleSystem[3];
                snowLayers[0] = CreateSnowLayer("Snow Near", 110, 0.055f, 0.75f, new Vector3(13f, 7f, 10f), snowMaterial);
                snowLayers[1] = CreateSnowLayer("Snow Mid", 90, 0.04f, 0.55f, new Vector3(16f, 8f, 13f), snowMaterial);
                snowLayers[2] = CreateSnowLayer("Snow Far", 65, 0.03f, 0.38f, new Vector3(19f, 9f, 16f), snowMaterial);
            }

            UpdateSnowCoverage();
            BuildAudio();
        }

        private void LateUpdate()
        {
            UpdateSnowCoverage();
        }

        public void ApplyQuality(bool high)
        {
            for (int i = 0; i < snowLayers.Length; i++)
            {
                if (snowLayers[i] == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = snowLayers[i].main;
                main.maxParticles = high ? 150 - i * 20 : 95 - i * 20;
            }
        }

        public void PlayFootstep(float volume = 0.08f)
        {
            if (oneShotSource == null || footsteps == null || footsteps.Length == 0)
            {
                return;
            }

            int index = UnityEngine.Random.Range(0, footsteps.Length);
            oneShotSource.pitch = UnityEngine.Random.Range(0.92f, 1.06f);
            oneShotSource.PlayOneShot(footsteps[index], volume);
        }

        private ParticleSystem CreateSnowLayer(
            string objectName,
            int maximum,
            float size,
            float speed,
            Vector3 box,
            Material material)
        {
            var layerObject = new GameObject(objectName);
            layerObject.transform.SetParent(transform, false);
            layerObject.transform.localPosition = new Vector3(0f, 7f, 0f);

            var particles = layerObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(11f, 18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.65f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.65f, size * 1.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, 0.45f),
                new Color(0.9f, 0.95f, 1f, 0.82f));
            main.maxParticles = maximum;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.012f;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = maximum / 12f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.025f, 0.11f);
            velocity.y = new ParticleSystem.MinMaxCurve(-speed, -speed * 0.65f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.035f, 0.04f);

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.025f, 0.085f);
            noise.frequency = 0.16f;
            noise.scrollSpeed = 0.08f;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material = material;
            particles.Play();
            return particles;
        }

        // Particle systems simulate in world space, but their emitters follow the current
        // ground focus. This keeps snowfall over the complete visible map after panning or
        // zooming, while already emitted flakes continue their natural fall.
        private void UpdateSnowCoverage()
        {
            if (dioramaCamera == null)
            {
                dioramaCamera = Camera.main;
            }
            if (dioramaCamera == null || !dioramaCamera.orthographic || snowLayers == null)
            {
                return;
            }

            Ray centerRay = dioramaCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(centerRay, out float distance))
            {
                return;
            }

            Vector3 center = centerRay.GetPoint(distance);
            float coverage = Mathf.Max(44f, dioramaCamera.orthographicSize * 4.25f);
            for (int i = 0; i < snowLayers.Length; i++)
            {
                ParticleSystem layer = snowLayers[i];
                if (layer == null) continue;

                layer.transform.position = new Vector3(center.x, 10f + i * 1.5f, center.z);
                ParticleSystem.ShapeModule shape = layer.shape;
                shape.scale = new Vector3(coverage, 12f, coverage);
            }
        }

        private void BuildAudio()
        {
            if (windSource == null)
            {
                windSource = gameObject.AddComponent<AudioSource>();
            }

            windSource.loop = true;
            windSource.playOnAwake = false;
            windSource.spatialBlend = 0f;
            windSource.volume = windVolume;
            windSource.clip = CreateWindClip();
            windSource.Play();

            if (oneShotSource == null)
            {
                var sourceObject = new GameObject("Quiet Footsteps");
                sourceObject.transform.SetParent(transform, false);
                oneShotSource = sourceObject.AddComponent<AudioSource>();
            }

            oneShotSource.playOnAwake = false;
            oneShotSource.spatialBlend = 0.45f;
            footsteps = new[] { CreateFootstepClip(0), CreateFootstepClip(1) };
        }

        private static AudioClip CreateWindClip()
        {
            const int sampleRate = 22050;
            const int seconds = 6;
            var samples = new float[sampleRate * seconds];
            var random = new System.Random(7319);
            float low = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.012f;
                float breath = 0.62f + Mathf.Sin(i * (Mathf.PI * 2f / sampleRate) * 0.11f) * 0.17f;
                samples[i] = low * breath * 0.22f;
            }

            AudioClip clip = AudioClip.Create("Procedural Winter Wind", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateFootstepClip(int variant)
        {
            const int sampleRate = 22050;
            const int sampleCount = 2205;
            var samples = new float[sampleCount];
            var random = new System.Random(421 + variant * 83);
            float filtered = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float progress = i / (float)sampleCount;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                filtered += (noise - filtered) * 0.24f;
                float envelope = Mathf.Pow(1f - progress, 3.2f) * Mathf.Clamp01(progress * 45f);
                samples[i] = filtered * envelope * 0.38f;
            }

            AudioClip clip = AudioClip.Create($"Procedural Snow Step {variant + 1}", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
