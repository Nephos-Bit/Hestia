using UnityEngine;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class AdaptiveOrthoCamera : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float referenceOrthographicSize = 8.2f;
        [SerializeField, Min(0.1f)] private float referenceAspect = 9f / 19.5f;
        [SerializeField, Range(0f, 1f)] private float widthProtection = 0.9f;

        private Camera cachedCamera;
        private int lastWidth;
        private int lastHeight;

        private void Awake()
        {
            cachedCamera = GetComponent<Camera>();
            cachedCamera.orthographic = true;
            Apply();
        }

        private void Update()
        {
            if (Screen.width != lastWidth || Screen.height != lastHeight)
            {
                Apply();
            }
        }

        public void Configure(float size, float aspect = 9f / 19.5f)
        {
            referenceOrthographicSize = size;
            referenceAspect = aspect;
            Apply();
        }

        private void Apply()
        {
            if (cachedCamera == null)
            {
                cachedCamera = GetComponent<Camera>();
            }

            lastWidth = Mathf.Max(1, Screen.width);
            lastHeight = Mathf.Max(1, Screen.height);
            float aspect = (float)lastWidth / lastHeight;
            float narrowScreenScale = referenceAspect / Mathf.Max(0.01f, aspect);
            float protectedScale = Mathf.Lerp(1f, Mathf.Max(1f, narrowScreenScale), widthProtection);
            cachedCamera.orthographicSize = referenceOrthographicSize * protectedScale;
        }
    }
}
