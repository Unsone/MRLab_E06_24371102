using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MRLab
{
    // Desktop camera adapter. VR/CAVE cameras can use the same experiment without this component.
    [DefaultExecutionOrder(-100)]
    public sealed class PCOrbitCamera : MonoBehaviour
    {
        public Camera viewCamera;
        public Vector3 focus = new Vector3(0, 1.12f, .1f);
        private Vector3 initialFocus;
        private float yaw, pitch, distance, initialYaw, initialPitch, initialDistance;
        private void Start()
        {
            if (!viewCamera) viewCamera = GetComponent<Camera>();
            Vector3 offset = transform.position - focus;
            distance = offset.magnitude; pitch = Mathf.Asin(offset.y / distance) * Mathf.Rad2Deg;
            yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            initialFocus = focus; initialYaw = yaw; initialPitch = pitch; initialDistance = distance;
        }
        public void Orbit(Vector2 delta) { yaw += delta.x * .22f; pitch = Mathf.Clamp(pitch - delta.y * .22f, 12, 80); Apply(); }
        public void Zoom(float delta) { distance = Mathf.Clamp(distance * Mathf.Exp(-delta * .0015f), .8f, 4.5f); Apply(); }
        public void ResetView() { focus = initialFocus; yaw = initialYaw; pitch = initialPitch; distance = initialDistance; Apply(); }
        private void Apply() { transform.position = focus + Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward * distance; transform.LookAt(focus); }
        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.homeKey.wasPressedThisFrame) ResetView();
            var mouse = Mouse.current;
            if (mouse == null || !viewCamera.pixelRect.Contains(mouse.position.ReadValue()) || (EventSystem.current && EventSystem.current.IsPointerOverGameObject())) return;
            Vector2 delta = mouse.delta.ReadValue();
            if (mouse.rightButton.isPressed) Orbit(delta);
            if (mouse.middleButton.isPressed)
            {
                Vector3 right = transform.right; right.y = 0; right.Normalize();
                Vector3 forward = Vector3.Cross(right, Vector3.up);
                focus -= (right * delta.x + forward * delta.y) * distance * .0007f;
                focus.x = Mathf.Clamp(focus.x, -.55f, .55f); focus.z = Mathf.Clamp(focus.z, -.35f, .5f);
                Apply();
            }
            Zoom(mouse.scroll.ReadValue().y);
        }
    }
}
