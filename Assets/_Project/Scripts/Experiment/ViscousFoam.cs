using System.Collections.Generic;
using UnityEngine;

namespace MRLab
{
    // Continuous material chain. Contact changes mobility, not identity or volume.
    // The deposited chain keeps gravity, viscous creep and local non-elastic contact.
    [RequireComponent(typeof(MeshFilter),typeof(MeshRenderer))]
    public sealed class ViscousFoam : MonoBehaviour
    {
        public Material whiteMaterial,dyedMaterial;
        public Vector3 nozzle=new Vector3(0,.32f,0);
        public const float FullBatchVolume=.060f;
        private const float Gravity=12f;
        private const float FallingGravity=54f;
        private const float SettleStart=3f,SettleEnd=9f;
        public const float FinalExpansion=.35f;
        private readonly List<Vector3> points=new List<Vector3>(),previous=new List<Vector3>();
        private readonly List<Vector2> creepFlow=new List<Vector2>();
        private readonly List<float> fallSpeed=new List<float>();
        private readonly List<float> expandedVolumes=new List<float>();
        private readonly List<float> baseRadii=new List<float>();
        private readonly Dictionary<Vector2Int,float> heightCache=new Dictionary<Vector2Int,float>();
        private readonly List<float> volumes=new List<float>(),births=new List<float>(),speeds=new List<float>(),radii=new List<float>(),contactAges=new List<float>();
        private readonly List<Vector2> bends=new List<Vector2>();
        private readonly List<bool> dyed=new List<bool>(),deposited=new List<bool>();
        private readonly Dictionary<Vector3Int,List<int>> cells=new Dictionary<Vector3Int,List<int>>();
        private readonly Stack<List<int>> pool=new Stack<List<int>>();
        private FoamVolumeSurface surface;
        private float targetVolume,age,lastEmissionTime,nextRender,quietTime,emissionOrigin,crestPeak,crestPeakAge;
        private bool currentDye,producing,dirty;
        public int PointCount=>points.Count;
        public float EmittedVolume { get; private set; }
        public float DepositedVolume { get; private set; }
        public float ExpansionVolume { get; private set; }
        public float ExpandedVolume=>EmittedVolume+ExpansionVolume;
        public float LongestBurstDescent { get; private set; }
        public float CrestDropDuration { get; private set; }
        public float AirborneVolume=>Mathf.Max(0,EmittedVolume-DepositedVolume);
        public float MeshVolume=>surface.MeshVolume;
        public float VolumeError=>surface.RelativeVolumeError;
        public float DisconnectedFraction=>surface.DisconnectedFraction;
        public float NearThickness=>surface.NearThickness;
        public float OuterThickness=>surface.OuterThickness;
        public float HighestPoint { get; private set; }
        public float DepositedFraction=>EmittedVolume>0?Mathf.Clamp01(DepositedVolume/EmittedVolume):0;
        public int OccupiedQuadrants { get; private set; }
        public float PileHeight { get; private set; }
        public int OutsideTrayCount { get; private set; }
        public int FloorDepositCount { get; private set; }
        public float OutsideTrayVolume { get; private set; }
        public int DepositVertexCount=>surface.VertexCount;
        public Vector3 PointPosition(int i)=>points[i];
        public bool IsDeposited(int i)=>deposited[i];
        public float TimeSinceContact(int i)=>deposited[i]?age-contactAges[i]:0;
        public bool IsSupported(int i)=>deposited[i] && points[i].y-SupportBelow(i)-radii[i]*.65f<.025f;
        public float BaseJetDeviation { get; private set; }
        public float MaximumStep { get; private set; }
        public float DepositedCentreHeight { get; private set; }
        public float SupportHeight(float x,float z)=>Support(x,z,0,float.PositiveInfinity);
        private void Awake()
        {
            GetComponent<MeshRenderer>().sharedMaterials=new[]{whiteMaterial,dyedMaterial};
            surface=new FoamVolumeSurface(transform,GetComponent<MeshFilter>());
        }
        public void SetProduction(float amount,bool colored,bool active)
        { targetVolume=Mathf.Clamp01(amount)*FullBatchVolume; currentDye=colored; producing=active; }

        public void ResetFoam()
        {
            points.Clear(); previous.Clear(); volumes.Clear(); births.Clear(); speeds.Clear(); radii.Clear();
            creepFlow.Clear(); fallSpeed.Clear(); expandedVolumes.Clear(); baseRadii.Clear(); heightCache.Clear();
            bends.Clear(); dyed.Clear(); deposited.Clear(); contactAges.Clear(); surface?.Reset();
            targetVolume=age=lastEmissionTime=nextRender=quietTime=EmittedVolume=DepositedVolume=HighestPoint=PileHeight=OutsideTrayVolume=BaseJetDeviation=MaximumStep=DepositedCentreHeight=0;
            emissionOrigin=0;
            ExpansionVolume=LongestBurstDescent=0;
            crestPeak=crestPeakAge=CrestDropDuration=0;
            OccupiedQuadrants=OutsideTrayCount=FloorDepositCount=0; dirty=producing=false;
        }
        private void FixedUpdate()
        {
            float dt=Time.fixedDeltaTime; age+=dt;
            float addition=targetVolume-EmittedVolume;
            if(addition>.0000001f)
            {
                float interval=lastEmissionTime>0?Mathf.Max(dt,age-lastEmissionTime):dt;
                if(points.Count==0) emissionOrigin=age-interval;
                float velocity=Mathf.Clamp(5.4f*Mathf.Sqrt(addition/interval/(FullBatchVolume*.8f)),.65f,5.6f);
                // A brief high-pressure pulse, followed by continuous low-head flow.
                // Otherwise later parcels keep replacing the crest even when each
                // descending parcel accelerates quickly.
                float pulse=1-.72f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.25f,.65f,age-emissionOrigin));
                velocity*=pulse;
                int count=Mathf.Max(1,Mathf.CeilToInt(velocity*interval/.04f));
                float volume=addition/count,length=Mathf.Max(.012f,velocity*interval/count);
                for(int n=0;n<count;n++)
                {
                    float born=age-interval*(1-(n+.5f)/count);
                    points.Add(nozzle); previous.Add(nozzle); volumes.Add(volume); births.Add(born); speeds.Add(velocity);
                    radii.Add(Mathf.Clamp(Mathf.Sqrt(volume/(Mathf.PI*length)),.018f,.075f));
                    baseRadii.Add(radii[radii.Count-1]);
                    bends.Add(Bend(born-emissionOrigin)); dyed.Add(currentDye); deposited.Add(false); contactAges.Add(-1);
                    creepFlow.Add(Vector2.zero);
                    fallSpeed.Add(0);
                    expandedVolumes.Add(volume);
                }
                lastEmissionTime=age; EmittedVolume+=addition; dirty=true;
            }
            if(points.Count==0) return;
            quietTime=producing?0:quietTime+dt;
            ExpansionVolume=0;
            for(int i=0;i<points.Count;i++)
            {
                float expansion=deposited[i]?FinalExpansion*Mathf.SmoothStep(0,1,Mathf.Clamp01((age-contactAges[i])/3.5f)):0;
                expandedVolumes[i]=volumes[i]*(1+expansion);
                radii[i]=baseRadii[i]*Mathf.Pow(1+expansion,1f/3f);
                ExpansionVolume+=expandedVolumes[i]-volumes[i];
                previous[i]=points[i];
            }
            BuildHeightCache();
            for(int i=0;i<points.Count;i++)
            {
                if(deposited[i])
                {
                    // Overdamped gravity: no bounce, no velocity inferred from collision
                    // corrections. Upper layers slowly settle rather than freezing.
                    float maturity=age-contactAges[i];
                    float mobility=Mobility(i);
                    float yielding=Mathf.Exp(-maturity/2f);
                    float creep=(.018f+.12f*yielding)*mobility;
                    Vector3 p=points[i]; const float probe=.04f;
                    float support=SupportBelow(i);
                    float gap=p.y-support-radii[i]*.65f;
                    fallSpeed[i]=gap>.025f?Mathf.Min(.65f,fallSpeed[i]+Gravity*dt):0;
                    if(mobility==0 && fallSpeed[i]==0) { creepFlow[i]=Vector2.zero;continue; }
                    creep=Mathf.Max(creep,Mathf.Min(fallSpeed[i],Mathf.Max(0,gap)/dt));
                    Vector2 slope=new Vector2(HeightAt(p.x+probe,p.z)-HeightAt(p.x-probe,p.z),HeightAt(p.x,p.z+probe)-HeightAt(p.x,p.z-probe))/(2*probe);
                    Vector2 wanted=slope.magnitude>.6f?-slope.normalized*((.014f+.045f*yielding)*mobility*Mathf.Clamp01((slope.magnitude-.6f)/2)):Vector2.zero;
                    creepFlow[i]=Vector2.Lerp(creepFlow[i],wanted,1-Mathf.Exp(-dt/ .5f));
                    points[i]+=new Vector3(creepFlow[i].x,-creep,creepFlow[i].y)*dt;
                    continue;
                }
                float life=age-births[i],v=speeds[i],apex=v/Gravity;
                float turn=Mathf.SmoothStep(0,1,Mathf.InverseLerp(apex*.72f,apex+.19f,life));
                Vector2 lateral=bends[i]*turn;
                float descent=Mathf.Max(0,life-apex);
                float height=life<=apex?v*life-.5f*Gravity*life*life:.5f*v*v/Gravity-.5f*FallingGravity*descent*descent;
                points[i]=nozzle+new Vector3(lateral.x,height,lateral.y);
                if(life<apex*.65f) BaseJetDeviation=Mathf.Max(BaseJetDeviation,lateral.magnitude);
            }
            // Cohesion along the emitted material also spans the air/contact boundary.
            for(int pass=0;pass<5;pass++)
            for(int i=1;i<points.Count;i++)
            {
                Vector3 delta=points[i]-points[i-1];
                float length=delta.magnitude,limit=Mathf.Max(.04f,(radii[i]+radii[i-1])*.90f);
                if(length<=limit || length<.00001f) continue;
                float wa=deposited[i]?0:1,wb=deposited[i-1]?0:1;
                if(wa+wb==0) continue;
                Vector3 correction=delta*((length-limit)/length*.65f/(wa+wb));
                if(age-births[i]>.08f) points[i]-=correction*wa;
                if(age-births[i-1]>.08f) points[i-1]+=correction*wb;
            }
            for(int i=0;i<points.Count;i++)
            {
                if(deposited[i] || age-births[i]<speeds[i]/Gravity) continue;
                Vector3 p=points[i];
                ProjectFlask(ref p,radii[i],bends[i]);
                float support=Support(p.x,p.z,radii[i],previous[i].y+radii[i]*2);
                if(p.y<=support+radii[i])
                {
                    p.y=Mathf.Min(support+radii[i],previous[i].y+.015f);
                    deposited[i]=true; contactAges[i]=age; DepositedVolume+=volumes[i];
                    if(speeds[i]>4.5f) LongestBurstDescent=Mathf.Max(LongestBurstDescent,age-births[i]-speeds[i]/Gravity);
                }
                points[i]=p;
            }
            RelaxContacts(dt);
            HighestPoint=PileHeight=0; int mask=0;
            OutsideTrayCount=FloorDepositCount=0; OutsideTrayVolume=DepositedCentreHeight=0;
            for(int i=0;i<points.Count;i++)
            {
                HighestPoint=Mathf.Max(HighestPoint,points[i].y+radii[i]);
                if(!deposited[i]) continue;
                PileHeight=Mathf.Max(PileHeight,points[i].y+radii[i]);
                DepositedCentreHeight+=points[i].y*volumes[i];
                if(age-contactAges[i]>.25f && fallSpeed[i]==0) MaximumStep=Mathf.Max(MaximumStep,Vector3.Distance(previous[i],points[i]));
                mask|=1<<((points[i].x>=0?1:0)+(points[i].z>=0?2:0));
                Vector3 world=transform.TransformPoint(points[i]);
                if(Mathf.Abs(world.x)>.41f || Mathf.Abs(world.z-.1f)>.39f)
                { OutsideTrayCount++; OutsideTrayVolume+=volumes[i]; }
                if(surface.Ground(points[i].x,points[i].z)<-.5f) FloorDepositCount++;
            }
            if(DepositedVolume>0) DepositedCentreHeight/=DepositedVolume;
            if(HighestPoint>crestPeak) {crestPeak=HighestPoint;crestPeakAge=age;CrestDropDuration=0;}
            else if(CrestDropDuration==0 && crestPeak>.9f && HighestPoint<crestPeak*.7f) CrestDropDuration=age-crestPeakAge;
            OccupiedQuadrants=0; for(int i=0;i<4;i++) if((mask&(1<<i))!=0) OccupiedQuadrants++;
            dirty=true;
        }
        private void RelaxContacts(float dt)
        {
            foreach(var bucket in cells.Values) { bucket.Clear(); pool.Push(bucket); }
            cells.Clear();
            const float cell=.16f;
            for(int i=0;i<points.Count;i++)
            {
                if(!deposited[i]) continue;
                Vector3Int key=Vector3Int.FloorToInt(points[i]/cell);
                for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++) for(int z=-1;z<=1;z++)
                {
                    if(!cells.TryGetValue(key+new Vector3Int(x,y,z),out var neighbors)) continue;
                    foreach(int j in neighbors)
                    {
                        if(i-j<4) continue;
                        Vector3 delta=points[i]-points[j];
                        float distance=delta.magnitude,contact=(radii[i]+radii[j])*.88f;
                        if(distance>=contact) continue;
                        Vector3 normal=distance>.00001f?delta/distance:new Vector3(.3f,.7f,.2f).normalized;
                        float wi=Mobility(i),wj=Mobility(j);
                        float correction=Mathf.Min((contact-distance)*.18f,.014f*dt);
                        // Local creep under load, bounded every step. No energetic repulsion.
                        points[i]+=normal*correction*wi; points[j]-=normal*correction*wj;
                    }
                }
                if(!cells.TryGetValue(key,out var bucket))
                { bucket=pool.Count>0?pool.Pop():new List<int>(); cells.Add(key,bucket); }
                bucket.Add(i);
            }
            for(int i=0;i<points.Count;i++)
            {
                if(!deposited[i]) continue;
                Vector3 p=points[i];
                if(age-contactAges[i]<dt*.5f)
                {
                    // Collision has already located the actual contact surface.
                    // Limiting this transition relative to the previous AIR position
                    // would leave a landed parcel suspended high above that surface.
                    p.y=Mathf.Max(p.y,surface.Ground(p.x,p.z)+radii[i]*.55f);
                    ProjectFlask(ref p,radii[i],bends[i]);points[i]=p;continue;
                }
                // A parcel can move only slowly after contact, even in dense folds.
                float mobility=Mobility(i);
                if(mobility==0 && fallSpeed[i]==0) { points[i]=previous[i];continue; }
                float rate=Mathf.Max((.035f+.15f*Mathf.Exp(-(age-contactAges[i])/2f))*mobility,fallSpeed[i]>0?fallSpeed[i]+.02f:0);
                p=Vector3.MoveTowards(previous[i],p,rate*dt);
                float floor=surface.Ground(p.x,p.z)+radii[i]*.55f;
                p.y=Mathf.Max(p.y,floor);
                ProjectFlask(ref p,radii[i],bends[i]);
                points[i]=Vector3.MoveTowards(previous[i],p,rate*dt);
            }
        }
        private float Mobility(int i)=>1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(SettleStart,SettleEnd,age-contactAges[i]));
        private void ProjectFlask(ref Vector3 p,float radius,Vector2 fallback)
        {
            if(p.y<0 || p.y>.46f) return;
            float vessel=p.y<.06f?.087f:Mathf.Lerp(.087f,.041f,Mathf.InverseLerp(.06f,.28f,p.y));
            float minimum=(vessel+radius*.75f)*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.28f,.46f,p.y)));
            Vector2 radial=new Vector2(p.x,p.z);
            if(radial.magnitude>=minimum) return;
            radial=(radial.sqrMagnitude>.00001f?radial.normalized:fallback.normalized)*minimum;
            p.x=radial.x;p.z=radial.y;
        }
        private float Support(float x,float z,float radius,float ceiling)
        {
            float height=surface.Ground(x,z);
            for(int i=0;i<points.Count;i++)
            {
                if(!deposited[i] || age-contactAges[i]<.06f) continue;
                float dx=x-points[i].x,dz=z-points[i].z,reach=(radii[i]+radius)*.86f;
                float sqr=reach*reach-dx*dx-dz*dz;
                if(sqr<=0) continue;
                float top=points[i].y+Mathf.Sqrt(sqr)-radius;
                if(top<=ceiling) height=Mathf.Max(height,top);
            }
            return height;
        }
        private float SupportBelow(int parcel)
        {
            Vector3 p=previous[parcel];
            float height=surface.Ground(p.x,p.z),radius=radii[parcel];
            for(int j=0;j<points.Count;j++)
            {
                if(!deposited[j] || age-contactAges[j]<.06f || Mathf.Abs(j-parcel)<3 || previous[j].y>p.y-radius*.5f) continue;
                Vector3 other=previous[j];
                float dx=p.x-other.x,dz=p.z-other.z,reach=(radius+radii[j])*.78f;
                float sqr=reach*reach-dx*dx-dz*dz;
                if(sqr>0) height=Mathf.Max(height,other.y+Mathf.Sqrt(sqr)-radius*.65f);
            }
            return height;
        }
        private void BuildHeightCache()
        {
            heightCache.Clear(); const float cell=.04f;
            for(int i=0;i<points.Count;i++)
            {
                if(!deposited[i]) continue;
                Vector3 p=points[i]; int cx=Mathf.RoundToInt(p.x/cell),cz=Mathf.RoundToInt(p.z/cell);
                int reach=Mathf.CeilToInt(radii[i]/cell);
                for(int x=cx-reach;x<=cx+reach;x++) for(int z=cz-reach;z<=cz+reach;z++)
                {
                    float dx=x*cell-p.x,dz=z*cell-p.z,sqr=radii[i]*radii[i]-dx*dx-dz*dz;
                    if(sqr<0) continue;
                    var key=new Vector2Int(x,z); float top=p.y+Mathf.Sqrt(sqr);
                    if(!heightCache.TryGetValue(key,out float old) || top>old) heightCache[key]=top;
                }
            }
        }
        private float HeightAt(float x,float z)
        {
            var key=new Vector2Int(Mathf.RoundToInt(x/.04f),Mathf.RoundToInt(z/.04f));
            float result=heightCache.TryGetValue(key,out float value)?value:surface.Ground(x,z);
            if(x*x+z*z<.11f*.11f) result=Mathf.Max(result,.34f);
            return result;
        }
        private static Vector2 Bend(float born)
        {
            float t=born*1.25f; int k=Mathf.FloorToInt(t);
            Vector2 direction=Vector2.Lerp(Direction(k),Direction(k+1),Mathf.SmoothStep(0,1,t-k)).normalized;
            return direction*(.15f+.025f*Mathf.PerlinNoise(born*.8f,7.13f));
        }
        private static Vector2 Direction(int seed)
        {
            uint hash=unchecked((uint)(seed+173)*747796405u+2891336453u);
            hash=((hash>>((int)(hash>>28)+4))^hash)*277803737u; hash=(hash>>22)^hash;
            float angle=(hash&65535)/65535f*Mathf.PI*2;
            return new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
        }
        private void LateUpdate()
        {
            if(!dirty || Time.unscaledTime<nextRender) return;
            RefreshSurface(); nextRender=Time.unscaledTime+.05f;
        }
        public void RefreshSurface(bool includeExpansion=true)
        { surface.Rebuild(points,includeExpansion?expandedVolumes:volumes,dyed,deposited,includeExpansion?ExpandedVolume:EmittedVolume,producing?nozzle:(Vector3?)null);dirty=false; }
        private void OnDestroy() { surface?.Dispose(); }
    }
}
