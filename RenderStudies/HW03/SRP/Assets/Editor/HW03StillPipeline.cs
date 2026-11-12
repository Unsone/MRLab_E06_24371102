using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class HW03StillPipeline : RenderPipelineAsset {
    protected override RenderPipeline CreatePipeline() => new StillRenderer();
    private class StillRenderer : RenderPipeline {
        protected override void Render(ScriptableRenderContext context, Camera[] cameras) {
            foreach(var camera in cameras) {
                if(camera != Target) continue;
                var cmd = new CommandBuffer {name="HW03 still bloom"};
                int a=Shader.PropertyToID("_HW03a"), b=Shader.PropertyToID("_HW03b");
                int w=Input.width/4,h=Input.height/4;
                cmd.GetTemporaryRT(a,w,h,0,FilterMode.Bilinear,RenderTextureFormat.ARGBHalf);
                cmd.GetTemporaryRT(b,w,h,0,FilterMode.Bilinear,RenderTextureFormat.ARGBHalf);
                cmd.SetGlobalTexture("_SourceStill",Input);cmd.Blit(Input,a,Effect,0);
                cmd.SetGlobalVector("_Step",new Vector4(1f/w,0,0,0));cmd.Blit(a,b,Effect,1);
                cmd.SetGlobalVector("_Step",new Vector4(0,1f/h,0,0));cmd.Blit(b,a,Effect,1);
                cmd.SetGlobalTexture("_GlowTex",a);cmd.Blit(Input,Destination,Effect,2);
                cmd.ReleaseTemporaryRT(a);cmd.ReleaseTemporaryRT(b);
                context.ExecuteCommandBuffer(cmd);context.Submit();cmd.Release();Frames++;
            }
        }
    }
    static RenderTexture Destination; static Camera Target; static Texture2D Input; static Material Effect; static int Frames;
    [MenuItem("MRLab/HW03/Export submission images")]
    public static void Export() {
        var old=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
        var pipeline=CreateInstance<HW03StillPipeline>();var go=new GameObject("Offline export");
        var folder=Path.GetFullPath(System.Environment.GetEnvironmentVariable("MRLAB_HW03_IMAGE_DIR") ?? Path.Combine(Application.dataPath,"../../images"));
        try {
            Target=go.AddComponent<Camera>();Target.enabled=false;
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            Effect=new Material(Shader.Find("Hidden/MRLab/HW03Bloom"));Frames=0;
            for(int i=1;i<=2;i++){
                Input=new Texture2D(2,2,TextureFormat.RGBA32,false,false);
                Input.LoadImage(File.ReadAllBytes(Path.Combine(folder,$"view{i:00}_hybrid.png")));
                var rt=RenderTexture.GetTemporary(Input.width,Input.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                Destination=rt;Target.Render();
                var prev=RenderTexture.active;RenderTexture.active=rt;
                var output=new Texture2D(Input.width,Input.height,TextureFormat.RGB24,false);
                output.ReadPixels(new Rect(0,0,Input.width,Input.height),0,0);output.Apply();
                File.WriteAllBytes(Path.Combine(folder,$"result{i:00}.png"),output.EncodeToPNG());
                RenderTexture.active=prev;Target.targetTexture=null;RenderTexture.ReleaseTemporary(rt);
                DestroyImmediate(output);DestroyImmediate(Input);
            }
            if(Frames!=2)throw new System.Exception("SRP did not render both cameras");
            Debug.Log("HW03_SRP_RENDERED_TWO");
        } finally {QualitySettings.renderPipeline=quality;GraphicsSettings.defaultRenderPipeline=old;DestroyImmediate(go);DestroyImmediate(Effect);DestroyImmediate(pipeline);}
    }
}



