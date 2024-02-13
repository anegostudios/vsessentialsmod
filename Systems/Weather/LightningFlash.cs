using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class LightningFlash : IDisposable
    {
        MeshRef quadRef;
        Vec4f color;
        float linewidth;
        public List<Vec3d> points;
        public LightiningPointLight[] pointLights = new LightiningPointLight[2];
        public Vec3d origin;

        public float secondsAlive;

        public bool Alive = true;

        ICoreAPI api;
        ICoreClientAPI capi;

        public float flashAccum;
        public float rndVal;
        public float advanceWaitSec;

        bool soundPlayed = false;
        WeatherSystemBase weatherSys;

        Random rand;

        public LightningFlash(WeatherSystemBase weatherSys, ICoreAPI api, int? seed, Vec3d startpoint)
        {
            this.weatherSys = weatherSys;
            this.capi = api as ICoreClientAPI;
            this.api = api;

            rand = new Random(seed == null ? capi.World.Rand.Next() : (int)seed);

            color = new Vec4f(1, 1, 1, 1);
            linewidth = 0.33f;

            origin = startpoint.Clone();
            origin.Y = api.World.BlockAccessor.GetRainMapHeightAt((int)origin.X, (int)origin.Z) + 1;

        }

        public void ClientInit()
        {
            genPoints(weatherSys);
            genMesh(points);

            float b = 200;
            pointLights[0] = new LightiningPointLight(new Vec3f(b,b,b), points[0].AddCopy(origin));
            pointLights[1] = new LightiningPointLight(new Vec3f(0,0,0), points[points.Count - 1].AddCopy(origin));

            capi.Render.AddPointLight(pointLights[0]);
            capi.Render.AddPointLight(pointLights[1]);

            var lp = points[points.Count - 1];
            Vec3d pos = origin + lp;
            var plr = capi.World.Player;
            float dist = (float)plr.Entity.Pos.DistanceTo(pos);

            if (dist < 150)
            {
                var loc = new AssetLocation("sounds/weather/lightning-verynear.ogg");
                capi.World.PlaySoundAt(loc, 0, 0, 0, null, EnumSoundType.Weather, 1, 32, 1 - dist / 180);
            } else
            {
                if (dist < 200)
                {
                    var loc = new AssetLocation("sounds/weather/lightning-near.ogg");
                    capi.World.PlaySoundAt(loc, 0, 0, 0, null, EnumSoundType.Weather, 1, 32, 1 - dist / 250);
                } else
                {
                    if (dist < 320)
                    {
                        var loc = new AssetLocation("sounds/weather/lightning-distant.ogg");
                        capi.World.PlaySoundAt(loc, 0, 0, 0, null, EnumSoundType.Weather, 1, 32, 1 - dist / 500);
                    }
                }
            }
        }

        protected void genPoints(WeatherSystemBase weatherSys)
        {
            Vec3d pos = new Vec3d();

            points = new List<Vec3d>();

            pos.Y = 0;
            
            float startY = (float)(weatherSys.CloudLevelRel * capi.World.BlockAccessor.MapSizeY + 2 - origin.Y);

            while (pos.Y < startY)
            {
                points.Add(pos.Clone());

                pos.Y += rand.NextDouble();
                pos.X += rand.NextDouble() * 2.0 - 1 / 1.0;
                pos.Z += rand.NextDouble() * 2.0 - 1 / 1.0;
            }

            if (points.Count == 0) points.Add(pos.Clone());

            points.Reverse();
        }

        protected void genMesh(List<Vec3d> points)
        {
            float[] data = new float[points.Count * 3];
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                data[i * 3 + 0] = (float)point.X;
                data[i * 3 + 1] = (float)point.Y;
                data[i * 3 + 2] = (float)point.Z;
            }

            quadRef?.Dispose();

            var quadMesh = CubeMeshUtil.GetCube(0.5f, 0.5f, 0.5f, new Vec3f(0f, 0f, 0f));
            quadMesh.Flags = null;
            quadMesh.Rgba = null;

            quadMesh.CustomFloats = new CustomMeshDataPartFloat()
            {
                Instanced = true,
                InterleaveOffsets = new int[] { 0, 12 },
                InterleaveSizes = new int[] { 3, 3 },
                InterleaveStride = 12,
                StaticDraw = false,
                Values = data,
                Count = data.Length
            };

            var updateMesh = new MeshData(false);
            updateMesh.CustomFloats = quadMesh.CustomFloats;

            quadRef = capi.Render.UploadMesh(quadMesh);

            capi.Render.UpdateMesh(quadRef, updateMesh);

        }

        public void GameTick(float dt)
        {
            dt *= 3;

            if (rand.NextDouble() < 0.4 && secondsAlive * 10 < 0.6 && advanceWaitSec <= 0)
            {
                advanceWaitSec = 0.05f + (float)rand.NextDouble() / 10f;
            }

            secondsAlive += Math.Max(0, dt - advanceWaitSec);
            advanceWaitSec = Math.Max(0, advanceWaitSec - dt);


            if (secondsAlive > 0.7)
            {
                Alive = false;
            }

            if (api.Side == EnumAppSide.Server && secondsAlive > 0)
            {
                weatherSys.TriggerOnLightningImpactEnd(origin, out var handling);

                if (handling == EnumHandling.PassThrough)
                {
                    if (api.World.Config.GetBool("lightningDamage", true))
                    {
                        var dmgSrc = new DamageSource()
                        {
                            KnockbackStrength = 2,
                            Source = EnumDamageSource.Weather,
                            Type = EnumDamageType.Electricity,
                            SourcePos = origin,
                            HitPosition = new Vec3d()
                        };

                        api.ModLoader.GetModSystem<EntityPartitioning>().WalkEntities(origin, 8, (entity) =>
                        {
                            if (!entity.IsInteractable) return true;
                            float damage = 6;
                            entity.ReceiveDamage(dmgSrc, damage);
                            return true;
                        }, EnumEntitySearchType.Creatures);
                    }
                }

            }

        }

        public void Render(float dt)
        {
            GameTick(dt);

            capi.Render.CurrentActiveShader.Uniform("color", color);
            capi.Render.CurrentActiveShader.Uniform("lineWidth", linewidth);

            var plr = capi.World.Player;
            Vec3d camPos = plr.Entity.CameraPos;
            capi.Render.CurrentActiveShader.Uniform("origin", new Vec3f((float)(origin.X - camPos.X), (float)(origin.Y - camPos.Y), (float)(origin.Z - camPos.Z)));

            double cntRel = GameMath.Clamp(secondsAlive * 10, 0, 1);

            int instanceCount = (int)(cntRel * points.Count) - 1;
            if (instanceCount > 0) capi.Render.RenderMeshInstanced(quadRef, instanceCount);

            if (cntRel >= 0.9 && !soundPlayed)
            {
                soundPlayed = true;
                var lp = points[points.Count - 1];
                Vec3d pos = origin + lp;
                float dist = (float)plr.Entity.Pos.DistanceTo(pos);
                
                if (dist < 150)
                {
                    var loc = new AssetLocation("sounds/weather/lightning-nodistance.ogg");
                    capi.World.PlaySoundAt(loc, 0, 0, 0, null, EnumSoundType.Weather, 1, 32, Math.Max(0.1f, 1 - dist / 70));
                }

                if (dist < 100)
                {
                    (weatherSys as WeatherSystemClient).simLightning.lightningTime = 0.3f + (float)rand.NextDouble() * 0.17f;
                    (weatherSys as WeatherSystemClient).simLightning.lightningIntensity = 1.5f + (float)rand.NextDouble() * 0.4f;

                    int sub = Math.Max(0, (int)dist - 5) * 3;

                    int color = ColorUtil.ToRgba(255, 255, 255, 200);

                    SimpleParticleProperties props = new SimpleParticleProperties(500/2 - sub, 600/2 - sub, color, pos.AddCopy(-0.5f, 0, -0.5f), pos.AddCopy(0.5f, 1f, 0.5f), new Vec3f(-5, 0, -5), new Vec3f(5, 10, 5), 3, 0.3f, 0.4f, 2f);
                    props.VertexFlags = 255;
                    props.ShouldDieInLiquid = true;
                    props.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEARREDUCE, 1f);

                    capi.World.SpawnParticles(props);

                    props.ParticleModel = EnumParticleModel.Quad;
                    props.MinSize /= 2f;
                    props.MaxSize /= 2f;
                    capi.World.SpawnParticles(props);
                }
            }

            flashAccum += dt;
            if (flashAccum > rndVal)
            {
                rndVal = (float)rand.NextDouble() / 10;
                flashAccum = 0;
                float bnorm = (float)rand.NextDouble();
                float b = 50 + bnorm * 150;
                pointLights[0].Color.Set(b, b, b);

                linewidth = (0.4f + 0.6f*bnorm) / 3f;

                if (cntRel < 1) b = 0;
                pointLights[1].Color.Set(b, b, b);
            }

        }

        public void Dispose()
        {
            quadRef?.Dispose();
            capi?.Render.RemovePointLight(pointLights[0]);
            capi?.Render.RemovePointLight(pointLights[1]);
        }
    }


    public class LightiningPointLight : IPointLight
    {
        public LightiningPointLight(Vec3f color, Vec3d pos)
        {
            this.Color = color;
            this.Pos = pos;
        }

        public Vec3f Color { get; set; }

        public Vec3d Pos { get; set; }
    }


}