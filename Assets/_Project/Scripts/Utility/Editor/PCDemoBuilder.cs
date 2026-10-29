using System;
using System.IO;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MRLab.Editor
{
    public static class PCDemoBuilder
    {
        private const string Root = "Assets/_Project/";
        private const string ScenePath = Root + "Scenes/PC/01_PC_Main.unity";
        private const string BackupPath = Root + "Scenes/Test/91_Test_BeforePCDemo.unity";
        private static TMP_FontAsset font;
        private static Material white, teal, navy, metal, glass, foamMat, amber, pink;
        private const string RevisionBackup = Root + "Scenes/Test/92_Test_BeforeFreeMix.unity";

        [MenuItem("MRLab/Build PC Demo Scene")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath);
            if (!File.Exists(BackupPath)) EditorSceneManager.SaveScene(scene, BackupPath, true);
            if (!File.Exists(RevisionBackup)) EditorSceneManager.SaveScene(scene, RevisionBackup, true);
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "Fonts/SourceHanSansSC-Bold SDF.asset");
            if (!font) throw new InvalidOperationException("Chinese font asset missing");
            white = Mat("LabBench", new Color(.78f, .85f, .86f));
            teal = Mat("Reagent_Peroxide", new Color(.08f, .56f, .61f));
            navy = Mat("LabFrame", new Color(.04f, .10f, .15f));
            metal = Mat("Tray", new Color(.25f, .36f, .4f));
            amber = Mat("Reagent_Soap", new Color(.95f, .58f, .17f));
            pink = Mat("Reagent_Color", new Color(.78f, .2f, .38f));
            foamMat = Mat("Foam", new Color(.68f, .69f, .66f));
            foamMat.SetFloat("_Smoothness", .48f);
            var dyedFoam = Mat("Foam_Dyed", new Color(.22f, .82f, .65f));
            dyedFoam.SetFloat("_Smoothness", .48f);
            var foamNormal = FoamNormal();
            foreach (var material in new[] { foamMat, dyedFoam })
            {
                material.SetTexture("_BumpMap", foamNormal); material.SetFloat("_BumpScale", .18f);
                material.SetTextureScale("_BaseMap", new Vector2(8,8)); material.EnableKeyword("_NORMALMAP");
            }
            glass = Mat("ReactionVessel_Glass", new Color(.7f, .9f, .94f, .22f), true);

            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>()) canvas.gameObject.SetActive(false);
            foreach (var es in UnityEngine.Object.FindObjectsOfType<EventSystem>()) es.gameObject.SetActive(false);
            // Only generated nodes are replaced when explicitly rebuilding.
            foreach (var old in UnityEngine.Object.FindObjectsOfType<Transform>())
                if (old && old.name == "PC_Demo") UnityEngine.Object.DestroyImmediate(old.gameObject);

            var environment = Group("PC_Demo", GameObject.Find("_Environment").transform);
            var devices = Group("PC_Demo", GameObject.Find("_Experiment/Devices").transform);
            var effects = Group("PC_Demo", GameObject.Find("_Experiment/Effects").transform);
            var interaction = Group("PC_Demo", GameObject.Find("_Interaction/PC").transform);
            var systems = Group("PC_Demo", GameObject.Find("_Systems").transform);
            var ui = Group("PC_Demo", GameObject.Find("_UI").transform);

            var bench = Group("LabBench", environment);
            Primitive("Top", PrimitiveType.Cube, bench, new Vector3(0,.84f,0), new Vector3(2.3f,.1f,1.05f), white);
            foreach (float x in new[] { -.95f, .95f })
                foreach (float z in new[] { -.38f, .38f })
                    Primitive("Leg", PrimitiveType.Cube, bench, new Vector3(x,.39f,z), new Vector3(.06f,.78f,.06f), navy);
            SavePrefab(bench, "Environment/LabBench");
            Primitive("Floor", PrimitiveType.Cube, environment, new Vector3(0,-.035f,0), new Vector3(7,.05f,7), Mat("LabFloor",new Color(.12f,.19f,.23f)));
            Primitive("BackWall", PrimitiveType.Cube, environment, new Vector3(0,1.6f,1.8f), new Vector3(7,3.2f,.1f), Mat("LabWall",new Color(.20f,.31f,.35f)));
            Primitive("WallStripe", PrimitiveType.Cube, environment, new Vector3(0,1.25f,1.735f), new Vector3(7,.08f,.03f), teal);
            var tray = Group("Tray", devices);
            tray.position = new Vector3(0,.9f,.10f);
            Primitive("Base", PrimitiveType.Cube, tray, Vector3.zero, new Vector3(.82f,.022f,.78f), metal);
            foreach (float x in new[] { -.4f, .4f }) Primitive("Rim", PrimitiveType.Cube, tray,new Vector3(x,.025f,0),new Vector3(.015f,.05f,.78f),metal);
            foreach (float z in new[] { -.38f, .38f }) Primitive("Rim", PrimitiveType.Cube, tray,new Vector3(0,.025f,z),new Vector3(.82f,.05f,.015f),metal);
            SavePrefab(tray, "Devices/Tray");

            var vessel = Group("ReactionVessel", devices);
            vessel.position = new Vector3(0,.92f,.12f);
            MeshObject("Glass", vessel, VesselMesh(), glass);
            var vesselItem = vessel.gameObject.AddComponent<LabItem>();
            vesselItem.isVessel = true;
            var vesselCollider = vessel.gameObject.AddComponent<CapsuleCollider>();
            vesselCollider.center = new Vector3(0,.15f,0); vesselCollider.radius = .092f; vesselCollider.height = .3f;
            var liquid = Primitive("Liquid",PrimitiveType.Cylinder,vessel,new Vector3(0,.04f,0),new Vector3(.14f,.04f,.14f),teal);
            Label(vessel, "反应容器", new Vector3(0,-.02f,-.23f), .032f);
            var target = Group("PourTarget", vessel); target.localPosition = new Vector3(0,.28f,0);
            SavePrefab(vessel, "Devices/ReactionVessel");

            var experiment = systems.gameObject.AddComponent<ElephantToothpasteExperiment>();
            experiment.pourTarget = target; experiment.liquid = liquid.transform; experiment.liquidRenderer = liquid.GetComponent<Renderer>();
            experiment.reagents = new LabItem[4];
            float[] positions = { -.88f, -.56f, .56f, .88f };
            Material[] bottleMats = { teal, amber, pink, white };
            string[] names = { "Peroxide", "Soap", "Color", "Catalyst" };
            for (int i = 0; i < 4; i++)
            {
                var bottle = Group("Reagent_" + names[i], devices);
                bottle.position = new Vector3(positions[i],.9f,-.05f);
                Primitive("Body",PrimitiveType.Cylinder,bottle,new Vector3(0,.085f,0),new Vector3(.11f,.085f,.11f),bottleMats[i]);
                Primitive("Shoulder",PrimitiveType.Sphere,bottle,new Vector3(0,.155f,0),new Vector3(.11f,.065f,.11f),bottleMats[i]);
                Primitive("Neck",PrimitiveType.Cylinder,bottle,new Vector3(0,.19f,0),new Vector3(.052f,.028f,.052f),bottleMats[i]);
                Primitive("Band",PrimitiveType.Cylinder,bottle,new Vector3(0,.07f,0),new Vector3(.112f,.023f,.112f),white);
                var item = bottle.gameObject.AddComponent<LabItem>(); item.reagent = (Reagent)i;
                var col = bottle.gameObject.AddComponent<CapsuleCollider>(); col.center = new Vector3(0,.11f,0); col.radius = .073f; col.height = .23f;
                var ring = Primitive("SelectionRing",PrimitiveType.Cylinder,bottle,new Vector3(0,.005f,0),new Vector3(.17f,.004f,.17f),foamMat);
                item.selectionRing = ring; ring.SetActive(false);
                item.pourSpout = Group("PourSpout",bottle); item.pourSpout.localPosition = new Vector3(0,.218f,0);
                Label(bottle, (i+1) + "  " + ExperimentSession.ReagentNames[i],new Vector3(0,.275f,0),.025f);
                experiment.reagents[i] = item;
                SavePrefab(bottle,"Interactables/Reagent_"+names[i]);
            }

            var foam = Group("Foam", effects); foam.position = vessel.position;
            var viscous = foam.gameObject.AddComponent<ViscousFoam>();
            viscous.whiteMaterial = foamMat; viscous.dyedMaterial = dyedFoam;
            foam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            experiment.foam = foam;
            SavePrefab(foam,"Effects/Foam");
            var particles = Group("ReactionBubbles",effects); particles.position = vessel.position + Vector3.up * .12f;
            var ps = particles.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.duration = 10; main.loop = true; main.startLifetime = .7f; main.startSpeed = .18f; main.startSize = .008f; main.startColor = new Color(.9f,.96f,1f); main.maxParticles = 60; main.gravityModifier = -.02f;
            var emission = ps.emission; emission.rateOverTime = 25;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .05f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); renderer.mesh = sphere.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(sphere);
            renderer.sharedMaterial = foamMat; renderer.shadowCastingMode = ShadowCastingMode.Off;
            experiment.bubbles = ps; SavePrefab(particles,"Effects/ReactionBubbles");
            var line = Group("PourStream",effects).gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = foamMat; line.positionCount = 2; line.startWidth = .008f; line.endWidth = .006f; line.numCapVertices = 4; line.enabled = false;
            experiment.stream = line;

            var camera = Camera.main;
            camera.transform.position = new Vector3(.6f,2.5f,-3.6f);
            camera.transform.LookAt(new Vector3(-.15f,1.38f,.06f));
            camera.fieldOfView = 42; camera.nearClipPlane = .03f; camera.farClipPlane = 30;
            camera.rect = new Rect(.23f,0,.77f,1); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f,.09f,.12f);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.64f,.73f,.8f);
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>()) { light.intensity = 1.05f; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(45,-35,0); }
            var controller = interaction.gameObject.AddComponent<PCInteractionController>(); controller.viewCamera = camera; controller.experiment = experiment;
            CreateUI(ui,experiment);
            var verifier = systems.gameObject.AddComponent<PCDemoVerification>(); verifier.experiment = experiment;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("PC_DEMO_SCENE_CREATED");
        }

        private static void CreateUI(Transform parent, ElephantToothpasteExperiment experiment)
        {
            var canvasObject = new GameObject("LabHUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent,false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600,900); scaler.matchWidthOrHeight = 0;
            var root = canvasObject.transform;
            var side = Panel("StepsPanel",root,new Vector2(0,0),new Vector2(.23f,1),Vector2.zero,Vector2.zero,new Color(.035f,.075f,.11f,.99f));
            Text("Brand",side,"MR LAB  /  PC DEMO",new Vector2(24,-24),new Vector2(315,28),17,new Color(.4f,.85f,.77f));
            Text("Title",side,"大象牙膏\n模拟实验",new Vector2(24,-68),new Vector2(315,100),37,Color.white);
            Text("StepHeading",side,"自由配制 · 每瓶一份",new Vector2(24,-206),new Vector2(315,32),21,Color.white);
            var steps = Text("Steps",side,"",new Vector2(24,-260),new Vector2(320,375),23,Color.white);
            Text("TeachingNote",side,"原理提示\n酵母促进分解，洗洁精稳定泡沫。色素可选，仅染色。\n\n后加洗洁精时，已逸散的气体不会补回来。\n本版本为定性教学示意。",new Vector2(24,-655),new Vector2(315,200),18,new Color(.63f,.76f,.8f));
            var top = Panel("TopBar",root,new Vector2(.23f,1),new Vector2(1,1),new Vector2(0,-115),Vector2.zero,new Color(.035f,.075f,.11f,.92f));
            var message = Text("Message",top,"",new Vector2(28,-20),new Vector2(930,75),26,Color.white);
            var bottom = Panel("BottomBar",root,new Vector2(.23f,0),new Vector2(1,0),Vector2.zero,new Vector2(0,164),new Color(.035f,.075f,.11f,.96f));
            var status = Text("Status",bottom,"",new Vector2(28,-16),new Vector2(930,66),21,Color.white);
            var selection = Text("Selection",bottom,"",new Vector2(28,-85),new Vector2(880,26),18,new Color(.46f,.9f,.78f));
            Text("Controls",bottom,"鼠标：选试剂 → 点容器    |    1—4 选择 / Enter 添加 / R 重置 / Esc 取消",new Vector2(28,-123),new Vector2(1000,26),17,new Color(.63f,.76f,.8f));
            var buttonObject = new GameObject("ResetButton",typeof(RectTransform),typeof(Image),typeof(Button)); buttonObject.transform.SetParent(bottom,false);
            var br = buttonObject.GetComponent<RectTransform>(); br.anchorMin = br.anchorMax = new Vector2(1,1); br.pivot = new Vector2(1,1); br.anchoredPosition = new Vector2(-24,-22); br.sizeDelta = new Vector2(150,54);
            buttonObject.GetComponent<Image>().color = new Color(.12f,.53f,.49f);
            var label = Text("Label",buttonObject.transform,"重置实验",new Vector2(0,0),new Vector2(150,54),22,Color.white); label.alignment = TextAlignmentOptions.Center;
            var bg = Panel("ProgressTrack",root,new Vector2(.23f,0),new Vector2(1,0),new Vector2(0,162),new Vector2(0,168),new Color(.1f,.22f,.25f));
            var fill = Panel("ProgressFill",bg,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,new Color(.28f,.84f,.68f)).GetComponent<Image>();
            // A white sprite is required for uGUI's filled-image mesh path.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            var view = canvasObject.AddComponent<ExperimentUIController>(); view.experiment = experiment; view.steps = steps; view.message = message; view.status = status; view.selection = selection; view.progressFill = fill; view.resetButton = buttonObject.GetComponent<Button>();
            var eventObject = new GameObject("DemoEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule)); eventObject.transform.SetParent(parent,false);
            eventObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private static Transform Group(string name, Transform parent)
        { var go = new GameObject(name); go.transform.SetParent(parent,false); return go.transform; }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent,false); go.transform.localPosition = position; go.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }

        private static Material Mat(string assetName, Color color, bool transparent = false)
        {
            string folder = Root + "Art/Materials/PCDemo/" + assetName; Directory.CreateDirectory(folder);
            string path = folder + "/" + assetName + "_MAT.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat,path); }
            mat.SetColor("_BaseColor",color); mat.SetFloat("_Smoothness",.45f);
            if (transparent)
            {
                mat.SetFloat("_Surface",1); mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha); mat.SetFloat("_ZWrite",0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.SetOverrideTag("RenderType","Transparent"); mat.renderQueue = 3000; mat.SetFloat("_Cull",(float)CullMode.Off); mat.SetShaderPassEnabled("ShadowCaster",false);
            }
            EditorUtility.SetDirty(mat); return mat;
        }

        private static void SavePrefab(Transform go, string name)
        { string path = Root+"Prefabs/"+name+".prefab"; Directory.CreateDirectory(Path.GetDirectoryName(path)); PrefabUtility.SaveAsPrefabAssetAndConnect(go.gameObject,path,InteractionMode.AutomatedAction); }

        private static void Label(Transform parent, string text, Vector3 position, float size)
        {
            var go = new GameObject("Label",typeof(TextMeshPro)); go.transform.SetParent(parent,false); go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(17,-13,0);
            var tmp = go.GetComponent<TextMeshPro>(); tmp.font = font; tmp.text = text; tmp.fontSize = size*10; tmp.alignment = TextAlignmentOptions.Center; tmp.color = new Color(.94f,1,1);
            tmp.rectTransform.sizeDelta = new Vector2(.34f,.1f); tmp.enableWordWrapping = false;
        }

        private static RectTransform Panel(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject(name,typeof(RectTransform),typeof(Image)); go.transform.SetParent(parent,false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=offsetMin; rect.offsetMax=offsetMax;
            var img=go.GetComponent<Image>(); img.color=color; img.raycastTarget=false; return rect;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string value, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            var go = new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); go.transform.SetParent(parent,false);
            var tmp = go.GetComponent<TextMeshProUGUI>(); tmp.font = font; tmp.text = value; tmp.fontSize=fontSize; tmp.color=color; tmp.raycastTarget=false;
            var r=tmp.rectTransform; r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1); r.anchoredPosition=position; r.sizeDelta=size; return tmp;
        }

        private static void MeshObject(string name, Transform parent, Mesh mesh, Material material)
        { var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(parent,false); go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshRenderer>().sharedMaterial=material; }

        private static Mesh VesselMesh()
        {
            string folder=Root+"Art/Models/PCDemo/ReactionVessel"; Directory.CreateDirectory(folder);
            string path=folder+"/ReactionVessel.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            // Revolved closed profile with inner wall and a real open mouth.
            Vector2[] profile={ new Vector2(0,0),new Vector2(.084f,0),new Vector2(.087f,.02f),new Vector2(.087f,.06f),new Vector2(.048f,.255f),new Vector2(.048f,.32f),new Vector2(.041f,.32f),new Vector2(.041f,.255f),new Vector2(.079f,.055f),new Vector2(.079f,.012f),new Vector2(0,.012f) };
            var vertices=new List<Vector3>(); var triangles=new List<int>(); var uv=new List<Vector2>(); const int n=48;
            for(int p=0;p<profile.Length;p++) for(int i=0;i<=n;i++) { float a=i*Mathf.PI*2/n; vertices.Add(new Vector3(Mathf.Cos(a)*profile[p].x,profile[p].y,Mathf.Sin(a)*profile[p].x)); uv.Add(new Vector2(i/(float)n,p/(float)(profile.Length-1))); }
            for(int p=0;p<profile.Length-1;p++) for(int i=0;i<n;i++) { int a=p*(n+1)+i,b=a+n+1; triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1}); }
            var mesh=new Mesh {name="ReactionVessel"}; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); if(existing) { EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); return existing; } AssetDatabase.CreateAsset(mesh,path); return mesh;
        }

        public static void VerifyAndBuild()
        {
            VerifyRules();
            CreateScene();
            BuildCurrentScene();
        }

        // Verification/build entry point that preserves hand-edited scene and materials.
        public static void VerifyAndBuildCurrentScene()
        {
            VerifyRules();
            VerifyFoamGeometry();
            BuildCurrentScene();
        }

        public static void VerifyFoamGeometry()
        {
            var go=new GameObject("FoamGeometryVerification");
            go.transform.position=new Vector3(0,.92f,.12f);
            try
            {
                var foam=go.AddComponent<ViscousFoam>();
                foam.SendMessage("Awake");
                foam.ResetFoam();
                var session=Mixture(Reagent.Soap,Reagent.Peroxide,Reagent.Catalyst);
                float peak=0,earlyCentre=0,earlyHeight=0;
                Vector3[] initialDepositPositions=null;
                bool[] initialDeposits=null;
                float initialMotion=0;
                for(int i=0;i<600;i++)
                {
                    session.Tick(Time.fixedDeltaTime);
                    foam.SetProduction(session.FoamAmount,false,session.IsProducingGas);
                    foam.SendMessage("FixedUpdate");
                    peak=Mathf.Max(peak,foam.HighestPoint);
                    if(i>=59 && i<149 && i%10==9) Debug.Log("FOAM_CREST time="+(i+1)*Time.fixedDeltaTime+" height="+foam.HighestPoint+" depositedTop="+foam.PileHeight);
                    if(i==79 || i==99 || i==129 || i==199 || i==399)
                    {
                        foam.RefreshSurface();
                        Debug.Log("FOAM_STAGE time="+(i+1)*Time.fixedDeltaTime+" emitted="+foam.EmittedVolume+" rendered="+foam.MeshVolume+" error="+foam.VolumeError+" disconnected="+foam.DisconnectedFraction);
                        Require(foam.VolumeError<.15f,"Transient rendered volume stays close to material volume");
                        if(i<199) Require(foam.DisconnectedFraction<.01f,"Main strand remains connected");
                    }
                    if(i==399) { earlyCentre=foam.DepositedCentreHeight; earlyHeight=foam.PileHeight; }
                    if(i==129)
                    {
                        initialDepositPositions=new Vector3[foam.PointCount];initialDeposits=new bool[foam.PointCount];
                        for(int j=0;j<foam.PointCount;j++) {initialDepositPositions[j]=foam.PointPosition(j);initialDeposits[j]=foam.IsDeposited(j);}
                    }
                    if(i==179)
                        for(int j=0;j<initialDepositPositions.Length;j++)
                            if(initialDeposits[j] && foam.IsDeposited(j)) initialMotion=Mathf.Max(initialMotion,Vector3.Distance(initialDepositPositions[j],foam.PointPosition(j)));
                }
                foam.RefreshSurface();
                Debug.Log("FOAM_GEOMETRY: peak="+peak+" pile="+foam.PileHeight+" volume="+foam.MeshVolume+" error="+foam.VolumeError+" near="+foam.NearThickness+" outer="+foam.OuterThickness+" outside="+foam.OutsideTrayVolume/foam.EmittedVolume+" centreDrop="+(earlyCentre-foam.DepositedCentreHeight)+" heightDrop="+(earlyHeight-foam.PileHeight)+" maxStep="+foam.MaximumStep);
                Debug.Log("FOAM_INITIAL_DEFORMATION displacement="+initialMotion);
                Require(initialMotion>.003f && foam.MaximumStep<.02f,"Initial gravity-driven deformation without jitter");
                Require(foam.PileHeight>.25f && foam.PileHeight<.8f,"Compact three-dimensional mound");
                Require(foam.VolumeError<.06f && foam.DepositedFraction>.98f,"Rendered and deposited volume");
                Require(foam.NearThickness>foam.OuterThickness*1.2f,"Thicker close to the flask");
                Require(foam.OutsideTrayVolume/foam.EmittedVolume<.15f,"Most foam stays near flask");
                Debug.Log("FOAM_RETURN burstDescent="+foam.LongestBurstDescent+" crestDropDuration="+foam.CrestDropDuration);
                Require(foam.LongestBurstDescent<.3f && foam.CrestDropDuration>0 && foam.CrestDropDuration<.65f,"Whole crest falls promptly after the initial pulse");
                for(int i=0;i<700;i++) foam.SendMessage("FixedUpdate");
                var stopped=new Vector3[foam.PointCount];
                for(int i=0;i<stopped.Length;i++) stopped[i]=foam.PointPosition(i);
                float expanded=foam.ExpandedVolume;
                for(int i=0;i<150;i++) foam.SendMessage("FixedUpdate");
                float drift=0;
                for(int i=0;i<stopped.Length;i++) drift=Mathf.Max(drift,Vector3.Distance(stopped[i],foam.PointPosition(i)));
                Debug.Log("FOAM_SETTLED drift="+drift+" expansionRatio="+foam.ExpansionVolume/foam.EmittedVolume+" burstDescent="+foam.LongestBurstDescent+" crestDropDuration="+foam.CrestDropDuration);
                Require(drift<.000001f && Mathf.Abs(expanded-foam.ExpandedVolume)<.000001f,"Deposits stop moving and expanding");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [MenuItem("MRLab/Build Current PC Scene")]
        public static void BuildCurrentScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.defaultScreenWidth=1600; PlayerSettings.defaultScreenHeight=900; PlayerSettings.fullScreenMode=FullScreenMode.Windowed; PlayerSettings.resizableWindow=true;
            Directory.CreateDirectory("Builds/PC");
            // Meta 201's post-build callback uses non-overwriting CopyTo. Preserve its
            // previous generated binding file outside the output before rebuilding.
            const string bindings = "Builds/PC/RuntimeActionBindings.json";
            if (File.Exists(bindings))
            {
                Directory.CreateDirectory("Logs");
                File.Move(bindings, "Logs/PCBindings_" + DateTime.UtcNow.Ticks + ".json");
            }
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath},locationPathName="Builds/PC/MRLab_E06_24371102.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None });
            if(report.summary.result!=BuildResult.Succeeded || report.summary.totalErrors!=0) throw new Exception("PC demo build failed: "+report.summary.result+", errors="+report.summary.totalErrors);
            Debug.Log("PC_DEMO_BUILD_PASSED: "+report.summary.totalErrors+" errors, "+report.summary.totalWarnings+" warnings");
            foreach(var step in report.steps) foreach(var entry in step.messages)
                if(entry.type==LogType.Warning) Debug.Log("BUILD_WARNING_DETAIL: "+entry.content);
        }

        [MenuItem("MRLab/Verify Experiment Rules")]
        public static void VerifyRules()
        {
            var induction = Mixture(Reagent.Soap, Reagent.Peroxide, Reagent.Catalyst);
            induction.Tick(.99f);
            Require(induction.GasAmount == 0 && induction.FoamAmount == 0, "One second induction");
            induction.Tick(.02f);
            Require(induction.GasAmount > 0 && induction.IsProducingGas, "Burst starts after induction");
            induction.Tick(3);
            Require(induction.FoamAmount > .9f, "Most foam produced within three seconds of burst");
            // All 24 four-reagent orders, including reaction progress between pours.
            int permutations = 0;
            foreach (Reagent a in Enum.GetValues(typeof(Reagent)))
            foreach (Reagent b in Enum.GetValues(typeof(Reagent)))
            foreach (Reagent c in Enum.GetValues(typeof(Reagent)))
            foreach (Reagent d in Enum.GetValues(typeof(Reagent)))
            {
                if(a==b || a==c || a==d || b==c || b==d || c==d) continue;
                var s = new ExperimentSession();
                foreach(var reagent in new[]{a,b,c,d})
                { Require(s.TryAdd(reagent),"Any order"); s.CommitAddition(); s.Tick(.7f); s.FinishAddition(); }
                s.Tick(20);
                Require(s.Step==4 && s.Stage==ExperimentStage.Completed,"All orders complete");
                Require(Math.Abs(s.GasAmount-1)<.0001f,"Conserve finite peroxide");
                permutations++;
            }
            Require(permutations==24,"24 permutations");
            var plain=Mixture(Reagent.Soap,Reagent.Peroxide,Reagent.Catalyst);
            var colored=Mixture(Reagent.Color,Reagent.Catalyst,Reagent.Soap,Reagent.Peroxide);
            plain.Tick(20); colored.Tick(20);
            Require(plain.Stage==ExperimentStage.Completed && Math.Abs(plain.FoamAmount-colored.FoamAmount)<.0001f,"Dye optional, identical yield");
            Require(plain.DyedFoamAmount==0 && colored.DyedFoamAmount>.99f,"Only color changes");
            var noSoap=Mixture(Reagent.Catalyst,Reagent.Peroxide); noSoap.Tick(20);
            Require(noSoap.FoamAmount==0 && noSoap.GasAmount>.99f,"Gas without stable foam");
            Require(noSoap.TryAdd(Reagent.Soap),"Late soap accepted"); noSoap.FinishAddition(); noSoap.Tick(20);
            Require(noSoap.FoamAmount==0,"Spent peroxide cannot restart");
            var late=Mixture(Reagent.Peroxide,Reagent.Catalyst); late.Tick(4);
            Require(late.TryAdd(Reagent.Soap),"Soap during reaction"); late.FinishAddition(); late.Tick(20);
            Require(late.FoamAmount>0 && late.FoamAmount<.3f,"Late soap only traps remaining gas");
            var dyeLate=Mixture(Reagent.Peroxide,Reagent.Soap,Reagent.Catalyst); dyeLate.Tick(2);
            dyeLate.TryAdd(Reagent.Color); dyeLate.FinishAddition(); dyeLate.Tick(20);
            Require(dyeLate.DyedFoamAmount>0 && dyeLate.DyedFoamAmount<dyeLate.FoamAmount,"Old foam does not instantly recolor");
            var reset=new ExperimentSession(); reset.TryAdd(Reagent.Peroxide);
            Require(!reset.TryAdd(Reagent.Soap),"Prevent overlapping pours");
            reset.Reset(); reset.FinishAddition();
            Require(reset.Step==0 && !reset.IsAdding,"Cancel pending pour");
            reset=Mixture(Reagent.Soap,Reagent.Peroxide,Reagent.Catalyst); reset.Tick(1); reset.Reset();
            Require(reset.FoamAmount==0 && reset.GasAmount==0 && reset.RemainingPeroxide==0,"Reset reaction");
            var incomplete=Mixture(Reagent.Soap,Reagent.Color,Reagent.Catalyst); incomplete.Tick(20);
            Require(incomplete.Stage==ExperimentStage.Ready && incomplete.GasAmount==0,"No peroxide, no gas");
            var dt=Mixture(Reagent.Peroxide,Reagent.Catalyst); dt.Tick(float.NaN); dt.Tick(-1);
            Require(dt.GasAmount==0,"Invalid delta ignored");
            Debug.Log("PC_DEMO_RULE_TESTS_PASSED: 24 permutations; optional dye; no soap; delayed soap; exhausted reagent; reset.");
        }

        private static ExperimentSession Mixture(params Reagent[] reagents)
        {
            var s = new ExperimentSession();
            foreach(var r in reagents) { Require(s.TryAdd(r),"Mixture setup"); s.FinishAddition(); }
            return s;
        }

        private static Texture2D FoamNormal()
        {
            const string path = Root + "Art/Textures/PCDemo/Foam/Foam_Normal.png";
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                const int size = 128;
                float[,] height = new float[size,size];
                for(int y=0;y<size;y++) for(int x=0;x<size;x++)
                {
                    float u=x/(float)size*Mathf.PI*32, v=y/(float)size*Mathf.PI*32;
                    height[x,y]=Mathf.Sin(u+.6f*Mathf.Sin(v*.5f))*Mathf.Sin(v+.5f*Mathf.Sin(u*.5f));
                }
                var texture = new Texture2D(size,size,TextureFormat.RGB24,false,true);
                for(int y=0;y<size;y++) for(int x=0;x<size;x++)
                {
                    float dx=height[(x+1)%size,y]-height[(x+size-1)%size,y];
                    float dy=height[x,(y+1)%size]-height[x,(y+size-1)%size];
                    Vector3 n=new Vector3(-dx*.2f,-dy*.2f,1).normalized;
                    texture.SetPixel(x,y,new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f));
                }
                texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.NormalMap; importer.sRGBTexture=false; importer.wrapMode=TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void Require(bool condition,string message) { if(!condition) throw new Exception("Rule check failed: "+message); }
    }
}
