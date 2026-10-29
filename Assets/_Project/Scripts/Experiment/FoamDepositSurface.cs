using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MRLab
{
    // Irreversible deposition into a shared smooth height field. No repulsive forces,
    // lateral relaxation or per-frame noise: deposited material stays where it landed.
    // A visual approximation for this demo, not a general fluid/overhang solver.
    public sealed class FoamDepositSurface
    {
        private const float Cell = .025f, ParcelVolume = .00020f;
        private struct Deposit { public float height; public bool dyed; }
        private readonly Dictionary<Vector2Int, Deposit> deposits = new Dictionary<Vector2Int, Deposit>();
        private readonly Dictionary<Vector2Int, int> indices = new Dictionary<Vector2Int, int>();
        private readonly HashSet<Vector2Int> tiles = new HashSet<Vector2Int>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> white = new List<int>(), colored = new List<int>();
        private readonly Mesh mesh;
        private readonly Transform owner;
        private readonly List<Vector2Int> footprint = new List<Vector2Int>();
        private readonly List<float> weights = new List<float>();
        private bool dirty;
        public int DepositCount { get; private set; }
        public int OutsideTrayCount { get; private set; }
        public int FloorCount { get; private set; }
        public float HighestSurface { get; private set; }
        public int VertexCount => mesh.vertexCount;

        public FoamDepositSurface(Transform parent, Material whiteMaterial, Material dyedMaterial)
        {
            owner = parent;
            var go = new GameObject("MergedFoamDeposit", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            mesh = new Mesh { name = "MergedFoamDeposit_Runtime", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { whiteMaterial, dyedMaterial };
        }

        // Scene geometry in world space. Unlike the old infinite tray plane, these
        // finite supports allow a parcel beyond a table edge to continue to the floor.
        public float Ground(float x, float z)
        {
            Vector3 world = owner.TransformPoint(new Vector3(x, 0, z));
            float y = -.01f; // Floor top.
            if (Mathf.Abs(world.x) <= 1.15f && Mathf.Abs(world.z) <= .525f) y = .89f;
            return owner.InverseTransformPoint(new Vector3(world.x, y, world.z)).y;
        }

        public float Sample(float x, float z)
        {
            float gx = x / Cell, gz = z / Cell;
            int ix = Mathf.FloorToInt(gx), iz = Mathf.FloorToInt(gz);
            float h = Mathf.Lerp(
                Mathf.Lerp(Height(new Vector2Int(ix, iz)), Height(new Vector2Int(ix+1, iz)), gx-ix),
                Mathf.Lerp(Height(new Vector2Int(ix, iz+1)), Height(new Vector2Int(ix+1, iz+1)), gx-ix), gz-iz);
            return Ground(x,z) + h;
        }

        public void Add(Vector3 point, bool dyed)
        {
            footprint.Clear(); weights.Clear();
            Vector2Int center = new Vector2Int(Mathf.RoundToInt(point.x/Cell), Mathf.RoundToInt(point.z/Cell));
            float baseY = Ground(point.x, point.z), total = 0;
            // Only incoming material spreads; older deposits are never relaxed or moved.
            float spread = Mathf.Lerp(.17f,.32f,Mathf.Clamp01((Sample(point.x,point.z)-baseY)/.35f));
            int reach = Mathf.CeilToInt(spread / Cell);
            for (int x = -reach; x <= reach; x++) for (int z = -reach; z <= reach; z++)
            {
                var key = center + new Vector2Int(x,z);
                Vector2 pos = new Vector2(key.x*Cell, key.y*Cell);
                float sqr = (pos - new Vector2(point.x,point.z)).sqrMagnitude / (spread*spread);
                if (sqr >= 1 || Mathf.Abs(Ground(pos.x,pos.y)-baseY) > .1f) continue;
                // Keep the flask interior empty instead of filling straight through glass.
                if (pos.sqrMagnitude < .10f*.10f) continue;
                float weight = Mathf.Pow(1-sqr, 2) / Mathf.Pow(1+Height(key)/.06f, 3);
                footprint.Add(key); weights.Add(weight); total += weight;
            }
            if (total <= 0) return;
            for (int i = 0; i < footprint.Count; i++)
            {
                var key = footprint[i];
                deposits.TryGetValue(key, out var deposit);
                deposit.height += ParcelVolume * weights[i] / (total * Cell * Cell);
                // Incoming colored foam coats only its landing area, not the entire mound.
                if (weights[i]/total > .002f) deposit.dyed = dyed;
                deposits[key] = deposit;
                HighestSurface = Mathf.Max(HighestSurface, Ground(key.x*Cell,key.y*Cell)+deposit.height);
            }
            DepositCount++;
            Vector3 world = owner.TransformPoint(point);
            if (Mathf.Abs(world.x) > .41f || Mathf.Abs(world.z-.10f) > .39f) OutsideTrayCount++;
            if (baseY < -.5f) FloorCount++;
            dirty = true;
        }

        private float Height(Vector2Int key) => deposits.TryGetValue(key, out var value) ? value.height : 0;

        public void RebuildIfDirty()
        {
            if (!dirty) return;
            dirty = false;
            vertices.Clear(); uvs.Clear(); white.Clear(); colored.Clear(); indices.Clear(); tiles.Clear();
            foreach (var pair in deposits)
                for (int x=-1; x<=0; x++) for (int z=-1; z<=0; z++) tiles.Add(pair.Key + new Vector2Int(x,z));
            foreach (var tile in tiles)
            {
                var b = tile + Vector2Int.right;
                var c = tile + Vector2Int.up;
                var d = b + Vector2Int.up;
                // Do not stretch a foam sheet from the tabletop down to the floor.
                float ground = Ground(tile.x*Cell,tile.y*Cell);
                if (Mathf.Abs(Ground(d.x*Cell,d.y*Cell)-ground) > .1f ||
                    Mathf.Abs(Ground(b.x*Cell,b.y*Cell)-ground) > .1f ||
                    Mathf.Abs(Ground(c.x*Cell,c.y*Cell)-ground) > .1f) continue;
                int aIndex = Vertex(tile), bIndex = Vertex(b), cIndex = Vertex(c), dIndex = Vertex(d);
                deposits.TryGetValue(tile, out var color);
                var triangles = color.dyed ? colored : white;
                triangles.Add(aIndex); triangles.Add(cIndex); triangles.Add(bIndex);
                triangles.Add(bIndex); triangles.Add(cIndex); triangles.Add(dIndex);
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0,uvs);
            mesh.subMeshCount=2; mesh.SetTriangles(white,0); mesh.SetTriangles(colored,1);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        }

        private int Vertex(Vector2Int key)
        {
            if (indices.TryGetValue(key, out int index)) return index;
            index = vertices.Count; indices.Add(key,index);
            vertices.Add(new Vector3(key.x*Cell, Ground(key.x*Cell,key.y*Cell)+Height(key)+.001f, key.y*Cell));
            uvs.Add(new Vector2(key.x*Cell*3,key.y*Cell*3));
            return index;
        }

        public void Reset()
        {
            deposits.Clear(); mesh.Clear(); dirty=false;
            DepositCount=OutsideTrayCount=FloorCount=0; HighestSurface=0;
        }

        public void Dispose() { if (mesh) Object.Destroy(mesh); }
    }
}
