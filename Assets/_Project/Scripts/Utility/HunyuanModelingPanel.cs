using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

namespace MRLab
{
    // Separate modeling preview: experiment objects and interaction references stay intact.
    public sealed class HunyuanModelingPanel : MonoBehaviour
    {
        private const string Service = "http://127.0.0.1:7861";
        private bool visible, busy;
        private string message = "打开本地工具选择参考图，完成后可载入原始网格。";
        private GameObject previewRoot;
        private Mesh previewMesh;
        private Material previewMaterial;
        private RenderTexture previewTexture;
        private Font panelFont;
        private float rotation = 25f;

        [Serializable] private class Status { public string status; public string error; }
        [Serializable] private class Verification { public bool preview; public int vertices; public string message; }

        private IEnumerator Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--hunyuan-verify") < 0) yield break;
            Application.runInBackground = true;
            yield return new WaitForSecondsRealtime(3);
            visible = true;
            yield return LoadPreview();
            string directory = Path.GetFullPath("SourceAssets/Models/Hunyuan3D");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "unity-runtime.json"), JsonUtility.ToJson(new Verification {
                preview = previewMesh != null && previewTexture != null,
                vertices = previewMesh ? previewMesh.vertexCount : 0, message = message
            }, true));
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "unity-runtime.png"));
            yield return new WaitForSecondsRealtime(3);
            Application.Quit(previewMesh && previewTexture ? 0 : 1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (SceneManager.GetActiveScene().name == "01_PC_Main" && !FindObjectOfType<HunyuanModelingPanel>())
                new GameObject("Hunyuan3D_Modeling").AddComponent<HunyuanModelingPanel>();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) visible = !visible;
            if (previewRoot && previewMesh)
            {
                var visual = previewRoot.transform.GetChild(0);
                visual.localRotation = Quaternion.Euler(0, rotation, 0);
                visual.localPosition = -(visual.localRotation * previewMesh.bounds.center);
            }
        }

        private void OnGUI()
        {
            if (!panelFont) panelFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 15);
            var previousFont = GUI.skin.font;
            GUI.skin.font = panelFont;
            if (GUI.Button(new Rect(Screen.width - 166, 12, 154, 32), "AI 建模 / F8")) visible = !visible;
            if (!visible) { GUI.skin.font = previousFont; return; }
            var wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true };
            float width = Mathf.Min(370, Screen.width - 20);
            GUILayout.BeginArea(new Rect(Screen.width - width - 10, 52, width, Mathf.Min(560, Screen.height - 62)), GUI.skin.box);
            GUILayout.Label("Hunyuan3D-2.1 · AMD DirectML");
            if (GUILayout.Button("打开本地建模工具", GUILayout.Height(32))) OpenGenerator();
            GUI.enabled = !busy;
            if (GUILayout.Button("载入本次生成结果预览", GUILayout.Height(32))) StartCoroutine(LoadPreview());
            GUI.enabled = true;
            GUILayout.Label(message, wrapped);
            if (previewTexture)
            {
                var previewRect = GUILayoutUtility.GetRect(width - 18, 280);
                GUI.DrawTexture(previewRect, previewTexture, ScaleMode.ScaleToFit, false);
                rotation = GUILayout.HorizontalSlider(rotation, -180, 180);
                GUILayout.Label("原始形状预览；尚未进行 Blender 优化。\n实验器材替换需另行核对尺寸、瓶口和碰撞体。", wrapped);
            }
            GUILayout.EndArea();
            GUI.skin.font = previousFont;
        }

        public void OpenGenerator()
        {
            try
            {
                // Locate the accompanying source project from either Editor or Builds/PC/<version>.
                var directory = new DirectoryInfo(Application.dataPath);
                string tool = null;
                for (int i = 0; directory != null && i < 7; i++, directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, "SourceAssets", "Models", "Hunyuan3D");
                    if (File.Exists(Path.Combine(candidate, "app.py"))) { tool = candidate; break; }
                }
                if (tool == null) throw new IOException("找不到建模工具，请从完整课程工程运行。");
                string python = Path.Combine(tool, ".runtime", "venv", "Scripts", "python.exe");
                if (!File.Exists(python)) throw new IOException("请先运行工程根目录 GenerateModel.cmd 完成安装。");
                // A running service is reused; a duplicate process simply fails to bind the port.
                Process.Start(new ProcessStartInfo {
                    FileName = python, Arguments = "-u \"" + Path.Combine(tool, "app.py") + "\" --no-browser",
                    WorkingDirectory = tool, UseShellExecute = false, CreateNoWindow = true
                });
                StartCoroutine(OpenBrowser());
                message = "正在启动本地服务，生成期间可在网页查看进度。";
            }
            catch (Exception error) { message = error.Message; }
        }

        private IEnumerator OpenBrowser()
        {
            for (int i = 0; i < 30; i++)
            {
                using (var request = UnityWebRequest.Get(Service + "/api/status"))
                {
                    request.timeout = 2;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Application.OpenURL(Service);
                        message = "工具已打开。生成完成后点击载入预览。";
                        yield break;
                    }
                }
                yield return new WaitForSecondsRealtime(1);
            }
            message = "本地服务未启动，请运行 GenerateModel.cmd 查看错误。";
        }

        private IEnumerator LoadPreview()
        {
            busy = true;
            using (var request = UnityWebRequest.Get(Service + "/api/status"))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { message = "请先打开本地建模工具。"; busy = false; yield break; }
                var status = JsonUtility.FromJson<Status>(request.downloadHandler.text);
                if (status.status != "completed")
                { message = "尚无本次已完成的生成结果。"; busy = false; yield break; }
            }
            using (var request = UnityWebRequest.Get(Service + "/download/model.obj"))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                try
                {
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error);
                    var mesh = ParseObj(request.downloadHandler.text);
                    ClearPreview();
                    previewMesh = mesh;
                    previewRoot = new GameObject("AI_RawMeshPreview");
                    previewRoot.transform.position = new Vector3(1000, 1000, 1000);
                    var visual = new GameObject("GeneratedShape");
                    visual.layer = 31;
                    visual.transform.SetParent(previewRoot.transform, false);
                    visual.transform.localPosition = -mesh.bounds.center;
                    visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (!shader) shader = Shader.Find("Standard");
                    if (!shader) throw new InvalidOperationException("预览材质不可用。");
                    previewMaterial = new Material(shader) { color = new Color(.55f, .75f, .9f) };
                    visual.AddComponent<MeshRenderer>().sharedMaterial = previewMaterial;
                    var cameraObject = new GameObject("AI_PreviewCamera");
                    cameraObject.transform.SetParent(previewRoot.transform, false);
                    float radius = Mathf.Max(mesh.bounds.extents.magnitude, .01f);
                    cameraObject.transform.localPosition = new Vector3(0, radius * .2f, -radius * 3);
                    cameraObject.transform.LookAt(previewRoot.transform.position);
                    var camera = cameraObject.AddComponent<Camera>();
                    camera.cullingMask = 1 << 31;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.12f, .16f, .21f);
                    camera.nearClipPlane = radius * .01f;
                    camera.farClipPlane = radius * 10;
                    previewTexture = new RenderTexture(512, 512, 16);
                    camera.targetTexture = previewTexture;
                    var light = new GameObject("AI_PreviewLight");
                    light.transform.SetParent(previewRoot.transform, false);
                    light.transform.localRotation = Quaternion.Euler(35, -35, 0);
                    var illumination = light.AddComponent<Light>();
                    illumination.type = LightType.Directional;
                    illumination.cullingMask = 1 << 31;
                    message = $"已载入原始模型：{mesh.vertexCount} 顶点 / {mesh.triangles.Length / 3} 三角面。";
                }
                catch (Exception error) { ClearPreview(); message = "载入失败：" + error.Message; }
            }
            busy = false;
        }

        public static Mesh ParseObj(string source)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            using (var reader = new StringReader(source))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length == 0) continue;
                    if (fields[0] == "v" && fields.Length >= 4)
                    {
                        float x = float.Parse(fields[1], CultureInfo.InvariantCulture);
                        float y = float.Parse(fields[2], CultureInfo.InvariantCulture);
                        float z = float.Parse(fields[3], CultureInfo.InvariantCulture);
                        if (float.IsNaN(x + y + z) || float.IsInfinity(x + y + z)) throw new FormatException("网格坐标无效。");
                        vertices.Add(new Vector3(-x, y, z));
                        if (vertices.Count > 1000000) throw new FormatException("预览模型过大。");
                    }
                    else if (fields[0] == "f" && fields.Length >= 4)
                    {
                        var face = new int[fields.Length - 1];
                        for (int i = 1; i < fields.Length; i++)
                        {
                            int index = int.Parse(fields[i].Split('/')[0], CultureInfo.InvariantCulture);
                            index = index < 0 ? vertices.Count + index : index - 1;
                            if (index < 0 || index >= vertices.Count) throw new FormatException("网格索引无效。");
                            face[i - 1] = index;
                        }
                        for (int i = 1; i < face.Length - 1; i++)
                        { triangles.Add(face[0]); triangles.Add(face[i + 1]); triangles.Add(face[i]); }
                        if (triangles.Count > 6000000) throw new FormatException("预览模型面数过多。");
                    }
                }
            }
            if (vertices.Count < 3 || triangles.Count < 3) throw new FormatException("OBJ 中没有有效网格。");
            var mesh = new Mesh { name = "Hunyuan3D_RawShape", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ClearPreview()
        {
            if (previewRoot) Destroy(previewRoot);
            if (previewMesh) Destroy(previewMesh);
            if (previewMaterial) Destroy(previewMaterial);
            if (previewTexture) { previewTexture.Release(); Destroy(previewTexture); }
        }
        private void OnDestroy() { ClearPreview(); if (panelFont) Destroy(panelFont); }
    }
}
