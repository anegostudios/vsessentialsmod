using ProtoBuf;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
    public class ClothSystem
    {
        [ProtoMember(1)]
        public int ClothId;
        [ProtoMember(2)]
        EnumClothType clothType;
        [ProtoMember(3)]
        List<List<ClothPoint>> Points2d = new List<List<ClothPoint>>();
        [ProtoMember(4)]
        List<ClothConstraint> Constraints = new List<ClothConstraint>();


        /// <summary>
        /// 10 joints per meter
        /// </summary>
        public static float Resolution = 10;
        public bool LineDebug;

        public bool boyant = false;

        protected ICoreClientAPI capi;
        protected ICoreAPI api;


        protected BlockPos originPos;
        public Vec3d windSpeed = new Vec3d();

        public ParticlePhysics pp;
        protected NormalizedSimplexNoise noiseGen;
        protected float[] tmpMat = new float[16];
        protected Vec3f distToCam = new Vec3f();



        protected AssetLocation ropeSectionModel;

        protected MeshData debugUpdateMesh;
        protected MeshRef debugMeshRef;



        public bool PinnedAnywhere
        {
            get
            {
                foreach (var pointlist in Points2d)
                {
                    foreach (var point in pointlist)
                    {
                        if (point.Pinned) return true;
                    }
                }

                return false;
            }
        }

        public ClothPoint FirstPoint => Points2d[0][0];
        public ClothPoint LastPoint
        {
            get
            {
                var points = Points2d[Points2d.Count - 1];
                return points[points.Count - 1];
            }
        }

        public ClothPoint[] Ends => new ClothPoint[] { FirstPoint, LastPoint };

        public int Width => Points2d.Count;
        public int Length => Points2d[0].Count;


        public static ClothSystem CreateCloth(ICoreAPI api, ClothManager cm, BlockPos originPos, float xsize, float zsize)
        {
            return new ClothSystem(api, cm, originPos, xsize, zsize, EnumClothType.Cloth);
        }

        public static ClothSystem CreateRope(ICoreAPI api, ClothManager cm, BlockPos originPos, float length, AssetLocation clothSectionModel)
        {
            return new ClothSystem(api, cm, originPos, length, 0, EnumClothType.Rope, clothSectionModel);
        }



        private ClothSystem(ICoreAPI api, ClothManager cm, BlockPos originPos, float xsize, float zsize, EnumClothType clothType, AssetLocation ropeSectionModel = null)
        {
            this.clothType = clothType;
            this.ropeSectionModel = ropeSectionModel;

            this.api = api;
            this.capi = api as ICoreClientAPI;
            pp = cm.partPhysics;

            this.originPos = originPos;

            if (capi != null && LineDebug)
            {
                debugUpdateMesh = new MeshData(20, 15, false, false, true, true);
            }

            noiseGen = NormalizedSimplexNoise.FromDefaultOctaves(4, 100, 0.9, api.World.Seed + originPos.GetHashCode());
            Random rand = api.World.Rand;

            bool hor = rand.NextDouble() < 0.5;
            int vertexIndex = 0;
            

            float step = 1 / Resolution;
            int numzpoints = (int)Math.Round(zsize * Resolution);
            int numxpoints = (int)Math.Round(xsize * Resolution);

            if (clothType == EnumClothType.Rope)
            {
                numzpoints = 1;
            }


            float roughness = 0.05f;
            int k = 0;

            int pointIndex = 0;

            for (int z = 0; z < numzpoints; z++)
            {
                Points2d.Add(new List<ClothPoint>());

                for (int x = 0; x < numxpoints; x++)
                {
                    float dx = x * step;
                    float dy = z * step;
                    float dz = -roughness/2 + (float)rand.NextDouble() * roughness;

                    if (hor)
                    {
                        dx = x * step;
                        dy = -roughness/2 + (float)rand.NextDouble() * roughness;
                        dz = z * step;
                    }

                    var point = new ClothPoint(this, pointIndex++, originPos.X + dx, originPos.Y + dy, originPos.Z + dz);

                    Points2d[z].Add(point);

                    int color = (k++ % 2) > 0 ? ColorUtil.WhiteArgb : ColorUtil.BlackArgb;

                    // add a vertical constraint
                    if (z > 0)
                    {
                        ClothPoint p1 = Points2d[z - 1][x];
                        ClothPoint p2 = Points2d[z][x];

                        var constraint = new ClothConstraint(p1, p2);
                        Constraints.Add(constraint);

                        if (capi != null)
                        {
                            if (LineDebug)
                            {
                                debugUpdateMesh.AddVertex(0, 0, 0, color);
                                debugUpdateMesh.AddVertex(0, 0, 0, color);

                                debugUpdateMesh.AddIndex(vertexIndex++);
                                debugUpdateMesh.AddIndex(vertexIndex++);
                            }
                        }
                    }

                    // add a new horizontal constraints
                    if (x > 0)
                    {
                        ClothPoint p1 = Points2d[z][x - 1];
                        ClothPoint p2 = Points2d[z][x];

                        var constraint = new ClothConstraint(p1, p2);
                        Constraints.Add(constraint);

                        if (capi != null)
                        {
                            if (LineDebug)
                            {
                                debugUpdateMesh.AddVertex(0, 0, 0, color);
                                debugUpdateMesh.AddVertex(0, 0, 0, color);

                                debugUpdateMesh.AddIndex(vertexIndex++);
                                debugUpdateMesh.AddIndex(vertexIndex++);
                            }
                        }
                    }
                }
            }



            if (capi != null && LineDebug)
            {
                debugUpdateMesh.mode = EnumDrawMode.Lines;
                debugMeshRef = capi.Render.UploadMesh(debugUpdateMesh);

                debugUpdateMesh.Indices = null;
                debugUpdateMesh.Rgba = null;
            }
        }



        public void WalkPoints(API.Common.Action<ClothPoint> onPoint)
        {
            foreach (var points in Points2d)
            {
                foreach (var point in points)
                {
                    onPoint(point);
                }
            }
        }


        public int UpdateMesh(MeshData updateMesh, float dt)
        {

            var cf = updateMesh.CustomFloats;

            Vec3d campos = capi.World.Player.Entity.CameraPos;


            int basep = cf.Count;

            /*if (cf.Values.Length - basep < point2Line.Count * 20)
            {
                cf.GrowBuffer(point2Line.Count * 20);
            }*/

            Vec4f lightRgba = api.World.BlockAccessor.GetLightRGBs(Constraints[Constraints.Count / 2].Point1.Pos.AsBlockPos);

            if (clothType == EnumClothType.Rope)
            {
                for (int i = 0; i < Constraints.Count; i++)
                {
                    ClothConstraint cc = Constraints[i];

                    Vec3d start = cc.Point1.Pos;
                    Vec3d end = cc.Point2.Pos;

                    double dX = start.X - end.X;
                    double dY = start.Y - end.Y;
                    double dZ = start.Z - end.Z;


                    float yaw = (float)Math.Atan2(dX, dZ) + GameMath.PIHALF;
                    float pitch = (float)Math.Atan2(Math.Sqrt(dZ * dZ + dX * dX), dY) + GameMath.PIHALF;

                    double nowx = start.X + (start.X - end.X) / 2;
                    double nowy = start.Y + (start.Y - end.Y) / 2;
                    double nowz = start.Z + (start.Z - end.Z) / 2;

                    cc.renderCenterPos.X += (nowx - cc.renderCenterPos.X) * dt * 20;
                    cc.renderCenterPos.Y += (nowy - cc.renderCenterPos.Y) * dt * 20;
                    cc.renderCenterPos.Z += (nowz - cc.renderCenterPos.Z) * dt * 20;

                    distToCam.Set(
                        (float)(cc.renderCenterPos.X - campos.X),
                        (float)(cc.renderCenterPos.Y - campos.Y),
                        (float)(cc.renderCenterPos.Z - campos.Z)
                    );

                    Mat4f.Identity(tmpMat);

                    Mat4f.Translate(tmpMat, tmpMat, 0, 1 / 32f, 0);

                    Mat4f.Translate(tmpMat, tmpMat, distToCam.X, distToCam.Y, distToCam.Z);
                    Mat4f.RotateY(tmpMat, tmpMat, yaw);
                    Mat4f.RotateZ(tmpMat, tmpMat, pitch);

                    float roll = i / 5f;
                    Mat4f.RotateX(tmpMat, tmpMat, roll);

                    Mat4f.Scale(tmpMat, tmpMat, new float[] { (float)cc.SpringLength, 1, 1 }); // + (float)Math.Sin(api.World.ElapsedMilliseconds / 1000f) * 0.1f
                    Mat4f.Translate(tmpMat, tmpMat, -1.5f, -1 / 32f, -0.5f); // not sure why the -1.5 here instead of -0.5



                    int j = basep + i * 20;
                    cf.Values[j++] = lightRgba.R;
                    cf.Values[j++] = lightRgba.G;
                    cf.Values[j++] = lightRgba.B;
                    cf.Values[j++] = lightRgba.A;

                    for (int k = 0; k < 16; k++)
                    {
                        cf.Values[j + k] = tmpMat[k];
                    }
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
                for (int i = 0; i < Constraints.Count; i++)
                {
                    ClothPoint p1 = Constraints[i].Point1;
                    ClothPoint p2 = Constraints[i].Point2;

                    debugUpdateMesh.xyz[i * 6 + 0] = (float)(p1.Pos.X - originPos.X);
                    debugUpdateMesh.xyz[i * 6 + 1] = (float)(p1.Pos.Y - originPos.Y) + 0.005f;
                    debugUpdateMesh.xyz[i * 6 + 2] = (float)(p1.Pos.Z - originPos.Z);

                    debugUpdateMesh.xyz[i * 6 + 3] = (float)(p2.Pos.X - originPos.X);
                    debugUpdateMesh.xyz[i * 6 + 4] = (float)(p2.Pos.Y - originPos.Y) + 0.005f;
                    debugUpdateMesh.xyz[i * 6 + 5] = (float)(p2.Pos.Z - originPos.Z);
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
            windSpeed = api.World.BlockAccessor.GetWindSpeedAt(originPos) * (0.2 + noiseGen.Noise(0, api.World.Calendar.TotalHours * 50) * 0.8);

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
            int numc = Constraints.Count;
            
            // make sure all the constraints are satisfied.
            for (int i = 0; i < numc; i++)
            {
                Constraints[i].satisfy(pdt);
            }

            // move each point with a pull from gravity
            for (int i = 0; i < Points2d.Count; i++)
            {
                for (int j = 0; j < Points2d[i].Count; j++)
                {
                    Points2d[i][j].update(pdt);
                }
            }

        }

    }
}
