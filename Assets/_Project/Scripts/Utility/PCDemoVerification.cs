using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MRLab
{
    // Explicit command-line verification; never runs during ordinary play.
    public sealed class PCDemoVerification : MonoBehaviour
    {
        public ElephantToothpasteExperiment experiment;
        private string output;
        private IEnumerator Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--demo-verify") < 0) yield break;
            Application.runInBackground = true;
            output = Path.GetFullPath(Path.Combine(Application.dataPath,"..","Verification"));
            Directory.CreateDirectory(output);
            yield return null;
            experiment.CancelSelection(); experiment.Select(experiment.reagents[3]); experiment.AddSelected();
            yield return new WaitForSeconds(.25f);
            experiment.ResetExperiment();
            yield return new WaitForSeconds(2);
            if(experiment.Session.Step!=0 || experiment.Session.IsAdding || experiment.stream.enabled) { Fail("Reset during pour"); yield break; }

            // Reversed order, no dye. Verify actual pouring callbacks and foam presentation.
            foreach(int index in new[]{3,1,0})
            {
                experiment.CancelSelection(); experiment.Select(experiment.reagents[index]); experiment.AddSelected();
                yield return new WaitForSeconds(1.8f);
                if(!experiment.Session.Has((Reagent)index)) { Fail("Any-order pour"); yield break; }
            }
            if(experiment.Session.Has(Reagent.Color) || experiment.FoamEffect.PointCount<2) { Fail("White foam without dye"); yield break; }
            Capture("01_WhiteBurst");
            float peak=0,maxBreak=0;
            Vector3[] earlyPositions=null;
            bool[] earlyDeposits=null;
            float earlyMotion=0;
            for(int frame=0;frame<40;frame++)
            {
                peak=Mathf.Max(peak,experiment.FoamEffect.HighestPoint);
                if(frame<12) maxBreak=Mathf.Max(maxBreak,experiment.FoamEffect.DisconnectedFraction);
                if(frame==12)
                {
                    var current=experiment.FoamEffect;
                    earlyPositions=new Vector3[current.PointCount];earlyDeposits=new bool[current.PointCount];
                    for(int i=0;i<earlyPositions.Length;i++) {earlyPositions[i]=current.PointPosition(i);earlyDeposits[i]=current.IsDeposited(i);}
                }
                if(frame==22)
                    for(int i=0;i<earlyPositions.Length;i++)
                        if(earlyDeposits[i] && experiment.FoamEffect.IsDeposited(i)) earlyMotion=Mathf.Max(earlyMotion,Vector3.Distance(earlyPositions[i],experiment.FoamEffect.PointPosition(i)));
                if(frame==5 || frame==12 || frame==22) Capture("01_BurstFrame_"+frame);
                yield return new WaitForSeconds(.1f);
            }
            Capture("02_WhiteFalling");
            Debug.Log("JET_CONNECTIVITY disconnectedFraction="+maxBreak);
            Debug.Log("EARLY_SETTLING displacement="+earlyMotion);
            if(maxBreak>.02f) { Fail("Continuous main jet"); yield break; }
            if(earlyMotion<.003f) { Fail("New deposits initially deform before coming to rest"); yield break; }
            yield return new WaitForSeconds(4);
            Capture("03_WhitePile");
            yield return new WaitForSeconds(.2f);
            Debug.Log("FOAM_METRICS peak="+peak+" deposited="+experiment.FoamEffect.DepositedFraction+" points="+experiment.FoamEffect.PointCount+" pileHeight="+experiment.FoamEffect.PileHeight+" quadrants="+experiment.FoamEffect.OccupiedQuadrants);
            if(experiment.Session.Stage!=ExperimentStage.Completed || experiment.FoamEffect.DepositedFraction<.98f || peak<.9f || peak>2.3f || experiment.FoamEffect.PileHeight<.25f || experiment.FoamEffect.PileHeight>.8f) { Fail("Cohesive burst settles into a substantial mound, not a needle"); yield break; }

            var effect = experiment.FoamEffect;
            float centreBefore=effect.DepositedCentreHeight;
            var frozen = new Vector3[effect.PointCount];
            var wasDeposited = new bool[effect.PointCount];
            for (int i=0; i<frozen.Length; i++) { frozen[i]=effect.PointPosition(i); wasDeposited[i]=effect.IsSupported(i) && effect.TimeSinceContact(i)>=4; }
            yield return new WaitForSeconds(2);
            float drift=0;
            int measured=0;
            for (int i=0; i<frozen.Length; i++)
                if (wasDeposited[i] && effect.IsSupported(i)) { measured++;drift=Mathf.Max(drift,Vector3.Distance(frozen[i],effect.PointPosition(i))); }
            // Unsupported parcels may genuinely fall off the table; they must not be
            // mistaken for jitter in the supported mound.
            Debug.Log("DEPOSIT_METRICS supportedDrift="+drift+" matureSamples="+measured+" centreDrop="+(centreBefore-effect.DepositedCentreHeight)+" maxStep="+effect.MaximumStep+" meshVertices="+effect.DepositVertexCount+" outsideTray="+effect.OutsideTrayCount+" floor="+effect.FloorDepositCount);
            float spillFraction=effect.OutsideTrayVolume/effect.EmittedVolume;
            Debug.Log("VOLUME_METRICS emitted="+effect.EmittedVolume+" expansion="+effect.ExpansionVolume+" deposited="+effect.DepositedVolume+" airborne="+effect.AirborneVolume+" mesh="+effect.MeshVolume+" meshError="+effect.VolumeError+" spillFraction="+spillFraction+" nearThickness="+effect.NearThickness+" outerThickness="+effect.OuterThickness+" baseDeviation="+effect.BaseJetDeviation+" burstDescent="+effect.LongestBurstDescent);
            if (centreBefore-effect.DepositedCentreHeight<-.002f || measured==0 || drift>.12f || effect.MaximumStep>.02f || effect.DepositVertexCount==0) { Fail("Gravity settlement slows without jitter"); yield break; }
            // Conservative solid-boundary transfer introduces voxel-surface error;
            // accept at most 7% against the separately recorded expanded volume.
            if(Mathf.Abs(effect.EmittedVolume-ViscousFoam.FullBatchVolume)>.0001f || Mathf.Abs(effect.DepositedVolume-effect.EmittedVolume)>.0001f || effect.VolumeError>.07f) { Fail("Actual rendered volume conservation"); yield break; }
            if(spillFraction>.15f || effect.NearThickness<effect.OuterThickness*1.2f || effect.BaseJetDeviation>.01f) { Fail("Compact near-flask mound and vertical initial jet"); yield break; }
            if (effect.SupportHeight(1.4f,0)>-.8f) { Fail("Outside table must fall to floor"); yield break; }
            Capture("03b_SlowlyCollapsedPile");
            yield return new WaitForSeconds(12);
            Capture("03c_SettledPile");
            for(int i=0;i<frozen.Length;i++) frozen[i]=effect.PointPosition(i);
            float volumeBefore=effect.ExpandedVolume;
            yield return new WaitForSeconds(3);
            float settledDrift=0;
            for(int i=0;i<frozen.Length;i++) settledDrift=Mathf.Max(settledDrift,Vector3.Distance(frozen[i],effect.PointPosition(i)));
            Debug.Log("SETTLED_METRICS drift="+settledDrift+" expansionRatio="+effect.ExpansionVolume/effect.EmittedVolume+" volumeChange="+Mathf.Abs(effect.ExpandedVolume-volumeBefore)+" crestDropDuration="+effect.CrestDropDuration);
            if(settledDrift>.000001f || Mathf.Abs(effect.ExpandedVolume-volumeBefore)>.000001f || Mathf.Abs(effect.ExpansionVolume/effect.EmittedVolume-ViscousFoam.FinalExpansion)>.0001f) { Fail("Foam finishes swelling and comes to rest"); yield break; }
            if(effect.LongestBurstDescent>.3f || effect.CrestDropDuration<=0 || effect.CrestDropDuration>.65f) { Fail("Whole crest returns promptly after the burst apex"); yield break; }
            Capture("03d_StillAfterThreeSeconds");
            effect.RefreshSurface(false);
            float unswollenReference=effect.MeshVolume;
            Capture("03e_WithoutExpansionReference");
            effect.RefreshSurface();
            Capture("03f_ExpandedReference");
            float visibleSwell=effect.MeshVolume/unswollenReference;
            Debug.Log("SWELL_RENDER_RATIO expandedToBase="+visibleSwell);
            if(visibleSwell<1.15f) { Fail("Expansion must visibly increase rendered volume"); yield break; }

            // No soap: gas, not a stable toothpaste column. Adding spent-batch soap must not restart it.
            experiment.ResetExperiment();
            foreach(int index in new[]{0,3}) { experiment.CancelSelection(); experiment.Select(experiment.reagents[index]); experiment.AddSelected(); yield return new WaitForSeconds(1.8f); }
            if(!experiment.bubbles.isPlaying || experiment.FoamEffect.PointCount!=0) { Fail("Gas only"); yield break; }
            Capture("04_GasOnly");
            yield return new WaitForSeconds(12);
            experiment.CancelSelection(); experiment.Select(experiment.reagents[1]); experiment.AddSelected(); yield return new WaitForSeconds(2);
            if(experiment.Session.FoamAmount!=0) { Fail("Spent batch soap"); yield break; }

            experiment.ResetExperiment();
            foreach(int index in new[]{2,1,3,0}) { experiment.CancelSelection(); experiment.Select(experiment.reagents[index]); experiment.AddSelected(); yield return new WaitForSeconds(1.8f); }
            Capture("05_DyedBurst");
            yield return new WaitForSeconds(3);
            Capture("06_DyedFalling");
            yield return new WaitForSeconds(.2f);
            experiment.ResetExperiment(); yield return new WaitForFixedUpdate();
            if(experiment.FoamEffect.PointCount!=0 || experiment.bubbles.particleCount!=0 || experiment.FoamEffect.DepositVertexCount!=0) { Fail("Reset effects"); yield break; }
            Capture("07_Reset");
            Debug.Log("PC_DEMO_PLAYER_VERIFICATION_PASSED: arbitrary order, optional dye, white foam, falling pile, gas without soap, spent batch, reset.");
            yield return new WaitForSeconds(1);
            Application.Quit(0);
        }
        private void Capture(string name)
        {
            // Explicit scene render also works when the automated player's window is hidden.
            // Screen-space overlay HUD is intentionally excluded from these visual tests.
            var camera = Camera.main;
            Rect rect = camera.rect;
            var active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1232,900,24,RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1232,900,TextureFormat.RGB24,false);
            try
            {
                camera.rect = new Rect(0,0,1,1);
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1232,900),0,0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,name+".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.rect=rect; RenderTexture.active=active;
                RenderTexture.ReleaseTemporary(target); Destroy(pixels);
            }
        }
        private static void Fail(string reason) { Debug.LogError("PC_DEMO_VERIFICATION_FAILED: "+reason); Application.Quit(1); }
    }
}

