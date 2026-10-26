using UnityEngine;

namespace WinterMarket
{
    public static class LocalSettings
    {
        public const string QualityKey = "WinterMarket.Quality";
        public const string AudioVolumeKey = "WinterMarket.AudioVolume";
        public const string DeterministicSeedKey = "WinterMarket.DeterministicSeed";

        public const float DefaultAudioVolume = 1f;

        public static QualityLevel Quality
        {
            get => GetQuality();
            set => SetQuality(value);
        }

        public static float AudioVolume
        {
            get => PlayerPrefs.GetFloat(AudioVolumeKey, DefaultAudioVolume);
            set
            {
                PlayerPrefs.SetFloat(AudioVolumeKey, Mathf.Clamp01(value));
                PlayerPrefs.Save();
            }
        }

        public static bool HasDeterministicSeed => PlayerPrefs.HasKey(DeterministicSeedKey);

        public static int DeterministicSeed
        {
            get => GetDeterministicSeed();
            set => SetDeterministicSeed(value);
        }

        public static QualityLevel GetQuality(QualityLevel fallback = QualityLevel.Standard)
        {
            int storedValue = PlayerPrefs.GetInt(QualityKey, (int)fallback);
            return storedValue == (int)QualityLevel.High ? QualityLevel.High : QualityLevel.Standard;
        }

        public static void SetQuality(QualityLevel quality)
        {
            QualityLevel safeValue = quality == QualityLevel.High
                ? QualityLevel.High
                : QualityLevel.Standard;
            PlayerPrefs.SetInt(QualityKey, (int)safeValue);
            PlayerPrefs.Save();
        }

        public static float GetAudioVolume(float fallback = DefaultAudioVolume)
        {
            return Mathf.Clamp01(PlayerPrefs.GetFloat(AudioVolumeKey, Mathf.Clamp01(fallback)));
        }

        public static void SetAudioVolume(float volume)
        {
            AudioVolume = volume;
        }

        public static int GetDeterministicSeed(int fallback = 0)
        {
            return PlayerPrefs.GetInt(DeterministicSeedKey, fallback);
        }

        public static void SetDeterministicSeed(int seed)
        {
            PlayerPrefs.SetInt(DeterministicSeedKey, seed);
            PlayerPrefs.Save();
        }

        public static void ClearDeterministicSeed()
        {
            PlayerPrefs.DeleteKey(DeterministicSeedKey);
            PlayerPrefs.Save();
        }
    }
}
