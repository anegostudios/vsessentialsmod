using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    [ProtoContract]
    public enum EnumClothType
    {
        Rope,
        Cloth
    }

    [ProtoContract]
    public class PointList 
    {
        [ProtoMember(1)]
        public List<ClothPoint> Points = new List<ClothPoint>();
    }


    // How to synchronize this over the network?
    // Idea: We run the simulation client and server, but server 
    [ProtoContract]
    public class ClothSystem
    {
        [ProtoMember(1)]
        public int ClothId;
        [ProtoMember(2)]
        EnumClothType clothType;
        [ProtoMember(3)]
        List<PointList> Points2d = new List<PointList>();
        [ProtoMember(4)]
        List<ClothConstraint> Constraints = new List<ClothConstraint>();
        [ProtoMember(5)]
        public bool Active { get; set; }

        /// <summary>
        /// 10 joints per meter
        /// </summary>
        public static float Resolution = 2;

        public float StretchWarn = 0.6f;
        public float StretchRip = 0.75f;

        public bool LineDebug=false;
        public bool boyant = false;
        protected ICoreClientAPI capi;
        public ICoreAPI api;        
        public Vec3d windSpeed = new Vec3d();
        public ParticlePhysics pp;
        protected NormalizedSimplexNoise noiseGen;
        protected float[] tmpMat = new float[16];
        protected Vec3f distToCam = new Vec3f();
        protected AssetLocation ropeSectionModel;
        protected MeshData debugUpdateMesh;
        protected MeshRef debugMeshRef;

        public float secondsOverStretched;


        public bool PinnedAnywhere
        {
            get
            {
                foreach (var pointlist in Points2d)
                {
                    foreach (var point in pointlist.Points)
                    {
                        if (point.Pinned) return true;
                    }
                }

                return false;
            }
        }

        public double MaxExtension => Constraints.Count == 0 ? 0 : Constraints.Max(c => c.Extension);

        public Vec3d CenterPosition
        {
            get
            {
                // Loop twice to not loose decimal precision

                Vec3d pos = new Vec3d();
                int cnt = 0;
                foreach (var pointlist in Points2d)
                {
                    foreach (var point in pointlist.Points)
                    {
                        cnt++;
                    }
                }

                foreach (var pointlist in Points2d)
                {
                    foreach (var point in pointlist.Points)
                    {
                        pos.Add(point.Pos.X / cnt, point.Pos.Y / cnt, point.Pos.Z / cnt);
                    }
                }

                return pos;
            }
        }

        public ClothPoint FirstPoint => Points2d[0].Points[0];
        public ClothPoint LastPoint
        {
            get
            {
                var points = Points2d[Points2d.Count - 1].Points;
                return points[points.Count - 1];
            }
        }

        public ClothPoint[] Ends => new ClothPoint[] { FirstPoint, LastPoint };

        public static ClothSystem CreateCloth(ICoreAPI api, ClothManager cm, Vec3d start, Vec3d end)
        {
            return new ClothSystem(api, cm, start, end, EnumClothType.Cloth);
        }

        public static ClothSystem CreateRope(ICoreAPI api, ClothManager cm, Vec3d start, Vec3d end, AssetLocation clothSectionModel)
        {
            return new ClothSystem(api, cm, start, end, EnumClothType.Rope, clothSectionModel);
        }

        private ClothSystem() { }

        double minLen = 1.5;
        double maxLen = 10;

        public bool ChangeRopeLength(double len)
        {
            var plist = Points2d[0];

            double currentLength = plist.Points.Count / Resolution;

            bool isAdd = len > 0;

            if (isAdd && len + currentLength > maxLen) return false;
            if (!isAdd && len + currentLength < minLen) return false;

            int pointIndex = plist.Points.Max(p => p.PointIndex)+1;
            var fp = FirstPoint;
            var pine = fp.PinnedToEntity;
            var pinb = fp.PinnedToBlockPos;
            var pino = fp.pinnedToOffset;
            fp.UnPin();
            float step = 1 / Resolution;
            int totalPoints = Math.Abs((int)(len * Resolution));

            if (isAdd)
            {
                for (int i = 0; i <= totalPoints; i++)
                {
                    plist.Points.Insert(0, new ClothPoint(this, pointIndex++, fp.Pos.X + step*(i+1), fp.Pos.Y, fp.Pos.Z));

                    ClothPoint p1 = plist.Points[0];
                    ClothPoint p2 = plist.Points[1];
                    var constraint = new ClothConstraint(p1, p2);
                    Constraints.Add(constraint);
                }
            } else
            {
                for (int i = 0; i <= totalPoints; i++)
                {
                    var point = plist.Points[0];
                    plist.Points.RemoveAt(0);
                        
                    for (int k = 0; k < Constraints.Count; k++)
                    {
                        var c = Constraints[k];
                        if (c.Point1 == point || c.Point2 == point)
                        {
                            Constraints.RemoveAt(k);
                            k--;
                        }
                    }
                }
            }

            if (pine != null) FirstPoint.PinTo(pine, pino);
            if (pinb != null) FirstPoint.PinTo(pinb, pino);
            
            genDebugMesh();

            return true;
        }

        private ClothSystem(ICoreAPI api, ClothManager cm, Vec3d start, Vec3d end, EnumClothType clothType, AssetLocation ropeSectionModel = null)
        {
            this.clothType = clothType;
            this.ropeSectionModel = ropeSectionModel;

            Init(api, cm);            
            float step = 1 / Resolution;

            var dir = end - start;

            if (clothType == EnumClothType.Rope) {
                double len = dir.Length();

                var plist = new PointList();
                Points2d.Add(plist);

                int totalPoints = (int)(len * Resolution);

                for (int i = 0; i <= totalPoints; i++)
                {
                    var t = (float)i / totalPoints;

                    plist.Points.Add(new ClothPoint(this, i, start.X + dir.X * t, start.Y + dir.Y * t, start.Z + dir.Z * t));

                    if (i > 0)
                    {
                        ClothPoint p1 = plist.Points[i - 1];
                        ClothPoint p2 = plist.Points[i];

                        var constraint = new ClothConstraint(p1, p2);
                        Constraints.Add(constraint);
                    }
                }
            }

            if (clothType == EnumClothType.Cloth)
            {
                double hlen = (end - start).HorLength();
                double vlen = Math.Abs(end.Y - start.Y);

                int hleni = (int)(hlen * Resolution);
                int vleni = (int)(vlen * Resolution);

                int index = 0;

                for (int a = 0; a < hleni; a++)
                {
                    Points2d.Add(new PointList());

                    for (int y = 0; y < vleni; y++)
                    {
                        var th = a / hlen;
                        var tv = y / vlen;

                        Points2d[a].Points.Add(new ClothPoint(this, index++, start.X + dir.X * th, start.Y + dir.Y * tv, start.Z + dir.Z * th));

                        // add a vertical constraint
                        if (a > 0)
                        {
                            ClothPoint p1 = Points2d[a - 1].Points[y];
                            ClothPoint p2 = Points2d[a].Points[y];

                            var constraint = new ClothConstraint(p1, p2);
                            Constraints.Add(constraint);
                        }

                        // add a new horizontal constraints
                        if (y > 0)
                        {
                            ClothPoint p1 = Points2d[a].Points[y - 1];
                            ClothPoint p2 = Points2d[a].Points[y];

                            var constraint = new ClothConstraint(p1, p2);
                            Constraints.Add(constraint);
                        }
                    }
                }
            }
        }

        public void genDebugMesh()
        {
            if (capi == null) return;

            debugMeshRef?.Dispose();
            debugUpdateMesh = new MeshData(20, 15, false, false, true, true);

            int vertexIndex = 0;

            for (int i = 0; i < Constraints.Count; i++)
            {
                var c = Constraints[i];
                int color = (i % 2) > 0 ? ColorUtil.WhiteArgb : ColorUtil.BlackArgb;

                debugUpdateMesh.AddVertexSkipTex(0, 0, 0, color);
                debugUpdateMesh.AddVertexSkipTex(0, 0, 0, color);

                debugUpdateMesh.AddIndex(vertexIndex++);
                debugUpdateMesh.AddIndex(vertexIndex++);
            }


            debugUpdateMesh.mode = EnumDrawMode.Lines;
            debugMeshRef = capi.Render.UploadMesh(debugUpdateMesh);

            debugUpdateMesh.Indices = null;
            debugUpdateMesh.Rgba = null;
        }

        

        public void Init(ICoreAPI api, ClothManager cm)
        {
            this.api = api;
            this.capi = api as ICoreClientAPI;
            pp = cm.partPhysics;

            noiseGen = NormalizedSimplexNoise.FromDefaultOctaves(4, 100, 0.9, api.World.Seed + CenterPosition.GetHashCode());
        }


        public void WalkPoints(Action<ClothPoint> onPoint)
        {
            foreach (var pl in Points2d)
            {
                foreach (var point in pl.Points)
                {
                    onPoint(point);
                }
            }
        }


        public int UpdateMesh(MeshData updateMesh, float dt)
        {
            var cfloats = updateMesh.CustomFloats;
            Vec3d campos = capi.World.Player.Entity.CameraPos;
            int basep = cfloats.Count;


            Vec4f lightRgba = new Vec4f();

            if (Constraints.Count > 0)
            {
                lightRgba = api.World.BlockAccessor.GetLightRGBs(Constraints[Constraints.Count / 2].Point1.Pos.AsBlockPos);
            }

            for (int i = 0; i < Constraints.Count; i++)
            {
                ClothConstraint cc = Constraints[i];
                Vec3d p1 = cc.Point1.Pos;
                Vec3d p2 = cc.Point2.Pos;

                double dX = p1.X - p2.X;
                double dY = p1.Y - p2.Y;
                double dZ = p1.Z - p2.Z;


                float yaw = (float)Math.Atan2(dX, dZ) + GameMath.PIHALF;
                float pitch = (float)Math.Atan2(Math.Sqrt(dZ * dZ + dX * dX), dY) + GameMath.PIHALF;

                double nowx = p1.X + (p1.X - p2.X) / 2;
                double nowy = p1.Y + (p1.Y - p2.Y) / 2;
                double nowz = p1.Z + (p1.Z - p2.Z) / 2;

                distToCam.Set(
                    (float)(nowx - campos.X),
                    (float)(nowy - campos.Y),
                    (float)(nowz - campos.Z)
                );

                Mat4f.Identity(tmpMat);

                Mat4f.Translate(tmpMat, tmpMat, 0, 1 / 32f, 0);

                Mat4f.Translate(tmpMat, tmpMat, distToCam.X, distToCam.Y, distToCam.Z);
                Mat4f.RotateY(tmpMat, tmpMat, yaw);
                Mat4f.RotateZ(tmpMat, tmpMat, pitch);

                float roll = i / 5f;
                Mat4f.RotateX(tmpMat, tmpMat, roll);

                float length = GameMath.Sqrt(dX*dX+dY*dY+dZ*dZ);

                Mat4f.Scale(tmpMat, tmpMat, new float[] { length, 1, 1 }); // + (float)Math.Sin(api.World.ElapsedMilliseconds / 1000f) * 0.1f
                Mat4f.Translate(tmpMat, tmpMat, -1.5f, -1 / 32f, -0.5f); // not sure why the -1.5 here instead of -0.5



                int j = basep + i * 20;
                cfloats.Values[j++] = lightRgba.R;
                cfloats.Values[j++] = lightRgba.G;
                cfloats.Values[j++] = lightRgba.B;
                cfloats.Values[j++] = lightRgba.A;

                for (int k = 0; k < 16; k++)
                {
                    cfloats.Values[j + k] = tmpMat[k];
                }
            }

            return Constraints.Count;
        }


        /// <summary>
        /// Instantly update all cloth contraints center render pos. This is used to reduce jerkiness on new rope in its initial state
        /// </summary>
        public void setRenderCenterPos()
        {
            for (int i = 0; i < Constraints.Count; i++)
            {
                ClothConstraint cc = Constraints[i];

                Vec3d start = cc.Point1.Pos;
                Vec3d end = cc.Point2.Pos;

                double nowx = start.X + (start.X - end.X) / 2;
                double nowy = start.Y + (start.Y - end.Y) / 2;
                double nowz = start.Z + (start.Z - end.Z) / 2;

                cc.renderCenterPos.X = nowx;
                cc.renderCenterPos.Y = nowy;
                cc.renderCenterPos.Z = nowz;
            }
        }


        Matrixf mat = new Matrixf();

        public void CustomRender(float dt)
        {
            if (LineDebug && capi != null)
            {
                if (debugMeshRef == null) genDebugMesh();

                BlockPos originPos = CenterPosition.AsBlockPos;

                for (int i = 0; i < Constraints.Count; i++)
                {
                    ClothConstraint cc = Constraints[i];
                    Vec3d p1 = cc.Point1.Pos;
                    Vec3d p2 = cc.Point2.Pos;

                    debugUpdateMesh.xyz[i * 6 + 0] = (float)(p1.X - originPos.X);
                    debugUpdateMesh.xyz[i * 6 + 1] = (float)(p1.Y - originPos.Y) + 0.005f;
                    debugUpdateMesh.xyz[i * 6 + 2] = (float)(p1.Z - originPos.Z);

                    debugUpdateMesh.xyz[i * 6 + 3] = (float)(p2.X - originPos.X);
                    debugUpdateMesh.xyz[i * 6 + 4] = (float)(p2.Y - originPos.Y) + 0.005f;
                    debugUpdateMesh.xyz[i * 6 + 5] = (float)(p2.Z - originPos.Z);
                }


                capi.Render.UpdateMesh(debugMeshRef, debugUpdateMesh);

                IShaderProgram prog = capi.Shader.GetProgram((int)EnumShaderProgram.Autocamera);
                prog.Use();

                capi.Render.LineWidth = 6;
                capi.Render.BindTexture2d(0);

                capi.Render.GLDisableDepthTest();

                Vec3d cameraPos = capi.World.Player.Entity.CameraPos;

                mat.Set(capi.Render.CameraMatrixOrigin);
                mat.Translate(
                    (float)(originPos.X - cameraPos.X),
                    (float)(originPos.Y - cameraPos.Y),
                    (float)(originPos.Z - cameraPos.Z)
                );

                prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
                prog.UniformMatrix("modelViewMatrix", mat.Values);

                capi.Render.RenderMesh(debugMeshRef);

                prog.Stop();


                capi.Render.GLEnableDepthTest();
            }

        }

        float accum = 0f;
        public void updateFixedStep(float dt)
        {
            accum += dt;
            if (accum > 1) accum = 0.25f;

            float step = pp.PhysicsTickTime;

            while (accum >= step)
            {
                accum -= step;
                tickNow(step);
            }
        }

        void tickNow(float pdt) {
            // make sure all the constraints are satisfied.
            for (int i = Constraints.Count - 1; i >= 0; i--)
            {
                Constraints[i].satisfy(pdt);
            }

            // move each point with a pull from gravity
            for (int i = Points2d.Count-1; i >= 0; i--)
            {
                for (int j = Points2d[i].Points.Count-1; j >= 0; j--)
                {
                    Points2d[i].Points[j].update(pdt, api.World);
                }
            }
        }


        public void slowTick3s()
        {
            if (double.IsNaN(CenterPosition.X)) return;

            windSpeed = api.World.BlockAccessor.GetWindSpeedAt(CenterPosition) * (0.2 + noiseGen.Noise(0, (api.World.Calendar.TotalHours * 50) % 2000) * 0.8);
        }

        public void restoreReferences()
        {
            if (!Active) return;

            Dictionary<int, ClothPoint> pointsByIndex = new Dictionary<int, ClothPoint>();
            WalkPoints((p) => {
                pointsByIndex[p.PointIndex] = p;
                p.restoreReferences(this, api.World);
            });

            foreach (var c in Constraints)
            {
                c.RestorePoints(pointsByIndex);
            }            
        }

        public void updateActiveState(EnumActiveStateChange stateChange)
        {
            if (Active && stateChange == EnumActiveStateChange.RegionNowLoaded) return;
            if (!Active && stateChange == EnumActiveStateChange.RegionNowUnloaded) return;

            bool wasActive = Active;

            Active = true;
            WalkPoints((p) => {
                Active &= api.World.BlockAccessor.GetChunkAtBlockPos((int)p.Pos.X, (int)p.Pos.Y, (int)p.Pos.Z) != null;
            });

            if (!wasActive && Active) restoreReferences();
        }




        public void CollectDirtyPoints(List<ClothPointPacket> packets)
        {
            for (int i = 0; i < Points2d.Count; i++)
            {
                for (int j = 0; j < Points2d[i].Points.Count; j++)
                {
                    var point = Points2d[i].Points[j];
                    if (point.Dirty)
                    {
                        packets.Add(new ClothPointPacket() { ClothId = ClothId, PointX = i, PointY = j, Point = point });
                        point.Dirty = false;
                    }
                }
            }
        }

        public void updatePoint(ClothPointPacket msg)
        {
            if (msg.PointX >= Points2d.Count)
            {
                api.Logger.Error($"ClothSystem: {ClothId} got invalid Points2d update index for {msg.PointX}/{Points2d.Count}");
                return;
            }
            if (msg.PointY >= Points2d[msg.PointX].Points.Count)
            {
                api.Logger.Error($"ClothSystem: {ClothId} got invalid Points2d[{msg.PointX}] update index for {msg.PointY}/{Points2d[msg.PointX].Points.Count}");
                return;
            }
            ClothPoint point = Points2d[msg.PointX].Points[msg.PointY];
            point.updateFromPoint(msg.Point, api.World);
        }

        public void OnPinnnedEntityLoaded(Entity entity)
        {
            if (FirstPoint.pinnedToEntityId == entity.EntityId) FirstPoint.restoreReferences(entity);
            if (LastPoint.pinnedToEntityId == entity.EntityId) LastPoint.restoreReferences(entity);

        }
    }

    public enum EnumActiveStateChange
    {
        Default,
        RegionNowLoaded,
        RegionNowUnloaded
    }
}
