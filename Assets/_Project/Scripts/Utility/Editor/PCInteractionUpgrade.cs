using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine.UI;

namespace MRLab.Editor
{
    public static class PCInteractionUpgrade
    {
        private const string ScenePath = "Assets/_Project/Scenes/PC/01_PC_Main.unity";
        [MenuItem("MRLab/Upgrade Models and Free Interaction")]
        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            string backup = "Assets/_Project/Scenes/Test/93_BeforeFreeInteraction.unity";
            if (!File.Exists(backup)) EditorSceneManager.SaveScene(scene, backup, true);
            const string noTrayBackup = "Assets/_Project/Scenes/Test/94_BeforeTrayRemoval.unity";
            if (!File.Exists(noTrayBackup)) EditorSceneManager.SaveScene(scene, noTrayBackup, true);
            var tray = GameObject.Find("_Experiment/Devices/PC_Demo/Tray");
            if (tray) UnityEngine.Object.DestroyImmediate(tray);
            var experiment = UnityEngine.Object.FindObjectOfType<ElephantToothpasteExperiment>();
            var vessel = experiment.pourTarget.parent;
            Vector3 vesselShift = new Vector3(0, .892f - vessel.position.y, 0);
            vessel.position += vesselShift; experiment.foam.position += vesselShift; experiment.bubbles.transform.position += vesselShift;
            var vesselLabel = vessel.Find("Label"); if (vesselLabel) vesselLabel.localPosition = new Vector3(0,.34f,0);
            var glass = vessel.Find("Glass").GetComponent<Renderer>().sharedMaterial;
            Replace(vessel, "Erlenmeyer_Empty_13cm", .28f, glass);
            vessel.Find("Glass").gameObject.SetActive(false);
            foreach (var item in experiment.reagents)
            {
                item.transform.position = new Vector3(item.transform.position.x, .892f, item.transform.position.z);
                var body = item.transform.Find("Body");
                var liquidMaterial = body.GetComponent<Renderer>().sharedMaterial;
                foreach (string name in new[] { "Body", "Shoulder", "Neck", "Band" })
                { var old = item.transform.Find(name); if (old) old.gameObject.SetActive(false); }
                Replace(item.transform, "Beaker_Empty_10cm", .16f, glass);
                item.pourSpout.localPosition = new Vector3(.05f, .155f, 0);
                var col = item.GetComponent<CapsuleCollider>(); col.center = new Vector3(0,.08f,0); col.height = .16f; col.radius = .075f;
                var contents = item.transform.Find("ReagentLiquid");
                if (!contents)
                {
                    var liquid = GameObject.CreatePrimitive(PrimitiveType.Cylinder); liquid.name = "ReagentLiquid";
                    UnityEngine.Object.DestroyImmediate(liquid.GetComponent<Collider>());
                    liquid.transform.SetParent(item.transform, false); contents = liquid.transform;
                }
                contents.localPosition = new Vector3(0,.045f,0); contents.localScale = new Vector3(.10f,.032f,.10f);
                contents.GetComponent<Renderer>().sharedMaterial = liquidMaterial;
                var label = item.transform.Find("Label"); if (label) label.localPosition = new Vector3(0,.22f,0);
                EditorUtility.SetDirty(item);
            }
            foreach (var label in UnityEngine.Object.FindObjectsOfType<TextMeshPro>())
                if (!label.GetComponent<PCFacingLabel>()) label.gameObject.AddComponent<PCFacingLabel>();
            // Remove the backdrop that would block a full orbit around the bench.
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                if (t.name == "BackWall" || t.name == "WallStripe") t.gameObject.SetActive(false);
            var camera = Camera.main;
            var orbit = camera.GetComponent<PCOrbitCamera>(); if (!orbit) orbit = camera.gameObject.AddComponent<PCOrbitCamera>();
            orbit.viewCamera = camera;
            foreach (var text in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
                if (text.name == "Controls")
                { text.text = "左键：拿烧杯 → 点锥形瓶倒入 → 点空闲台面放下\n右键旋转 · 滚轮缩放 · 中键平移 · Home 复位视角 · Esc 放回 · R 重置"; text.fontSize = 15; text.rectTransform.anchoredPosition = new Vector2(28,-119); text.rectTransform.sizeDelta = new Vector2(1000,40); }
            foreach (var panel in UnityEngine.Object.FindObjectsOfType<Image>())
                if (panel.name == "StepsPanel" || panel.name == "TopBar" || panel.name == "BottomBar") panel.raycastTarget = true;
            if (!experiment.GetComponent<PCInteractionVerification>()) experiment.gameObject.AddComponent<PCInteractionVerification>();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("FREE_INTERACTION_UPGRADE_SAVED");
        }
        private static void Replace(Transform root, string asset, float height, Material material)
        {
            var existing = root.Find("HunyuanVisual"); if (existing) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Models/Hunyuan/" + asset + ".fbx");
            if (!prefab) throw new Exception("Missing generated model: " + asset);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab); visual.name = "HunyuanVisual";
            visual.transform.SetParent(root, false);
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new Exception("Model has no renderer");
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) { bounds.Encapsulate(r.bounds); var mats = r.sharedMaterials; for (int i=0;i<mats.Length;i++) mats[i]=material; r.sharedMaterials=mats; }
            float scale = height / bounds.size.y; visual.transform.localScale *= scale;
            bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            visual.transform.position += new Vector3(root.position.x - bounds.center.x, root.position.y - bounds.min.y, root.position.z - bounds.center.z);
            Debug.Log("HUNYUAN_MODEL " + asset + " sourceHeight=" + height/scale + " fittedHeight=" + height);
        }
        public static void ApplyAndBuild() { Apply(); PCDemoBuilder.VerifyRules(); PCDemoBuilder.BuildCurrentScene(); }
    }
}
