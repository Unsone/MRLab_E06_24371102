using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MRLab
{
    public sealed class PCInteractionController : MonoBehaviour
    {
        public Camera viewCamera;
        public ElephantToothpasteExperiment experiment;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) experiment.ResetExperiment();
                if (keyboard.escapeKey.wasPressedThisFrame && !experiment.Session.IsAdding) experiment.CancelSelection();
                if (keyboard.digit1Key.wasPressedThisFrame) experiment.Select(experiment.reagents[0]);
                if (keyboard.digit2Key.wasPressedThisFrame) experiment.Select(experiment.reagents[1]);
                if (keyboard.digit3Key.wasPressedThisFrame) experiment.Select(experiment.reagents[2]);
                if (keyboard.digit4Key.wasPressedThisFrame) experiment.Select(experiment.reagents[3]);
                if (keyboard.enterKey.wasPressedThisFrame) experiment.AddSelected();
            }
            experiment.HoverName = "";
            var mouse = Mouse.current;
            if (mouse == null || (EventSystem.current && EventSystem.current.IsPointerOverGameObject())) return;
            if (Physics.Raycast(viewCamera.ScreenPointToRay(mouse.position.ReadValue()), out var hit, 20f))
            {
                var item = hit.collider.GetComponentInParent<LabItem>();
                if (item)
                {
                    experiment.HoverName = item.DisplayName;
                    if (mouse.leftButton.wasPressedThisFrame) experiment.Select(item);
                }
            }
        }
    }
}
