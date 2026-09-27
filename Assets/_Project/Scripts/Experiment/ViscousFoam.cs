using System.Collections.Generic;
using UnityEngine;

namespace MRLab
{
    // Continuous extrusion with gravity, soft strand constraints and self-contact.
    // Smooth tube geometry, not visible spheres. This is a visual model, not CFD.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ViscousFoam : MonoBehaviour
    {
        public Material whiteMaterial;
        public Material dyedMaterial;
        public Vector3 nozzle = new Vector3(0, .32f, 0);
        private const float Spacing = .033f, Radius = .033f;
        private const int Sides = 14, Limit = 190;
        private readonly List<Vector3> points = new List<Vector3>(Limit);
        private readonly List<Vector3> previous = new List<Vector3>(Limit);
        private readonly List<bool> dyed = new List<bool>(Limit);
        private readonly List<Vector3> vertices = new List<Vector3>(6000);
        private readonly List<Vector3> normals = new List<Vector3>(6000);
        private readonly List<Vector2> uv = new List<Vector2>(6000);
        private readonly List<int> whiteTriangles = new List<int>(22000);
        private readonly List<int> dyedTriangles = new List<int>(22000);
        private Mesh mesh;
        private float targetVolume, age, settledTime;
        private bool currentDye, producing;
        public int PointCount => points.Count;
        public float HighestPoint { get; private set; }
        public float DepositedFraction { get; private set; }

        private void Awake()
        {
            mesh = new Mesh { name = "ContinuousFoam_Runtime" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterials = new[] { whiteMaterial, dyedMaterial };
        }

        public void SetProduction(float volume, bool colored, bool active)
        { targetVolume = volume; currentDye = colored; producing = active; }

        public void ResetFoam()
        {
            points.Clear(); previous.Clear(); dyed.Clear();
            targetVolume = age = settledTime = HighestPoint = DepositedFraction = 0;
            producing = currentDye = false;
            if (mesh) mesh.Clear();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            int desired = Mathf.Min(Limit, Mathf.FloorToInt(targetVolume * Limit));
            while (points.Count < desired)
            {
                points.Add(nozzle);
                var velocity = new Vector3(.27f + .035f * Mathf.Sin(age * 8), 2.5f, -.35f);
                previous.Add(nozzle - velocity * dt);
                dyed.Add(currentDye);
            }
            if (points.Count < 2) return;
            if (!producing) settledTime += dt; else settledTime = 0;
            if (settledTime > 8) return;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                Vector3 velocity = (p - previous[i]) * .975f;
                previous[i] = p;
                points[i] = p + Vector3.ClampMagnitude(velocity, .065f) + Vector3.down * (3.2f * dt * dt);
            }
            for (int iteration = 0; iteration < 6; iteration++)
            {
                for (int j = 1; j < points.Count; j++)
                {
                    int i = iteration % 2 == 0 ? j : points.Count - j;
                    Vector3 delta = points[i] - points[i - 1];
                    float length = delta.magnitude;
                    if (length < .00001f) continue;
                    Vector3 correction = delta * ((length - Spacing) / length * .5f);
                    points[i] -= correction; points[i - 1] += correction;
                }
                for (int i = 0; i < points.Count; i++)
                    for (int j = i + 4; j < points.Count; j++)
                    {
                        Vector3 delta = points[j] - points[i];
                        float sqr = delta.sqrMagnitude;
                        const float diameter = Radius * 1.8f;
                        if (sqr >= diameter * diameter || sqr < .00000001f) continue;
                        float distance = Mathf.Sqrt(sqr);
                        Vector3 correction = delta * ((diameter - distance) / distance * .5f);
                        points[j] += correction; points[i] -= correction;
                    }
                for (int i = 0; i < points.Count; i++) Collide(i);
                if (producing && points.Count < Limit) points[points.Count - 1] = nozzle;
            }
            HighestPoint = 0;
            int deposited = 0;
            for (int i = 0; i < points.Count; i++)
            {
                HighestPoint = Mathf.Max(HighestPoint, points[i].y);
                if (points[i].y < .22f) deposited++;
            }
            DepositedFraction = deposited / (float)points.Count;
        }

        private void Collide(int i)
        {
            Vector3 p = points[i];
            if (p.y < Radius - .004f)
            {
                p.y = Radius - .004f;
                previous[i] = new Vector3(Mathf.Lerp(previous[i].x,p.x,.35f),p.y,Mathf.Lerp(previous[i].z,p.z,.35f));
            }
            if (p.y < .32f && p.y > 0)
            {
                float vesselRadius = p.y < .06f ? .087f : Mathf.Lerp(.087f,.043f,Mathf.InverseLerp(.06f,.28f,p.y));
                float minRadius = vesselRadius + Radius * .9f;
                Vector2 radial = new Vector2(p.x,p.z);
                if (radial.magnitude < minRadius)
                {
                    if (radial.sqrMagnitude < .00001f) radial = new Vector2(.7f,-1f);
                    radial = radial.normalized * minRadius; p.x = radial.x; p.z = radial.y;
                }
            }
            p.x = Mathf.Clamp(p.x,-.38f+Radius,.38f-Radius);
            p.z = Mathf.Clamp(p.z,-.38f+Radius,.3f-Radius);
            points[i] = p;
        }

        private void LateUpdate()
        {
            if (points.Count < 2 || !mesh) return;
            vertices.Clear(); normals.Clear(); uv.Clear(); whiteTriangles.Clear(); dyedTriangles.Clear();
            int rings = (points.Count-1)*2+1;
            Vector3 normal = Vector3.right;
            for (int ring=0; ring<rings; ring++)
            {
                float index=ring*.5f;
                Vector3 center=Sample(index);
                Vector3 tangent=(Sample(Mathf.Min(index+.12f,points.Count-1))-Sample(Mathf.Max(0,index-.12f))).normalized;
                if (tangent.sqrMagnitude<.01f) tangent=Vector3.up;
                normal=Vector3.ProjectOnPlane(normal,tangent).normalized;
                if (normal.sqrMagnitude<.01f) normal=Vector3.Cross(tangent,Vector3.forward).normalized;
                Vector3 binormal=Vector3.Cross(tangent,normal).normalized;
                float tip=Mathf.Min(1f,(ring+.2f)/2f,(rings-ring-.8f)/2f);
                float r=Radius*Mathf.Sqrt(Mathf.Max(.015f,tip))*(1+.045f*Mathf.Sin(index*.55f));
                for (int side=0;side<=Sides;side++)
                {
                    float a=side*Mathf.PI*2/Sides;
                    Vector3 radial=normal*Mathf.Cos(a)+binormal*Mathf.Sin(a);
                    vertices.Add(center+radial*r); normals.Add(radial); uv.Add(new Vector2(side/(float)Sides,index*.18f));
                    if(ring==0 || side==Sides) continue;
                    int v=ring*(Sides+1)+side,before=v-Sides-1;
                    var tris=dyed[Mathf.Min((int)index,dyed.Count-1)]?dyedTriangles:whiteTriangles;
                    tris.Add(before); tris.Add(v+1); tris.Add(v); tris.Add(before); tris.Add(before+1); tris.Add(v+1);
                }
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv);
            mesh.subMeshCount=2; mesh.SetTriangles(whiteTriangles,0); mesh.SetTriangles(dyedTriangles,1); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        }

        private Vector3 Sample(float index)
        {
            int i=Mathf.FloorToInt(index); float t=index-i;
            Vector3 a=points[Mathf.Max(0,i-1)],b=points[i],c=points[Mathf.Min(points.Count-1,i+1)],d=points[Mathf.Min(points.Count-1,i+2)];
            return .5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
        }
        private void OnDestroy() { if(mesh) Destroy(mesh); }
    }
}
