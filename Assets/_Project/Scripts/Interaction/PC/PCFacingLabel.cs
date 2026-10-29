using UnityEngine;
namespace MRLab
{
    public sealed class PCFacingLabel : MonoBehaviour
    {
        private void LateUpdate() { if (Camera.main) transform.rotation = Camera.main.transform.rotation; }
    }
}
