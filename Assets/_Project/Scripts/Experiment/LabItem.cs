using UnityEngine;

namespace MRLab
{
    public sealed class LabItem : MonoBehaviour
    {
        public bool isVessel;
        public Reagent reagent;
        public GameObject selectionRing;
        public Transform pourSpout;
        public string DisplayName => isVessel ? "反应容器" : ExperimentSession.ReagentNames[(int)reagent];
        public void SetSelected(bool selected) { if (selectionRing) selectionRing.SetActive(selected); }
    }
}
