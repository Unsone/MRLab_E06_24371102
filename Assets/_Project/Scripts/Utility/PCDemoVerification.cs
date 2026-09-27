using System;
using System.Collections;
using System.IO;
using UnityEngine;

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
            experiment.Select(experiment.reagents[3]); experiment.AddSelected();
            yield return new WaitForSeconds(.25f);
            experiment.ResetExperiment();
            yield return new WaitForSeconds(2);
            if(experiment.Session.Step!=0 || experiment.Session.IsAdding || experiment.stream.enabled) { Fail("Reset during pour"); yield break; }

            // Reversed order, no dye. Verify actual pouring callbacks and foam presentation.
            foreach(int index in new[]{3,1,0})
            {
                experiment.Select(experiment.reagents[index]); experiment.AddSelected();
                yield return new WaitForSeconds(1.8f);
                if(!experiment.Session.Has((Reagent)index)) { Fail("Any-order pour"); yield break; }
            }
            if(experiment.Session.Has(Reagent.Color) || experiment.FoamEffect.PointCount<2) { Fail("White foam without dye"); yield break; }
            Capture("01_WhiteBurst");
            float peak=0;
            for(float t=0;t<4;t+=.1f) { peak=Mathf.Max(peak,experiment.FoamEffect.HighestPoint); yield return new WaitForSeconds(.1f); }
            Capture("02_WhiteFalling");
            yield return new WaitForSeconds(10);
            Capture("03_WhitePile");
            yield return new WaitForSeconds(.2f);
            Debug.Log("FOAM_METRICS peak="+peak+" deposited="+experiment.FoamEffect.DepositedFraction+" points="+experiment.FoamEffect.PointCount);
            if(experiment.Session.Stage!=ExperimentStage.Completed || experiment.FoamEffect.PointCount<180 || experiment.FoamEffect.DepositedFraction<.45f) { Fail("Foam settles into pile"); yield break; }

            // No soap: gas, not a stable toothpaste column. Adding spent-batch soap must not restart it.
            experiment.ResetExperiment();
            foreach(int index in new[]{0,3}) { experiment.Select(experiment.reagents[index]); experiment.AddSelected(); yield return new WaitForSeconds(1.8f); }
            if(!experiment.bubbles.isPlaying || experiment.FoamEffect.PointCount!=0) { Fail("Gas only"); yield break; }
            Capture("04_GasOnly");
            yield return new WaitForSeconds(12);
            experiment.Select(experiment.reagents[1]); experiment.AddSelected(); yield return new WaitForSeconds(2);
            if(experiment.Session.FoamAmount!=0) { Fail("Spent batch soap"); yield break; }

            experiment.ResetExperiment();
            foreach(int index in new[]{2,1,3,0}) { experiment.Select(experiment.reagents[index]); experiment.AddSelected(); yield return new WaitForSeconds(1.8f); }
            Capture("05_DyedBurst");
            yield return new WaitForSeconds(3);
            Capture("06_DyedFalling");
            yield return new WaitForSeconds(.2f);
            experiment.ResetExperiment(); yield return new WaitForFixedUpdate();
            if(experiment.FoamEffect.PointCount!=0 || experiment.bubbles.particleCount!=0) { Fail("Reset effects"); yield break; }
            Capture("07_Reset");
            Debug.Log("PC_DEMO_PLAYER_VERIFICATION_PASSED: arbitrary order, optional dye, white foam, falling pile, gas without soap, spent batch, reset.");
            yield return new WaitForSeconds(1);
            Application.Quit(0);
        }
        private void Capture(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png")); }
        private static void Fail(string reason) { Debug.LogError("PC_DEMO_VERIFICATION_FAILED: "+reason); Application.Quit(1); }
    }
}
