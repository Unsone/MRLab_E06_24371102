using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace MRLab
{
    public sealed class PCInteractionVerification : MonoBehaviour
    {
        private void Check(bool condition, string message) { if (!condition) throw new Exception("INTERACTION_CHECK_FAILED: " + message); Debug.Log("INTERACTION_CHECK_PASS: " + message); }
        private IEnumerator Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--interaction-verify") < 0) yield break;
            Application.runInBackground = true;
            yield return null;
            var e = GetComponent<ElephantToothpasteExperiment>();
            var input = FindObjectOfType<PCInteractionController>(); var camera = Camera.main; var orbit = camera.GetComponent<PCOrbitCamera>();
            string output = Path.GetFullPath(Path.Combine(Application.dataPath,"..","InteractionVerification")); Directory.CreateDirectory(output);
            Check(orbit && input, "Desktop adapters wired");
            foreach (var item in e.reagents) Check(item.transform.Find("HunyuanVisual") && !item.transform.Find("Body").gameObject.activeSelf, "Generated beaker replaces primitive");
            Check(e.pourTarget.parent.Find("HunyuanVisual"), "Generated flask attached");
            Check(!GameObject.Find("_Experiment/Devices/PC_Demo/Tray"), "Tray removed from active scene");
            var support = new FoamDepositSurface(e.foam, e.FoamEffect.whiteMaterial, e.FoamEffect.dyedMaterial);
            Check(Mathf.Abs(e.foam.TransformPoint(new Vector3(0, support.Ground(0,0), 0)).y - .89f) < .0001f, "Foam rests on tabletop without tray offset");
            Check(Mathf.Abs(e.pourTarget.parent.position.y - .892f) < .0001f, "Flask aligned to tabletop");
            e.ResetExperiment(); var first = e.reagents[0]; Vector3 home = first.transform.position;
            e.Select(first); input.FollowPointer(new Ray(new Vector3(-.7f, 2, -.25f), Vector3.down));
            Check(Vector3.Distance(home, first.transform.position) > .1f && e.Selected == first, "Held beaker follows pointer ray");
            e.Select(e.reagents[1]); Check(e.Selected == first, "Cannot silently replace held cup");
            Check(!input.TryPlace(new Vector3(2, .89f, 0)), "Reject outside table");
            Check(!input.TryPlace(new Vector3(0, .89f, .1f)), "Reject occupied reaction area");
            Check(!input.TryPlace(e.reagents[1].transform.position), "Reject occupied beaker footprint");
            e.AddSelected(); yield return new WaitForSeconds(1.9f);
            Check(e.Session.Has(Reagent.Peroxide) && e.Selected == first && !e.Session.IsAdding, "Pour commits and keeps cup held");
            Check(!first.transform.Find("ReagentLiquid").gameObject.activeSelf, "Used beaker visibly empty");
            e.AddSelected(); Check(e.Session.Step == 1 && !e.Session.IsAdding, "Empty cup cannot add twice");
            Check(input.TryPlace(new Vector3(-.85f,.89f,-.30f)) && !e.Selected, "Place empty cup on free table");
            e.Select(first); e.CancelSelection(); Check(!e.Selected && Vector3.Distance(first.transform.position,new Vector3(-.85f,.892f,-.30f)) < .001f, "Cancel restores latest pickup position");
            e.Select(e.reagents[1]); e.AddSelected(); yield return new WaitForSeconds(.2f); e.ResetExperiment();
            Check(!e.Selected && !e.Session.IsAdding && e.Session.Step == 0 && !e.stream.enabled, "Reset interrupts pour safely");
            foreach (var item in e.reagents) Check(item.transform.Find("ReagentLiquid").gameObject.activeSelf, "Reset refills cup");
            Vector3 original = camera.transform.position;
            orbit.Orbit(new Vector2(500, 100)); orbit.Zoom(200);
            Check(Vector3.Distance(original,camera.transform.position) > .5f, "Orbit and zoom change actual camera");
            Vector3 mouth = e.pourTarget.position - Vector3.up * .05f;
            Ray pick = camera.ScreenPointToRay(camera.WorldToScreenPoint(mouth));
            bool vesselHit = false;
            foreach (var hit in Physics.RaycastAll(pick,20)) { var item = hit.collider.GetComponentInParent<LabItem>(); if(item && item.isVessel) vesselHit = true; }
            Check(vesselHit, "Vessel remains pickable from orbited viewpoint");
            yield return new WaitForEndOfFrame(); Capture(Path.Combine(output,"02_Orbit.png")); yield return new WaitForSeconds(.5f);
            orbit.ResetView(); Check(Vector3.Distance(original,camera.transform.position)<.001f, "Home restores view");
            yield return new WaitForEndOfFrame(); Capture(Path.Combine(output,"01_Ready.png")); yield return new WaitForSeconds(.5f);
            foreach (int index in new[]{1,0,3})
            { e.Select(e.reagents[index]); e.AddSelected(); yield return new WaitForSeconds(1.9f); Check(e.Selected == e.reagents[index], "Cup retained after reagent " + index); e.CancelSelection(); }
            Check(e.Session.Stage == ExperimentStage.Reacting && e.FoamEffect.PointCount > 0, "Chemistry and foam still run through new flow");
            yield return new WaitForSeconds(1);
            yield return new WaitForEndOfFrame(); Capture(Path.Combine(output,"03_Reaction.png")); yield return new WaitForSeconds(.5f);
            File.WriteAllText(Path.Combine(output,"PASS.txt"),"FREE_INTERACTION_RUNTIME_PASSED\n"+DateTime.Now);
            Debug.Log("FREE_INTERACTION_RUNTIME_PASSED"); Application.Quit(0);
        }
        private void Capture(string path)
        {
            var camera = Camera.main; Rect rect = camera.rect; var active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1232,900,24,RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1232,900,TextureFormat.RGB24,false);
            try
            {
                camera.rect = new Rect(0,0,1,1);
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,1232,900),0,0); pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally { camera.rect=rect; RenderTexture.active=active; RenderTexture.ReleaseTemporary(target); Destroy(pixels); }
        }
    }
}
