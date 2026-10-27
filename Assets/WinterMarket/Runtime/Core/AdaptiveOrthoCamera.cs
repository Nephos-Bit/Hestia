using UnityEngine;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class AdaptiveOrthoCamera : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float referenceOrthographicSize = 22f;
        [SerializeField, Min(0.1f)] private float referenceAspect = 9f / 19.5f;
        [SerializeField, Range(0f, 1f)] private float widthProtection = 0.9f;
        [SerializeField, Min(1f)] private float minimumOrthographicSize = 12f;
        [SerializeField, Min(1f)] private float maximumOrthographicSize = 42f;
        [SerializeField, Min(1f)] private float panLimit = 68f;

        private Camera cachedCamera;
        private int lastWidth;
        private int lastHeight;
        private float zoomMultiplier = 1f;
        private bool mousePanning;
        private Vector3 mousePanAnchor;
        private bool touchPanning;
        private Vector3 touchPanAnchor;
        private float pinchDistance;

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
            HandleNavigation();
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
            cachedCamera.orthographicSize = Mathf.Clamp(
                referenceOrthographicSize * protectedScale * zoomMultiplier,
                minimumOrthographicSize,
                maximumOrthographicSize);
        }

        private void HandleNavigation()
        {
            if (cachedCamera == null) return;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.001f)
            {
                zoomMultiplier = Mathf.Clamp(zoomMultiplier * (wheel > 0f ? 0.88f : 1.14f), 0.5f, 2f);
                Apply();
            }

            HandleMousePan();
            HandleKeyboardPan();
            HandleTouchNavigation();
        }

        private void HandleMousePan()
        {
            bool panButtonDown = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            bool panButtonHeld = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (panButtonDown && TryGetGroundPoint(Input.mousePosition, out Vector3 point))
            {
                mousePanning = true;
                mousePanAnchor = point;
            }
            if (mousePanning && panButtonHeld && TryGetGroundPoint(Input.mousePosition, out Vector3 current))
            {
                MoveByGroundDelta(mousePanAnchor - current);
            }
            if (!panButtonHeld) mousePanning = false;
        }

        private void HandleKeyboardPan()
        {
            Vector3 input = Vector3.zero;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) input.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) input.x += 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) input.z += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) input.z -= 1f;
            if (input.sqrMagnitude < 0.001f) return;

            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float speed = cachedCamera.orthographicSize * 1.2f * Time.unscaledDeltaTime;
            MoveByGroundDelta((right * input.x + forward * input.z).normalized * speed);
        }

        private void HandleTouchNavigation()
        {
            if (Input.touchCount != 2)
            {
                touchPanning = false;
                return;
            }

            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);
            Vector2 midpoint = (first.position + second.position) * 0.5f;
            float distance = Vector2.Distance(first.position, second.position);
            if (!touchPanning || first.phase == TouchPhase.Began || second.phase == TouchPhase.Began)
            {
                touchPanning = TryGetGroundPoint(midpoint, out touchPanAnchor);
                pinchDistance = Mathf.Max(1f, distance);
                return;
            }
            if (TryGetGroundPoint(midpoint, out Vector3 current)) MoveByGroundDelta(touchPanAnchor - current);
            if (distance > 1f && pinchDistance > 1f)
            {
                zoomMultiplier = Mathf.Clamp(zoomMultiplier * (pinchDistance / distance), 0.5f, 2f);
                Apply();
            }
            pinchDistance = Mathf.Max(1f, distance);
        }

        private bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
        {
            Ray ray = cachedCamera.ScreenPointToRay(screenPosition);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }
            point = default;
            return false;
        }

        private void MoveByGroundDelta(Vector3 delta)
        {
            Vector3 position = transform.position + new Vector3(delta.x, 0f, delta.z);
            position.x = Mathf.Clamp(position.x, -panLimit, panLimit);
            position.z = Mathf.Clamp(position.z, -panLimit, panLimit);
            transform.position = position;
        }
    }
}
