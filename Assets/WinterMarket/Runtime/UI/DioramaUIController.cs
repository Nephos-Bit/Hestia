using UnityEngine;
using System;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class DioramaUIController : MonoBehaviour
    {
        private const string DebugUiArgument = "-winterDebugUI";
        private static bool debugUiArgumentRead;
        private static bool debugUiRequested;

        [SerializeField, Min(1f)] private float hideAfterSeconds = 6f;
        [SerializeField] private QualityController quality;

        private float hideAt;
        private bool visible = true;
        private bool audioEnabled;

        /// <summary>
        /// The release diorama intentionally has no touch-revealed settings panel. Development
        /// captures can explicitly opt in with -winterDebugUI without changing normal viewing.
        /// </summary>
        public static bool IsDebugUiRequested
        {
            get
            {
                if (!debugUiArgumentRead)
                {
                    ReadDebugUiArgument();
                }

                return debugUiRequested;
            }
        }

        private static void ReadDebugUiArgument()
        {
            debugUiArgumentRead = true;
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], DebugUiArgument, StringComparison.OrdinalIgnoreCase))
                {
                    debugUiRequested = true;
                    return;
                }
            }
        }

        public void Configure(QualityController qualityController)
        {
            quality = qualityController;
        }

        private void Awake()
        {
            audioEnabled = LocalSettings.GetAudioVolume() > 0.01f;
            AudioListener.volume = audioEnabled ? LocalSettings.GetAudioVolume() : 0f;
            hideAt = Time.unscaledTime + hideAfterSeconds;
        }

        private void Update()
        {
            bool touched = Input.touchCount > 0
                || Input.GetMouseButtonDown(0)
                || Input.anyKeyDown;
            if (touched)
            {
                visible = true;
                hideAt = Time.unscaledTime + hideAfterSeconds;
            }
            else if (visible && Time.unscaledTime >= hideAt)
            {
                visible = false;
            }
        }

        private void OnGUI()
        {
            if (!IsDebugUiRequested || !visible)
            {
                return;
            }

            Rect safe = Screen.safeArea;
            float scale = Mathf.Max(1f, Screen.dpi > 0f ? Screen.dpi / 180f : 1f);
            float width = 116f * scale;
            float height = 78f * scale;
            float margin = 12f * scale;
            var area = new Rect(
                safe.xMax - width - margin,
                safe.y + margin,
                width,
                height);

            GUILayout.BeginArea(area, GUI.skin.box);
            if (quality != null)
            {
                string label = quality.CurrentQuality == QualityLevel.High ? "Qualität: Hoch" : "Qualität: Standard";
                if (GUILayout.Button(label, GUILayout.Height(28f * scale)))
                {
                    QualityLevel next = quality.CurrentQuality == QualityLevel.High
                        ? QualityLevel.Standard
                        : QualityLevel.High;
                    quality.SetQuality(next);
                    hideAt = Time.unscaledTime + hideAfterSeconds;
                }
            }

            if (GUILayout.Button(audioEnabled ? "Ton: An" : "Ton: Aus", GUILayout.Height(28f * scale)))
            {
                audioEnabled = !audioEnabled;
                float volume = audioEnabled ? 0.65f : 0f;
                LocalSettings.SetAudioVolume(volume);
                AudioListener.volume = volume;
                hideAt = Time.unscaledTime + hideAfterSeconds;
            }
            GUILayout.EndArea();
        }
    }
}
