using System;
using System.Globalization;
using UnityEngine;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class FrameDiagnostics : MonoBehaviour
    {
        [SerializeField, Min(10f)] private float reportInterval = 30f;
        [SerializeField, Min(60f)] private float soakDuration = 600f;

        private double startedAt;
        private double intervalStartedAt;
        private int intervalFrames;
        private int totalFrames;
        private float slowestFrameMs;
        private bool finished;

        private void Awake()
        {
            ParseArguments();
            startedAt = Time.realtimeSinceStartupAsDouble;
            intervalStartedAt = startedAt;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            intervalFrames++;
            totalFrames++;
            slowestFrameMs = Mathf.Max(slowestFrameMs, Time.unscaledDeltaTime * 1000f);

            double now = Time.realtimeSinceStartupAsDouble;
            double interval = now - intervalStartedAt;
            if (interval >= reportInterval)
            {
                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "WINTERMARKT_PERF interval={0:F1}s fps={1:F2} worstFrameMs={2:F2} visibleRenderers={3}",
                    interval,
                    intervalFrames / interval,
                    slowestFrameMs,
                    FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length));
                intervalStartedAt = now;
                intervalFrames = 0;
                slowestFrameMs = 0f;
            }

            double elapsed = now - startedAt;
            if (elapsed >= soakDuration)
            {
                finished = true;
                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "WINTERMARKT_SOAK_PASS duration={0:F1}s averageFps={1:F2} frames={2}",
                    elapsed,
                    totalFrames / elapsed,
                    totalFrames));
            }
        }

        private void ParseArguments()
        {
            string[] args = Environment.GetCommandLineArgs();
            const string prefix = "-soakSeconds=";
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (float.TryParse(
                        args[i].Substring(prefix.Length),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float parsed))
                {
                    soakDuration = Mathf.Max(60f, parsed);
                }
            }
        }
    }
}
