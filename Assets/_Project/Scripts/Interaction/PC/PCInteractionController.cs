using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MRLab
{
    public sealed class PCInteractionController : MonoBehaviour
    {
        public Camera viewCamera;
        public ElephantToothpasteExperiment experiment;
        public float tableHeight = .89f;
        public Vector2 tableHalfSize = new Vector2(1.15f, .525f);
        public bool CanPlace(Vector3 point)
        {
            const float margin = .09f;
            if (Mathf.Abs(point.x) > tableHalfSize.x - margin || Mathf.Abs(point.z) > tableHalfSize.y - margin) return false;
            // Reserve a circular reaction area on the tabletop for the flask and foam.
            Vector3 vessel = experiment.pourTarget.parent.position;
            if (Vector2.Distance(new Vector2(point.x, point.z), new Vector2(vessel.x, vessel.z)) < .42f) return false;
            foreach (var other in experiment.reagents)
                if (other != experiment.Selected && Vector2.Distance(new Vector2(point.x, point.z), new Vector2(other.transform.position.x, other.transform.position.z)) < .18f) return false;
            return true;
        }
        public bool TryPlace(Vector3 point)
        {
            if (!CanPlace(point)) { experiment.Session.SetMessage("这里放不下，请选择反应区域外的空闲台面。"); return false; }
            point.y = tableHeight + .002f;
            return experiment.PlaceSelected(point);
        }
        public void FollowPointer(Ray ray)
        {
            if (!experiment.Selected || experiment.Session.IsAdding) return;
            var plane = new Plane(Vector3.up, new Vector3(0, tableHeight + .18f, 0));
            if (!plane.Raycast(ray, out float distance) || distance > 20) return;
            Vector3 point = ray.GetPoint(distance);
            point.x = Mathf.Clamp(point.x, -1.15f, 1.15f); point.z = Mathf.Clamp(point.z, -.65f, .65f);
            experiment.Selected.transform.position = point;
        }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) experiment.ResetExperiment();
                if (keyboard.escapeKey.wasPressedThisFrame) experiment.CancelSelection();
                if (keyboard.digit1Key.wasPressedThisFrame) experiment.Select(experiment.reagents[0]);
                if (keyboard.digit2Key.wasPressedThisFrame) experiment.Select(experiment.reagents[1]);
                if (keyboard.digit3Key.wasPressedThisFrame) experiment.Select(experiment.reagents[2]);
                if (keyboard.digit4Key.wasPressedThisFrame) experiment.Select(experiment.reagents[3]);
                if (keyboard.enterKey.wasPressedThisFrame) experiment.AddSelected();
            }
            experiment.HoverName = "";
            var mouse = Mouse.current;
            if (mouse == null || !viewCamera.pixelRect.Contains(mouse.position.ReadValue()) || (EventSystem.current && EventSystem.current.IsPointerOverGameObject())) return;
            Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
            FollowPointer(ray);
            LabItem target = null; float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(ray, 20f))
            {
                var item = hit.collider.GetComponentInParent<LabItem>();
                if (item && item != experiment.Selected && hit.distance < nearest) { target = item; nearest = hit.distance; }
            }
            if (target) experiment.HoverName = target.DisplayName;
            if (!mouse.leftButton.wasPressedThisFrame || mouse.rightButton.isPressed || mouse.middleButton.isPressed || experiment.Session.IsAdding) return;
            if (target) { experiment.Select(target); return; }
            if (!experiment.Selected) return;
            var table = new Plane(Vector3.up, new Vector3(0, tableHeight, 0));
            if (table.Raycast(ray, out float d) && d < 20) TryPlace(ray.GetPoint(d));
        }
    }
}
