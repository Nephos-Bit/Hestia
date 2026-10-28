using System;
using System.Globalization;
using UnityEngine;

namespace WinterMarket
{
    public enum TimeOfDayPhase
    {
        EarlyMorning,
        Day,
        Evening,
        Closing,
        Night
    }

    [DisallowMultipleComponent]
    public sealed class TimeOfDayController : MonoBehaviour
    {
        private const string CommandLineArgument = "-winterTime=";
        private const float HoursPerDay = 24f;
        private const float DegreesPerHour = 360f / HoursPerDay;

        [Header("Phase boundaries")]
        [SerializeField, Range(0f, 24f)] private float earlyMorningStartsAt = 5.5f;
        [SerializeField, Range(0f, 24f)] private float dayStartsAt = 9.5f;
        [SerializeField, Range(0f, 24f)] private float eveningStartsAt = 16.5f;
        [SerializeField, Range(0f, 24f)] private float closingStartsAt = 20.5f;
        [SerializeField, Range(0f, 24f)] private float nightStartsAt = 23.5f;

        [Header("Clock")]
        [SerializeField, Min(0f)] private float smoothingTime = 0.2f;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField, Min(0.05f)] private float visualUpdateInterval = 0.25f;

        private float hour;
        private float angularVelocity;
        private bool hasDebugTime;
        private float debugHour;
        private TimeOfDayPhase currentPhase;
        private bool initialized;
        private float nextVisualUpdateAt;

        public event Action<float> TimeChanged;
        public event Action<TimeOfDayPhase> PhaseChanged;

        public float Hour => hour;
        public float CurrentHour => hour;
        public TimeOfDayPhase CurrentPhase => currentPhase;
        public bool IsDebugTimeEnabled => hasDebugTime;
        public float DebugHour => debugHour;

        private void Awake()
        {
            if (TryReadCommandLineTime(out float commandLineHour))
            {
                hasDebugTime = true;
                debugHour = commandLineHour;
            }

            hour = GetTargetHour();
            currentPhase = GetPhase(hour);
            nextVisualUpdateAt = Time.unscaledTime;
            initialized = true;
        }

        private void Start()
        {
            TimeChanged?.Invoke(hour);
            PhaseChanged?.Invoke(currentPhase);
        }

        private void Update()
        {
            float targetHour = GetTargetHour();
            float previousHour = hour;

            if (smoothingTime <= 0f || hasDebugTime)
            {
                hour = targetHour;
                angularVelocity = 0f;
            }
            else
            {
                float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float angle = Mathf.SmoothDampAngle(
                    hour * DegreesPerHour,
                    targetHour * DegreesPerHour,
                    ref angularVelocity,
                    smoothingTime,
                    Mathf.Infinity,
                    deltaTime);
                hour = NormalizeHour(angle / DegreesPerHour);
            }

            PublishChanges(previousHour);
        }

        public bool SetDebugTime(float newHour)
        {
            if (!IsValidHour(newHour))
            {
                return false;
            }

            float previousHour = hour;
            hasDebugTime = true;
            debugHour = newHour;
            hour = newHour;
            angularVelocity = 0f;
            PublishChanges(previousHour, true);
            return true;
        }

        public void ClearDebugTime()
        {
            if (!hasDebugTime)
            {
                return;
            }

            float previousHour = hour;
            hasDebugTime = false;
            hour = GetLocalTimeHour();
            angularVelocity = 0f;
            PublishChanges(previousHour, true);
        }

        public TimeOfDayPhase GetPhase(float hourValue)
        {
            float normalizedHour = NormalizeHour(hourValue);

            if (normalizedHour >= nightStartsAt || normalizedHour < earlyMorningStartsAt)
            {
                return TimeOfDayPhase.Night;
            }

            if (normalizedHour < dayStartsAt)
            {
                return TimeOfDayPhase.EarlyMorning;
            }

            if (normalizedHour < eveningStartsAt)
            {
                return TimeOfDayPhase.Day;
            }

            if (normalizedHour < closingStartsAt)
            {
                return TimeOfDayPhase.Evening;
            }

            return TimeOfDayPhase.Closing;
        }

        private float GetTargetHour()
        {
            return hasDebugTime ? debugHour : GetLocalTimeHour();
        }

        private static float GetLocalTimeHour()
        {
            DateTime now = DateTime.Now;
            return now.Hour
                + now.Minute / 60f
                + now.Second / 3600f
                + now.Millisecond / 3600000f;
        }

        private void PublishChanges(float previousHour, bool forceTimeEvent = false)
        {
            // Local time advances continuously, but applying the complete render look every
            // frame only rewrites light and material state. Four updates per second are
            // visually continuous for a real-world clock while explicit debug changes stay
            // immediate.
            bool timeChanged = !Mathf.Approximately(previousHour, hour);
            if (forceTimeEvent || (timeChanged && Time.unscaledTime >= nextVisualUpdateAt))
            {
                TimeChanged?.Invoke(hour);
                nextVisualUpdateAt = Time.unscaledTime + Mathf.Max(0.05f, visualUpdateInterval);
            }

            TimeOfDayPhase newPhase = GetPhase(hour);
            if (!initialized || newPhase != currentPhase)
            {
                currentPhase = newPhase;
                PhaseChanged?.Invoke(currentPhase);
            }
        }

        private static bool TryReadCommandLineTime(out float parsedHour)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (!argument.StartsWith(CommandLineArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = argument.Substring(CommandLineArgument.Length);
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedHour)
                    && IsValidHour(parsedHour))
                {
                    return true;
                }
            }

            parsedHour = 0f;
            return false;
        }

        private static bool IsValidHour(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value < HoursPerDay;
        }

        private static float NormalizeHour(float value)
        {
            float normalized = value % HoursPerDay;
            return normalized < 0f ? normalized + HoursPerDay : normalized;
        }

        private void OnValidate()
        {
            earlyMorningStartsAt = Mathf.Clamp(earlyMorningStartsAt, 0f, 24f);
            dayStartsAt = Mathf.Clamp(dayStartsAt, earlyMorningStartsAt, 24f);
            eveningStartsAt = Mathf.Clamp(eveningStartsAt, dayStartsAt, 24f);
            closingStartsAt = Mathf.Clamp(closingStartsAt, eveningStartsAt, 24f);
            nightStartsAt = Mathf.Clamp(nightStartsAt, closingStartsAt, 24f);
            smoothingTime = Mathf.Max(0f, smoothingTime);
            visualUpdateInterval = Mathf.Max(0.05f, visualUpdateInterval);
        }
    }
}
