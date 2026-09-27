using System.Collections;
using UnityEngine;

namespace MRLab
{
    public sealed class ElephantToothpasteExperiment : MonoBehaviour
    {
        public LabItem[] reagents;
        public Transform pourTarget;
        public Transform liquid;
        public Renderer liquidRenderer;
        public Transform foam;
        public ParticleSystem bubbles;
        public LineRenderer stream;
        public ExperimentSession Session { get; private set; }
        public LabItem Selected { get; private set; }
        public string HoverName { get; set; }
        public ViscousFoam FoamEffect { get; private set; }
        private Vector3[] initialPositions;
        private Quaternion[] initialRotations;
        private MaterialPropertyBlock liquidProperties;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Session = new ExperimentSession();
            FoamEffect = foam.GetComponent<ViscousFoam>();
            initialPositions = new Vector3[reagents.Length];
            initialRotations = new Quaternion[reagents.Length];
            for (int i = 0; i < reagents.Length; i++)
            {
                initialPositions[i] = reagents[i].transform.position;
                initialRotations[i] = reagents[i].transform.rotation;
            }
            liquidProperties = new MaterialPropertyBlock();
            ResetExperiment();
        }

        private void Update()
        {
            Session.Tick(Time.deltaTime);
            FoamEffect.SetProduction(Session.FoamAmount, Session.Has(Reagent.Color),
                Session.Stage == ExperimentStage.Reacting && Session.Has(Reagent.Soap));
            bool gasVisible = Session.Stage == ExperimentStage.Reacting && !Session.Has(Reagent.Soap);
            if (gasVisible)
            {
                var emission = bubbles.emission;
                emission.rateOverTime = 10 + 80 * Session.RemainingPeroxide;
                if (!bubbles.isPlaying) bubbles.Play();
            }
            else if (bubbles.isPlaying) bubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void Select(LabItem item)
        {
            if (!item) return;
            if (item.isVessel) { AddSelected(); return; }
            if (Session.IsAdding) { Session.SetMessage("请等当前试剂瓶放回后再选择。"); return; }
            if (Selected) Selected.SetSelected(false);
            Selected = item;
            Selected.SetSelected(true);
            Session.SetMessage("已选中" + item.DisplayName + "，点击中央反应容器添加。");
        }

        public void CancelSelection()
        {
            if (Selected) Selected.SetSelected(false);
            Selected = null;
            Session.SetMessage("已取消选择。点击试剂瓶重新选择。");
        }

        public void AddSelected()
        {
            if (!Selected) { Session.SetMessage("请先点击一个试剂瓶，或按数字键 1—4 选择。"); return; }
            if (Session.TryAdd(Selected.reagent)) StartCoroutine(Pour(Selected));
        }

        private IEnumerator Pour(LabItem item)
        {
            var sourcePosition = item.transform.position;
            var sourceRotation = item.transform.rotation;
            // The spout is the anchor: replacing a mesh does not change the pouring destination.
            var rotation = Quaternion.Euler(0, 0, -65);
            var spoutOffset = Quaternion.Inverse(sourceRotation) * (item.pourSpout.position - sourcePosition);
            var destination = pourTarget.position + Vector3.up * .15f - rotation * spoutOffset;
            yield return MoveItem(item.transform, sourcePosition, destination, sourceRotation, rotation, .55f);
            stream.enabled = true;
            stream.SetPosition(0, item.pourSpout.position);
            stream.SetPosition(1, pourTarget.position - Vector3.up * .12f);
            Session.CommitAddition();
            UpdateLiquid();
            yield return new WaitForSeconds(.7f);
            stream.enabled = false;
            yield return MoveItem(item.transform, destination, sourcePosition, rotation, sourceRotation, .45f);
            item.SetSelected(false);
            Selected = null;
            Session.FinishAddition();
            UpdateLiquid();
        }

        private static IEnumerator MoveItem(Transform item, Vector3 from, Vector3 to, Quaternion a, Quaternion b, float duration)
        {
            for (float elapsed = 0; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                item.SetPositionAndRotation(Vector3.Lerp(from, to, t), Quaternion.Slerp(a, b, t));
                yield return null;
            }
            item.SetPositionAndRotation(to, b);
        }

        private void UpdateLiquid()
        {
            liquid.gameObject.SetActive(Session.Step > 0);
            float height = .022f + Session.Step * .008f;
            liquid.localPosition = new Vector3(0, height, 0);
            liquid.localScale = new Vector3(.14f, height, .14f);
            liquidProperties.SetColor("_BaseColor", Session.Has(Reagent.Color) ? new Color(.12f, .65f, .6f) : new Color(.8f, .88f, .87f));
            liquidRenderer.SetPropertyBlock(liquidProperties);
        }

        public void ResetExperiment()
        {
            StopAllCoroutines();
            Selected = null;
            HoverName = "";
            for (int i = 0; i < reagents.Length; i++)
            {
                reagents[i].transform.SetPositionAndRotation(initialPositions[i], initialRotations[i]);
                reagents[i].SetSelected(false);
            }
            bubbles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foam.gameObject.SetActive(true);
            foam.localScale = Vector3.one;
            FoamEffect.ResetFoam();
            stream.enabled = false;
            Session.Reset();
            UpdateLiquid();
        }
    }
}
