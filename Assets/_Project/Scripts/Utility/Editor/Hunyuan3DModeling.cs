using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MRLab.Editor
{
    /// <summary>Editor bridge to the project's local image-to-3D program.</summary>
    public static class Hunyuan3DModeling
    {
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        [MenuItem("MRLab/AI Modeling/Start Hunyuan3D")]
        public static void StartGenerator()
        {
            var launcher = Path.Combine(ProjectRoot, "GenerateModel.cmd");
            if (!File.Exists(launcher))
            {
                EditorUtility.DisplayDialog("Hunyuan3D", "GenerateModel.cmd was not found in the project root.", "OK");
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = launcher,
                WorkingDirectory = ProjectRoot,
                UseShellExecute = true
            });
        }

        [MenuItem("MRLab/AI Modeling/Open Generated Models")]
        public static void OpenOutputs()
        {
            var folder = Path.Combine(ProjectRoot, "SourceAssets", "Models", "Hunyuan3D", "Outputs");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }
    }
}
