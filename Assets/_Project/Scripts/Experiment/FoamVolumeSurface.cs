using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MRLab
{
    // A continuous, mass-weighted swept material curve, reconstructed with a FIXED
    // isovalue. Contact never switches kernels or globally inflates an existing pile.
    public sealed class FoamVolumeSurface : IDisposable
    {
        public const float Cell = .027f;
        private readonly Mesh mesh;
        private readonly Transform owner;
        private float[] field = new float[0], dye = new float[0];
        private float[] flux=new float[0],dyeFlux=new float[0];
        private bool[] solid=new bool[0];
        private Vector3 min;
        private int nx,ny,nz,size;
        private const float iso=.33f;
        private readonly List<Vector3> samples=new List<Vector3>();
        private readonly List<float> sampleMass=new List<float>(),sampleRadius=new List<float>();
        private readonly List<bool> sampleColors=new List<bool>();
        private readonly List<Vector3> vertices=new List<Vector3>(), normals=new List<Vector3>();
        private readonly List<Vector2> uv=new List<Vector2>();
        private readonly List<bool> vertexDye=new List<bool>();
        private readonly List<int> white=new List<int>(),colored=new List<int>();
        private readonly Dictionary<long,int> edges=new Dictionary<long,int>();
        private readonly int[] corners=new int[8],inside=new int[4],outside=new int[4];
        private static readonly int[,] Tetrahedra={{0,5,1,6},{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6}};
        private static readonly Vector3Int[] BoundaryDirections={Vector3Int.left,Vector3Int.right,Vector3Int.down,Vector3Int.up,new Vector3Int(0,0,-1),new Vector3Int(0,0,1)};
        private double signedVolume;
        public float MeshVolume { get; private set; }
        public float RelativeVolumeError { get; private set; }
        public int VertexCount=>mesh.vertexCount;
        public float NearThickness { get; private set; }
        public float OuterThickness { get; private set; }
        public float DisconnectedFraction { get; private set; }

        public FoamVolumeSurface(Transform parent,MeshFilter filter)
        {
            owner=parent;
            mesh=new Mesh {name="ConservedFoamVolume_Runtime",indexFormat=IndexFormat.UInt32};
            mesh.MarkDynamic(); filter.sharedMesh=mesh;
        }
        public float Ground(float x,float z)
        {
            Vector3 world=owner.TransformPoint(new Vector3(x,0,z));
            float y=-.01f;
            if(Mathf.Abs(world.x)<=1.15f && Mathf.Abs(world.z)<=.525f) y=.89f;
            return owner.InverseTransformPoint(new Vector3(world.x,y,world.z)).y;
        }
        public void Rebuild(List<Vector3> points,List<float> volumes,List<bool> colors,List<bool> settled,float totalVolume,Vector3? outlet=null)
        {
            if(points.Count==0 || totalVolume<=0) { Reset(); return; }
            samples.Clear(); sampleMass.Clear(); sampleRadius.Clear(); sampleColors.Clear();
            for(int i=0;i<points.Count;i++)
            {
                Vector3 a=i>0?(points[i-1]+points[i])*.5f:points[i];
                Vector3 b=i<points.Count-1?(points[i]+points[i+1])*.5f:outlet??points[i];
                float first=Vector3.Distance(a,points[i]),second=Vector3.Distance(points[i],b),length=first+second;
                // Area follows the local amount of material per unit length: stretching
                // makes a thinner neck; compression makes a thicker fold, locally only.
                float radius=Mathf.Clamp(2*Mathf.Sqrt(volumes[i]/(Mathf.PI*Mathf.Max(.012f,length))),.045f,.18f);
                if(length<.00001f) AddSample(points[i],volumes[i],radius,colors[i]);
                else
                {
                    AddSpan(a,points[i],volumes[i]*first/length,radius,colors[i]);
                    AddSpan(points[i],b,volumes[i]*second/length,radius,colors[i]);
                }
            }
            Vector3 low=points[0],high=low;
            float margin=.05f;
            for(int i=0;i<samples.Count;i++)
            {
                low=Vector3.Min(low,samples[i]); high=Vector3.Max(high,samples[i]);
                margin=Mathf.Max(margin,sampleRadius[i]+Cell*2);
            }
            min=new Vector3(Mathf.Floor((low.x-margin)/Cell)*Cell,Mathf.Floor((low.y-margin)/Cell)*Cell,Mathf.Floor((low.z-margin)/Cell)*Cell);
            high+=Vector3.one*margin;
            nx=Mathf.CeilToInt((high.x-min.x)/Cell)+1; ny=Mathf.CeilToInt((high.y-min.y)/Cell)+1; nz=Mathf.CeilToInt((high.z-min.z)/Cell)+1;
            size=nx*ny*nz;
            if(field.Length<size) { field=new float[size]; dye=new float[size];flux=new float[size];dyeFlux=new float[size];solid=new bool[size]; }
            else { Array.Clear(field,0,size); Array.Clear(dye,0,size);Array.Clear(solid,0,size); }
            for(int i=0;i<samples.Count;i++)
            {
                float radius=sampleRadius[i],square=radius*radius;
                float density=sampleMass[i]*1.56668147f/(radius*radius*radius);
                Vector3 grid=(samples[i]-min)/Cell;
                int reach=Mathf.CeilToInt(radius/Cell),cx=Mathf.RoundToInt(grid.x),cy=Mathf.RoundToInt(grid.y),cz=Mathf.RoundToInt(grid.z);
                for(int z=Mathf.Max(0,cz-reach);z<=Mathf.Min(nz-1,cz+reach);z++)
                for(int y=Mathf.Max(0,cy-reach);y<=Mathf.Min(ny-1,cy+reach);y++)
                for(int x=Mathf.Max(0,cx-reach);x<=Mathf.Min(nx-1,cx+reach);x++)
                {
                    float q=1-(new Vector3(x,y,z)-grid).sqrMagnitude*Cell*Cell/square;
                    if(q<=0) continue;
                    int index=x+nx*(y+ny*z); float value=density*q*q*q;
                    field[index]+=value; if(sampleColors[i]) dye[index]+=value;
                }
            }
            // Solid supports, not invisible tray walls.
            for(int z=0;z<nz;z++) for(int x=0;x<nx;x++)
            {
                float px=min.x+x*Cell,pz=min.z+z*Cell,ground=Ground(px,pz);
                for(int y=0;y<ny;y++)
                {
                    float py=min.y+y*Cell;
                    float flask=py<.06f?.087f:Mathf.Lerp(.087f,.041f,Mathf.InverseLerp(.06f,.28f,py));
                    if(py<ground || (py>=0 && py<.32f && px*px+pz*pz<flask*flask))
                    { int index=x+nx*(y+ny*z);solid[index]=true; }
                }
            }
            ResolveSolidBoundary();
            RelaxDensity();
            MeshVolume=Extract(true); RelativeVolumeError=Mathf.Abs(MeshVolume-totalVolume)/totalVolume;
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv);
            mesh.subMeshCount=2; mesh.SetTriangles(white,0); mesh.SetTriangles(colored,1);
            mesh.RecalculateTangents(); mesh.RecalculateBounds(); MeasureThickness(); MeasureConnectivity();
        }
        private void AddSpan(Vector3 a,Vector3 b,float mass,float radius,bool color)
        {
            if(mass<=0) return;
            int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/(.6f*Cell)));
            for(int i=0;i<count;i++) AddSample(Vector3.Lerp(a,b,(i+.5f)/count),mass/count,radius,color);
        }
        private void AddSample(Vector3 point,float mass,float radius,bool color)
        { samples.Add(point);sampleMass.Add(mass);sampleRadius.Add(radius);sampleColors.Add(color); }
        private void ResolveSolidBoundary()
        {
            // A kernel intersecting the flask/table is displaced locally, not erased.
            // Transfer its mass and dye to the nearest free boundary cells before
            // resolving local pressure; this preserves incoming material at impact.
            int[] targets=new int[6];int limit=Mathf.Max(nx,Mathf.Max(ny,nz));
            for(int z=0;z<nz;z++) for(int y=0;y<ny;y++) for(int x=0;x<nx;x++)
            {
                int i=x+nx*(y+ny*z);if(!solid[i] || field[i]<=0) continue;
                int nearest=limit+1,count=0;
                foreach(Vector3Int direction in BoundaryDirections)
                for(int step=1;step<=Mathf.Min(nearest,limit);step++)
                {
                    int px=x+direction.x*step,py=y+direction.y*step,pz=z+direction.z*step;
                    if(px<0 || py<0 || pz<0 || px>=nx || py>=ny || pz>=nz) break;
                    int j=px+nx*(py+ny*pz);if(solid[j]) continue;
                    if(step<nearest) {nearest=step;count=0;}
                    targets[count++]=j;break;
                }
                if(count==0) throw new InvalidOperationException("No free foam boundary cell");
                float mass=field[i]/count,color=dye[i]/count;
                for(int k=0;k<count;k++) {field[targets[k]]+=mass;dye[targets[k]]+=color;}
                field[i]=dye[i]=0;
            }
        }
        private void RelaxDensity()
        {
            // Resolve over-compressed kernels only in their immediate neighborhood.
            // Equal and opposite fluxes retain field mass; no global scale/isovalue fit.
            int[] offsets={-1,1,-nx,nx,-nx*ny,nx*ny};
            for(int pass=0;pass<32;pass++)
            {
                Array.Clear(flux,0,size);Array.Clear(dyeFlux,0,size);
                for(int z=1;z<nz-1;z++) for(int y=1;y<ny-1;y++) for(int x=1;x<nx-1;x++)
                {
                    int i=x+nx*(y+ny*z);float value=field[i];
                    if(solid[i] || value<=1.1f) continue;
                    float sum=0;
                    foreach(int offset in offsets) if(!solid[i+offset]) sum+=Mathf.Max(0,value-field[i+offset]);
                    if(sum<=0) continue;
                    float excess=(value-1.1f)*.4f,color=Mathf.Clamp01(dye[i]/value);
                    foreach(int offset in offsets)
                    {
                        int j=i+offset;if(solid[j]) continue;
                        float transfer=excess*Mathf.Max(0,value-field[j])/sum;
                        flux[i]-=transfer;flux[j]+=transfer;dyeFlux[i]-=transfer*color;dyeFlux[j]+=transfer*color;
                    }
                }
                for(int i=0;i<size;i++) {field[i]+=flux[i];dye[i]+=dyeFlux[i];}
            }
        }
        private Vector3 Position(int index)=>min+new Vector3(index%nx,index/nx%ny,index/(nx*ny))*Cell;
        private Vector3 Gradient(int index)
        {
            int x=index%nx,y=index/nx%ny,z=index/(nx*ny);
            return new Vector3(field[index+(x<nx-1?1:0)]-field[index-(x>0?1:0)],field[index+(y<ny-1?nx:0)]-field[index-(y>0?nx:0)],field[index+(z<nz-1?nx*ny:0)]-field[index-(z>0?nx*ny:0)]);
        }
        private Vector3 Crossing(int a,int b)=>Vector3.LerpUnclamped(Position(a),Position(b),(iso-field[a])/(field[b]-field[a]));
        private float Extract(bool build)
        {
            signedVolume=0;
            if(build) { vertices.Clear(); normals.Clear(); uv.Clear(); vertexDye.Clear(); white.Clear(); colored.Clear(); edges.Clear(); }
            for(int z=0;z<nz-1;z++) for(int y=0;y<ny-1;y++) for(int x=0;x<nx-1;x++)
            {
                int a=x+nx*(y+ny*z),plane=nx*ny;
                corners[0]=a; corners[1]=a+1; corners[2]=a+1+nx; corners[3]=a+nx;
                corners[4]=a+plane; corners[5]=a+plane+1; corners[6]=a+plane+1+nx; corners[7]=a+plane+nx;
                int count=0; for(int k=0;k<8;k++) if(field[corners[k]]>=iso) count++;
                if(count==0 || count==8) continue;
                for(int t=0;t<6;t++)
                {
                    int ni=0,no=0; Vector3 inward=Vector3.zero,outward=Vector3.zero;
                    for(int k=0;k<4;k++)
                    {
                        int index=corners[Tetrahedra[t,k]];
                        if(field[index]>=iso) { inside[ni++]=index; inward+=Position(index); }
                        else { outside[no++]=index; outward+=Position(index); }
                    }
                    if(ni==0 || no==0) continue;
                    Vector3 direction=outward/no-inward/ni;
                    if(ni==1) Triangle(inside[0],outside[0],inside[0],outside[1],inside[0],outside[2],direction,build);
                    else if(ni==3) Triangle(outside[0],inside[0],outside[0],inside[1],outside[0],inside[2],direction,build);
                    else
                    {
                        Triangle(inside[0],outside[0],inside[0],outside[1],inside[1],outside[0],direction,build);
                        Triangle(inside[0],outside[1],inside[1],outside[1],inside[1],outside[0],direction,build);
                    }
                }
            }
            return (float)Math.Abs(signedVolume);
        }
        private void Triangle(int a0,int a1,int b0,int b1,int c0,int c1,Vector3 outward,bool build)
        {
            Vector3 a=Crossing(a0,a1),b=Crossing(b0,b1),c=Crossing(c0,c1);
            bool reverse=Vector3.Dot(Vector3.Cross(b-a,c-a),outward)<0;
            signedVolume+=(reverse?-1:1)*Vector3.Dot(a,Vector3.Cross(b,c))/6.0;
            if(!build) return;
            int ia=Vertex(a0,a1,a),ib=Vertex(b0,b1,b),ic=Vertex(c0,c1,c);
            var triangles=(vertexDye[ia]?1:0)+(vertexDye[ib]?1:0)+(vertexDye[ic]?1:0)>=2?colored:white;
            triangles.Add(ia); triangles.Add(reverse?ic:ib); triangles.Add(reverse?ib:ic);
        }
        private int Vertex(int a,int b,Vector3 point)
        {
            long key=((long)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
            if(edges.TryGetValue(key,out int index)) return index;
            index=vertices.Count; edges.Add(key,index);
            float t=(iso-field[a])/(field[b]-field[a]);
            vertices.Add(point); normals.Add(-Vector3.LerpUnclamped(Gradient(a),Gradient(b),t).normalized);
            uv.Add(new Vector2(point.x*3+point.y,point.z*3+point.y));
            vertexDye.Add(Mathf.LerpUnclamped(dye[a],dye[b],t)>iso*.5f); return index;
        }
        private void MeasureThickness()
        {
            double near=0,outer=0; int nc=0,oc=0;
            for(int z=0;z<nz;z++) for(int x=0;x<nx;x++)
            {
                float px=min.x+x*Cell,pz=min.z+z*Cell,r=Mathf.Sqrt(px*px+pz*pz),height=0;
                if(r<.10f || r>.39f) continue;
                for(int y=0;y<ny;y++) if(field[x+nx*(y+ny*z)]>=iso) height+=Cell;
                if(r<.22f) { near+=height; nc++; } else if(r>.29f) { outer+=height; oc++; }
            }
            NearThickness=nc>0?(float)(near/nc):0; OuterThickness=oc>0?(float)(outer/oc):0;
        }
        private void MeasureConnectivity()
        {
            int[] parent=new int[vertices.Count];
            for(int i=0;i<parent.Length;i++) parent[i]=i;
            foreach(var triangles in new[]{white,colored})
                for(int i=0;i<triangles.Count;i+=3)
                {
                    int root=Root(parent,triangles[i]);
                    parent[Root(parent,triangles[i+1])]=root;
                    parent[Root(parent,triangles[i+2])]=root;
                }
            var componentVolumes=new Dictionary<int,double>();
            foreach(var triangles in new[]{white,colored})
                for(int i=0;i<triangles.Count;i+=3)
                {
                    int root=Root(parent,triangles[i]);
                    componentVolumes.TryGetValue(root,out double volume);
                    componentVolumes[root]=volume+Vector3.Dot(vertices[triangles[i]],Vector3.Cross(vertices[triangles[i+1]],vertices[triangles[i+2]]))/6.0;
                }
            double total=0,largest=0;
            foreach(double value in componentVolumes.Values) { double v=Math.Abs(value);total+=v;largest=Math.Max(largest,v); }
            DisconnectedFraction=total>0?(float)(1-largest/total):0;
        }
        private static int Root(int[] parent,int index)
        {
            while(parent[index]!=index) { parent[index]=parent[parent[index]];index=parent[index]; }
            return index;
        }
        public void Reset() { mesh.Clear(); MeshVolume=RelativeVolumeError=NearThickness=OuterThickness=DisconnectedFraction=0; }
        public void Dispose()
        {
            if(!mesh) return;
            if(Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
