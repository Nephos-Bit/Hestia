using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace WinterMarket
{
    /// <summary>
    /// Development-only performance summary. It is never attached to a release player
    /// unless -perfCapture is explicitly supplied.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrameDiagnostics : MonoBehaviour
    {
        [SerializeField, Min(10f)] private float reportInterval = 30f;
        [SerializeField, Min(60f)] private float soakDuration = 600f;

        private static FrameDiagnostics activeInstance;
        private static bool captureArgumentRead;
        private static bool performanceCaptureRequested;

        private readonly List<float> intervalFrameTimes = new List<float>(2048);
        private readonly List<float> intervalBuildTimes = new List<float>(64);
        private ProfilerRecorder gcAllocatedRecorder;
        private ProfilerRecorder drawCallsRecorder;
        private double startedAt;
        private double intervalStartedAt;
        private int intervalFrames;
        private int totalFrames;
        private float slowestFrameMs;
        private long intervalGcBytes;
        private bool finished;

        public static bool IsPerformanceCaptureRequested
        {
            get
            {
                if (!captureArgumentRead)
                {
                    ReadCaptureArgument();
                }

                return performanceCaptureRequested;
            }
        }

        public static void RecordBuildAction(float durationMs)
        {
            if (activeInstance != null && durationMs >= 0f)
            {
                activeInstance.intervalBuildTimes.Add(durationMs);
            }
        }

        private void Awake()
        {
            ParseArguments();
            activeInstance = this;
            startedAt = Time.realtimeSinceStartupAsDouble;
            intervalStartedAt = startedAt;
            gcAllocatedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC Allocated In Frame");
            drawCallsRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render,
                "Draw Calls Count");
        }

        private void OnDestroy()
        {
            if (activeInstance == this)
            {
                activeInstance = null;
            }

            gcAllocatedRecorder.Dispose();
            drawCallsRecorder.Dispose();
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            float frameMs = Time.unscaledDeltaTime * 1000f;
            intervalFrameTimes.Add(frameMs);
            intervalFrames++;
            totalFrames++;
            slowestFrameMs = Mathf.Max(slowestFrameMs, frameMs);
            if (gcAllocatedRecorder.Valid)
            {
                intervalGcBytes += Math.Max(0L, gcAllocatedRecorder.LastValue);
            }

            double now = Time.realtimeSinceStartupAsDouble;
            double interval = now - intervalStartedAt;
            if (interval >= reportInterval)
            {
                WriteIntervalReport(interval);
                intervalStartedAt = now;
                intervalFrames = 0;
                slowestFrameMs = 0f;
                intervalGcBytes = 0L;
                intervalFrameTimes.Clear();
                intervalBuildTimes.Clear();
            }

            double elapsed = now - startedAt;
            if (elapsed >= soakDuration)
            {
                finished = true;
                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "WINTERMARKT_SOAK_PASS duration={0:F1}s averageFps={1:F2} frames={2} memoryBytes={3}",
                    elapsed,
                    totalFrames / elapsed,
                    totalFrames,
                    Profiler.GetTotalAllocatedMemoryLong()));
            }
        }

        private void WriteIntervalReport(double interval)
        {
            float p95FrameMs = Percentile(intervalFrameTimes, 0.95f);
            float p95BuildMs = Percentile(intervalBuildTimes, 0.95f);
            long drawCalls = drawCallsRecorder.Valid ? drawCallsRecorder.LastValue : -1L;
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "WINTERMARKT_PERF interval={0:F1}s fps={1:F2} p95FrameMs={2:F2} worstFrameMs={3:F2} gcBytes={4} drawCalls={5} buildActions={6} p95BuildMs={7:F2} memoryBytes={8}",
                interval,
                intervalFrames / interval,
                p95FrameMs,
                slowestFrameMs,
                intervalGcBytes,
                drawCalls,
                intervalBuildTimes.Count,
                p95BuildMs,
                Profiler.GetTotalAllocatedMemoryLong()));
        }

        private static float Percentile(List<float> samples, float percentile)
        {
            if (samples == null || samples.Count == 0)
            {
                return 0f;
            }

            samples.Sort();
            int index = Mathf.Clamp(
                Mathf.CeilToInt((samples.Count - 1) * Mathf.Clamp01(percentile)),
                0,
                samples.Count - 1);
            return samples[index];
        }

        private void ParseArguments()
        {
            ReadCaptureArgument();
            const string prefix = "-soakSeconds=";
            string[] args = Environment.GetCommandLineArgs();
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

        private static void ReadCaptureArgument()
        {
            captureArgumentRead = true;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-perfCapture", StringComparison.OrdinalIgnoreCase))
                {
                    performanceCaptureRequested = true;
                    return;
                }
            }
        }
    }
}
