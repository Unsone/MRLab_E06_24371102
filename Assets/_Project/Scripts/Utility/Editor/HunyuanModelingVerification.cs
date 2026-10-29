using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MRLab.Editor
{
    public static class HunyuanModelingVerification
    {
        public static void Build()
        {
            Verify();
            string directory = "Builds/PC/AMDModeling_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/_Project/Scenes/PC/01_PC_Main.unity" },
                locationPathName = directory + "/MRLab_E06_24371102.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("AMD modeling PC build failed: " + report.summary.result);
            File.WriteAllText("SourceAssets/Models/Hunyuan3D/pc-build-path.txt", Path.GetFullPath(directory));
        }

        public static void Verify()
        {
            var mesh = HunyuanModelingPanel.ParseObj("v 0 0 0\nv 1 0 0\nv 0 1 0\nf -3/1 -2/2 -1/3\n");
            if (mesh.vertexCount != 3 || mesh.triangles.Length != 3 || mesh.vertices[1].x != -1)
                throw new Exception("OBJ coordinate/index conversion failed.");
            UnityEngine.Object.DestroyImmediate(mesh);
            bool invalidRejected = false;
            try { HunyuanModelingPanel.ParseObj("v 0 0 0\nf 1 2 3\n"); }
            catch (FormatException) { invalidRejected = true; }
            if (!invalidRejected) throw new Exception("Invalid OBJ accepted.");
            if (Shader.Find("Universal Render Pipeline/Lit") == null) throw new Exception("URP preview shader missing.");
            File.WriteAllText("SourceAssets/Models/Hunyuan3D/unity-verification.json",
                "{\"unity\":\"" + Application.unityVersion + "\",\"compile\":true,\"obj_parser\":true,\"urp_shader\":true}");
            Debug.Log("HUNYUAN_UNITY_VERIFIED");
        }
    }
}
