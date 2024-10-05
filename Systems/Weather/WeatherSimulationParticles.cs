using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class HailParticleProps : WeatherParticleProps
    {
        public override Vec3d Pos
        {
            get
            {
                double px = MinPos.X + (rand.NextDouble() * rand.NextDouble()) * 80 * (1 - 2 * rand.Next(2));
                double pz = MinPos.Z + (rand.NextDouble() * rand.NextDouble()) * 80 * (1 - 2 * rand.Next(2)); 
                
                tmpPos.Set( px, MinPos.Y + AddPos.Y * rand.NextDouble(), pz);

                int dx = (int)(tmpPos.X - centerPos.X);
                int dz = (int)(tmpPos.Z - centerPos.Z);

                int lx = GameMath.Clamp(dx / 4 + 8, 0, 15);
                int lz = GameMath.Clamp(dz / 4 + 8, 0, 15);
                tmpPos.Y = Math.Max(tmpPos.Y, lowResRainHeightMap[lx, lz] + 3);

                return tmpPos;
            }
        }
    }

    public class WeatherParticleProps : SimpleParticleProperties
    {
        public int[,] lowResRainHeightMap;
        public BlockPos centerPos;

        public override Vec3d Pos {
            get
            {
                tmpPos.Set(
                    MinPos.X + AddPos.X * rand.NextDouble(),
                    MinPos.Y + AddPos.Y * rand.NextDouble(),
                    MinPos.Z + AddPos.Z * rand.NextDouble()
                );

                int dx = (int)(tmpPos.X - centerPos.X);
                int dz = (int)(tmpPos.Z - centerPos.Z);

                int lx = GameMath.Clamp(dx / 4 + 8, 0, 15);
                int lz = GameMath.Clamp(dz / 4 + 8, 0, 15);
                tmpPos.Y = Math.Max(tmpPos.Y, lowResRainHeightMap[lx, lz] + 3);

                return tmpPos;
            }
        }
    }

    public class WeatherSimulationParticles
    {
        WeatherSystemClient ws;
        ICoreClientAPI capi;
        Random rand;
        static int[,] lowResRainHeightMap = new int[16, 16];
        static BlockPos centerPos = new BlockPos();

        #region Particle

        public static int waterColor = ColorUtil.ToRgba(230, 128, 178, 255);
        public static int lowStabColor = ColorUtil.ToRgba(230, 207, 53, 10);

        public int rainParticleColor;

        public static SimpleParticleProperties splashParticles = new SimpleParticleProperties()
        {
            MinPos = new Vec3d(),
            AddPos = new Vec3d(1, 0.25, 0),
            MinQuantity = 0,
            AddQuantity = 3,
            Color = ColorUtil.ToRgba(230, 128, 178, 200),
            GravityEffect = 1f,
            WithTerrainCollision = true,
            ParticleModel = EnumParticleModel.Quad,
            LifeLength = 0.5f,
            MinVelocity = new Vec3f(-1, 2, -1),
            AddVelocity = new Vec3f(2, 0, 2),
            MinSize = 0.07f,
            MaxSize = 0.2f,
            VertexFlags = 32
        };

        public static WeatherParticleProps stormDustParticles = new WeatherParticleProps()
        {
            MinPos = new Vec3d(),
            AddPos = new Vec3d(),
            MinQuantity = 0,
            AddQuantity = 3,
            Color = ColorUtil.ToRgba(100, 200, 200, 200),
            GravityEffect = 1f,
            WithTerrainCollision = true,
            ParticleModel = EnumParticleModel.Quad,
            LifeLength = 0.5f,
            MinVelocity = new Vec3f(-1, 2, -1),
            AddVelocity = new Vec3f(2, 0, 2),
            MinSize = 0.07f,
            MaxSize = 0.1f
        };

        public static SimpleParticleProperties stormWaterParticles = new SimpleParticleProperties()
        {
            MinPos = new Vec3d(),
            AddPos = new Vec3d(),
            MinQuantity = 0,
            AddQuantity = 3,
            Color = ColorUtil.ToRgba(230, 128, 178, 200),
            GravityEffect = 1f,
            WithTerrainCollision = true,
            ParticleModel = EnumParticleModel.Quad,
            LifeLength = 0.5f,
            MinVelocity = new Vec3f(-1, 2, -1),
            AddVelocity = new Vec3f(2, 0, 2),
            MinSize = 0.07f,
            MaxSize = 0.2f,
            VertexFlags = 0
        };


        public static WeatherParticleProps rainParticle = new WeatherParticleProps()
        {
            MinPos = new Vec3d(),
            AddPos = new Vec3d(60, 9, 60),
            MinQuantity = 300,
            AddQuantity = 25,
            Color = waterColor,
            GravityEffect = 1f,
            WithTerrainCollision = false,
            DieOnRainHeightmap = true,
            ShouldDieInLiquid = true,
            ParticleModel = EnumParticleModel.Quad,
            LifeLength = 1.5f,
            MinVelocity = new Vec3f(-0.25f, -0.25f, -0.25f),
            AddVelocity = new Vec3f(0.5f, 0, 0.5f),
            MinSize = 0.15f,
            MaxSize = 0.22f,
            VertexFlags = 32 | (1 << 31)
        };


        public static WeatherParticleProps hailParticle = new HailParticleProps()
        {
            MinPos = new Vec3d(),
            AddPos = new Vec3d(60, 0, 60),
            MinQuantity = 50,
            AddQuantity = 25,
            Color = ColorUtil.ToRgba(255, 255, 255, 255),
            GravityEffect = 1f,
            WithTerrainCollision = true,
            DieOnRainHeightmap = false,
            ShouldDieInLiquid = false,
            ShouldSwimOnLiquid = true,
            ParticleModel = EnumParticleModel.Cube,
            LifeLength = 3f,
            MinVelocity = new Vec3f(-1, -2, -1),
            AddVelocity = new Vec3f(2, 0, 2),
            MinSize = 0.1f,
            MaxSize = 0.14f,
            WindAffectednes = 0f,
            ParentVelocity = null,
            Bounciness = 0.3f
        };

        static WeatherParticleProps snowParticle = new WeatherParticleProps()
        {
            MinPos = new Vec3d(),
            AddPos = new Vec3d(60, 0, 60),
            MinQuantity = 80,
            AddQuantity = 15,
            Color = ColorUtil.ToRgba(200, 255, 255, 255),
            GravityEffect = 0.003f,
            WithTerrainCollision = true,
            DieOnRainHeightmap = false,
            ShouldDieInLiquid = false,
            ParticleModel = EnumParticleModel.Quad,
            LifeLength = 5f,
            MinVelocity = new Vec3f(-3.5f, -1.25f, -0.5f),
            AddVelocity = new Vec3f(1f, 0.05f, 1f),
            MinSize = 0.1f,
            MaxSize = 0.2f
        };

        static WeatherSimulationParticles()
        {
            stormDustParticles.lowResRainHeightMap = lowResRainHeightMap;
            hailParticle.lowResRainHeightMap = lowResRainHeightMap;
            snowParticle.lowResRainHeightMap = lowResRainHeightMap;
            rainParticle.lowResRainHeightMap = lowResRainHeightMap;

            stormDustParticles.centerPos = centerPos;
            hailParticle.centerPos = centerPos;
            snowParticle.centerPos = centerPos;
            rainParticle.centerPos = centerPos;
        }

        #endregion

        Block lblock;
        Vec3f parentVeloSnow = new Vec3f();
        BlockPos tmpPos = new BlockPos();
        Vec3d particlePos = new Vec3d();

        #region desert storm ambient
        AmbientModifier desertStormAmbient;
        int spawnCount;
        float sandFinds;
        int dustParticlesPerTick = 30;
        float[] sandCountByBlock;

        float[] targetFogColor = new float[3];
        float targetFogDensity;

        Dictionary<int, int> indicesBySandBlockId = new Dictionary<int, int>();

        #endregion

        public WeatherSimulationParticles(ICoreClientAPI capi, WeatherSystemClient ws)
        {
            this.capi = capi;
            this.ws = ws;
            rand = new Random(capi.World.Seed + 223123123);
            rainParticleColor = waterColor;

            desertStormAmbient = new AmbientModifier().EnsurePopulated();
            desertStormAmbient.FogDensity = new WeightedFloat();
            desertStormAmbient.FogColor = new WeightedFloatArray() { Value = new float[3] };
            desertStormAmbient.FogMin = new WeightedFloat();

            capi.Ambient.CurrentModifiers["desertstorm"] = desertStormAmbient;
        }

        public void Initialize()
        {
            lblock = capi.World.GetBlock(new AssetLocation("water-still-7"));

            if (lblock != null)
            {
                capi.Event.RegisterAsyncParticleSpawner(asyncParticleSpawn);
                capi.Event.RegisterRenderer(new DummyRenderer() { action = desertStormSim }, EnumRenderStage.Before);

                int i = 0;
                foreach (var block in capi.World.Blocks)
                {
                    if (block.BlockMaterial == EnumBlockMaterial.Sand)
                    {
                        indicesBySandBlockId[block.Id] = i++;
                    }
                }

                sandCountByBlock = new float[indicesBySandBlockId.Count];
            }
        }

        float accum;
        private void desertStormSim(float dt)
        {
            accum += dt;

            if (accum > 2)
            {
                int cnt = spawnCount;
                float sum = sandFinds;
                float[] sandBlocks = sandCountByBlock;

                if (cnt > 10 && sum > 0)
                {
                    sandCountByBlock = new float[indicesBySandBlockId.Count];
                    spawnCount = 0;
                    sandFinds = 0;


                    WeatherDataSnapshot weatherData = ws.BlendedWeatherData;
                    float windIntensity = weatherData.curWindSpeed.Length();
                    var bpos = capi.World.Player.Entity.Pos.AsBlockPos;
                    var climate = capi.World.BlockAccessor.GetClimateAt(bpos, EnumGetClimateMode.NowValues);
                    var sunlightrel = capi.World.BlockAccessor.GetLightLevel(bpos, EnumLightLevelType.OnlySunLight) / 22f;

                    float climateWeight = 2f * Math.Max(0, windIntensity - 0.65f) * (1 - climate.WorldgenRainfall) * (1 - climate.Rainfall);

                    var pos = capi.World.Player.Entity.Pos.AsBlockPos;
                    targetFogColor[0] = targetFogColor[1] = targetFogColor[2] = 0;
                    foreach (var val in indicesBySandBlockId)
                    {
                        float blockCount = sandBlocks[val.Value];
                        float weight = blockCount / sum;

                        int col = capi.World.GetBlock(val.Key).GetColor(capi, pos);
                        double[] colparts = ColorUtil.ToRGBADoubles(col);

                        targetFogColor[0] += (float)colparts[2] * weight;
                        targetFogColor[1] += (float)colparts[1] * weight;
                        targetFogColor[2] += (float)colparts[0] * weight;
                    }
                
                    float sandRatio = (float)(sum / 30.0 / cnt) * climateWeight * sunlightrel;
                    targetFogDensity = sandRatio;

                }

                accum = 0;
            }

            float dtf = dt / 3f;

            targetFogDensity = Math.Max(0, targetFogDensity - 2*WeatherSystemClient.CurrentEnvironmentWetness4h);

            desertStormAmbient.FogColor.Value[0] += (targetFogColor[0] - desertStormAmbient.FogColor.Value[0]) * dtf;
            desertStormAmbient.FogColor.Value[1] += (targetFogColor[1] - desertStormAmbient.FogColor.Value[1]) * dtf;
            desertStormAmbient.FogColor.Value[2] += (targetFogColor[2] - desertStormAmbient.FogColor.Value[2]) * dtf;

            desertStormAmbient.FogDensity.Value += ((float)Math.Pow(targetFogDensity, 1.2f) - desertStormAmbient.FogDensity.Value) * dtf;
            desertStormAmbient.FogDensity.Weight += (targetFogDensity - desertStormAmbient.FogDensity.Weight) * dtf;
            desertStormAmbient.FogColor.Weight += (Math.Min(1, 2*targetFogDensity) - desertStormAmbient.FogColor.Weight) * dtf;
        }

        private bool asyncParticleSpawn(float dt, IAsyncParticleManager manager)
        {
            WeatherDataSnapshot weatherData = ws.BlendedWeatherData;

            ClimateCondition conds = ws.clientClimateCond;
            if (conds == null || !ws.playerChunkLoaded) return true; 
            
            EntityPos plrPos = capi.World.Player.Entity.Pos;
            float precIntensity = conds.Rainfall;

            float plevel = precIntensity * capi.Settings.Int["particleLevel"] / 100f;

            float dryness = GameMath.Clamp(1 - precIntensity, 0, 1);

            tmpPos.Set((int)plrPos.X, (int)plrPos.Y, (int)plrPos.Z);

            precIntensity = Math.Max(0, precIntensity - (float)Math.Max(0, (plrPos.Y - capi.World.SeaLevel - 5000) / 10000f));


            EnumPrecipitationType precType = weatherData.BlendedPrecType;
            if (precType == EnumPrecipitationType.Auto)
            {
                precType = conds.Temperature < weatherData.snowThresholdTemp ? EnumPrecipitationType.Snow : EnumPrecipitationType.Rain;
            }

            int rainYPos = capi.World.BlockAccessor.GetRainMapHeightAt((int)particlePos.X, (int)particlePos.Z);
            particlePos.Set(capi.World.Player.Entity.Pos.X, rainYPos, capi.World.Player.Entity.Pos.Z);

            
            int onwaterSplashParticleColor = capi.World.ApplyColorMapOnRgba(lblock.ClimateColorMapResolved, lblock.SeasonColorMapResolved, ColorUtil.WhiteArgb, (int)particlePos.X, (int)particlePos.Y, (int)particlePos.Z, false);
            byte[] col = ColorUtil.ToBGRABytes(onwaterSplashParticleColor);
            onwaterSplashParticleColor = ColorUtil.ToRgba(94, col[0], col[1], col[2]);

            centerPos.Set((int)particlePos.X, 0, (int)particlePos.Z);
            for (int lx = 0; lx < 16; lx++)
            {
                int dx = (lx - 8) * 4;
                for (int lz = 0; lz < 16; lz++)
                {
                    int dz = (lz - 8) * 4;

                    lowResRainHeightMap[lx, lz] = capi.World.BlockAccessor.GetRainMapHeightAt(centerPos.X + dx, centerPos.Z + dz);
                }
            }


            
            

            parentVeloSnow.X = -Math.Max(0, weatherData.curWindSpeed.X / 2 - 0.15f) * 2;
            parentVeloSnow.Y = 0;
            parentVeloSnow.Z = -Math.Max(0, weatherData.curWindSpeed.Z / 2 - 0.15f) * 2;

            float windIntensity = weatherData.curWindSpeed.Length();

            // Don't spawn if wind speed below 50% or if the player is 10 blocks above ground
            if (windIntensity > 0.5f) // && particlePos.Y - rainYPos < 10
            {
                SpawnDustParticles(manager, weatherData, plrPos, dryness, onwaterSplashParticleColor);
            }

            particlePos.Y = capi.World.Player.Entity.Pos.Y;

            if (precIntensity <= 0.02) return true;

            if (precType == EnumPrecipitationType.Hail)
            {
                SpawnHailParticles(manager, weatherData, conds, plrPos, plevel);
                return true;
            }


            if (precType == EnumPrecipitationType.Rain)
            {
                SpawnRainParticles(manager, weatherData, conds, plrPos, plevel, onwaterSplashParticleColor);
            }


            if (precType == EnumPrecipitationType.Snow)
            {
                SpawnSnowParticles(manager, weatherData, conds, plrPos, plevel);
            }

            return true;
        }


        private void SpawnDustParticles(IAsyncParticleManager manager, WeatherDataSnapshot weatherData, EntityPos plrPos, float dryness, int onwaterSplashParticleColor)
        {
            float dx = (float)(plrPos.Motion.X * 40) - 50 * weatherData.curWindSpeed.X;
            float dy = (float)(plrPos.Motion.Y * 40);
            float dz = (float)(plrPos.Motion.Z * 40) - 50 * weatherData.curWindSpeed.Z;

            double range = 40;
            // Particles are less visible during fog so we can abuse the situation to make it more particle rich
            float rangReduction = 1 - targetFogDensity;
            range *= rangReduction;

            float intensity = weatherData.curWindSpeed.Length();

            stormDustParticles.MinPos.Set(particlePos.X - range + dx, particlePos.Y + 20 + 5 * intensity + dy, particlePos.Z - range + dz);
            stormDustParticles.AddPos.Set(2*range, -30, 2*range);
            stormDustParticles.GravityEffect = 0.1f;
            stormDustParticles.ParticleModel = EnumParticleModel.Quad;
            stormDustParticles.LifeLength = 1f;
            stormDustParticles.DieOnRainHeightmap = true;
            stormDustParticles.WindAffectednes = 8f;
            stormDustParticles.MinQuantity = 0;
            stormDustParticles.AddQuantity = 8 * (intensity - 0.5f) * dryness;

            stormDustParticles.MinSize = 0.2f;
            stormDustParticles.MaxSize = 0.7f;

            stormDustParticles.MinVelocity.Set(-0.025f + 12 * weatherData.curWindSpeed.X, 0f, -0.025f + 12 * weatherData.curWindSpeed.Z).Mul(3);
            stormDustParticles.AddVelocity.Set(0.05f + 6 * weatherData.curWindSpeed.X, -0.25f, 0.05f + 6 * weatherData.curWindSpeed.Z).Mul(3);

            float extra = Math.Max(1, intensity * 8);
            int cnt = (int)(dustParticlesPerTick * extra);

            for (int i = 0; i < cnt; i++)
            {
                double px = particlePos.X + dx + (rand.NextDouble() * rand.NextDouble()) * 60 * (1 - 2 * rand.Next(2));
                double pz = particlePos.Z + dz + (rand.NextDouble() * rand.NextDouble()) * 60 * (1 - 2 * rand.Next(2));

                int py = capi.World.BlockAccessor.GetRainMapHeightAt((int)px, (int)pz);
                Block block = capi.World.BlockAccessor.GetBlock((int)px, py, (int)pz);
                if (block.Id == 0) continue;
                if (capi.World.BlockAccessor.GetBlock((int)px, py, (int)pz, BlockLayersAccess.Fluid).Id != 0) continue;    // Liquid surface or ice produces no particles
                if (block.BlockMaterial != EnumBlockMaterial.Sand && block.BlockMaterial != EnumBlockMaterial.Snow)
                {
                    if (rand.NextDouble() < 0.7f || block.RenderPass == EnumChunkRenderPass.TopSoil) continue;
                }
                if (block.BlockMaterial == EnumBlockMaterial.Sand)
                {
                    sandFinds+=1/ extra;
                    sandCountByBlock[indicesBySandBlockId[block.Id]]+=1/ extra;
                }

                if (Math.Abs(py - particlePos.Y) > 15) continue;

                tmpPos.Set((int)px, py, (int)pz);
                stormDustParticles.Color = ColorUtil.ReverseColorBytes(block.GetColor(capi, tmpPos));
                stormDustParticles.Color |= 255 << 24;

                manager.Spawn(stormDustParticles);
            }



            spawnCount++;

            if (weatherData.curWindSpeed.Length() > 0.85f)
            {
                stormWaterParticles.AddVelocity.Y = 1.5f;
                stormWaterParticles.LifeLength = 0.17f;
                stormWaterParticles.WindAffected = true;
                stormWaterParticles.WindAffectednes = 1f;
                stormWaterParticles.GravityEffect = 0.4f;
                stormWaterParticles.MinVelocity.Set(-0.025f + 4 * weatherData.curWindSpeed.X, 1.5f, -0.025f + 4 * weatherData.curWindSpeed.Z);
                stormWaterParticles.Color = onwaterSplashParticleColor;
                //stormWaterParticles.Color |= 140 << 24;
                stormWaterParticles.MinQuantity = 1;
                stormWaterParticles.AddQuantity = 5;
                stormWaterParticles.ShouldDieInLiquid = false;

                splashParticles.WindAffected = true;
                splashParticles.WindAffectednes = 1f;

                for (int i = 0; i < 20; i++)
                {
                    double px = particlePos.X + (rand.NextDouble() * rand.NextDouble()) * 40 * (1 - 2 * rand.Next(2));
                    double pz = particlePos.Z + (rand.NextDouble() * rand.NextDouble()) * 40 * (1 - 2 * rand.Next(2));
                    int py = capi.World.BlockAccessor.GetRainMapHeightAt((int)px, (int)pz);

                    Block block = capi.World.BlockAccessor.GetBlock((int)px, py, (int)pz, BlockLayersAccess.Fluid);
                    if (!block.IsLiquid()) continue;

                    stormWaterParticles.MinPos.Set(px, py + block.TopMiddlePos.Y, pz);
                    stormWaterParticles.ParticleModel = EnumParticleModel.Cube;
                    stormWaterParticles.MinSize = 0.4f;

                    manager.Spawn(stormWaterParticles);



                    splashParticles.MinPos.Set(px, py + block.TopMiddlePos.Y - 1 / 8f, pz);
                    splashParticles.MinVelocity.X = weatherData.curWindSpeed.X * 1.5f;
                    splashParticles.AddVelocity.Y = 1.5f;
                    splashParticles.MinVelocity.Z = weatherData.curWindSpeed.Z * 1.5f;
                    splashParticles.LifeLength = 0.17f;

                    splashParticles.Color = onwaterSplashParticleColor;
                    manager.Spawn(splashParticles);
                }
            }
        }

        private void SpawnHailParticles(IAsyncParticleManager manager, WeatherDataSnapshot weatherData, ClimateCondition conds, EntityPos plrPos, float plevel)
        {
            float dx = (float)(plrPos.Motion.X * 40) - 4 * weatherData.curWindSpeed.X;
            float dy = (float)(plrPos.Motion.Y * 40);
            float dz = (float)(plrPos.Motion.Z * 40) - 4 * weatherData.curWindSpeed.Z;

            hailParticle.MinPos.Set(particlePos.X + dx, particlePos.Y + 15 + dy, particlePos.Z + dz);

            hailParticle.MinSize = 0.3f * (0.5f + conds.Rainfall); // * weatherData.PrecParticleSize;
            hailParticle.MaxSize = 1f * (0.5f + conds.Rainfall); // * weatherData.PrecParticleSize;
                                                                 //hailParticle.AddPos.Set(80, 5, 80);

            hailParticle.Color = ColorUtil.ToRgba(220, 210, 230, 255);

            hailParticle.MinQuantity = 100 * plevel;
            hailParticle.AddQuantity = 25 * plevel;
            hailParticle.MinVelocity.Set(-0.025f + 7.5f * weatherData.curWindSpeed.X, -5f, -0.025f + 7.5f * weatherData.curWindSpeed.Z);
            hailParticle.AddVelocity.Set(0.05f + 7.5f * weatherData.curWindSpeed.X, 0.05f, 0.05f + 7.5f * weatherData.curWindSpeed.Z);

            manager.Spawn(hailParticle);
        }

        private void SpawnRainParticles(IAsyncParticleManager manager, WeatherDataSnapshot weatherData, ClimateCondition conds, EntityPos plrPos, float plevel, int onwaterSplashParticleColor)
        {
            float dx = (float)(plrPos.Motion.X * 80);
            float dy = (float)(plrPos.Motion.Y * 80);
            float dz = (float)(plrPos.Motion.Z * 80);

            rainParticle.MinPos.Set(particlePos.X - 30 + dx, particlePos.Y + 15 + dy, particlePos.Z - 30 + dz);
            rainParticle.WithTerrainCollision = false;
            rainParticle.MinQuantity = 1000 * plevel;
            rainParticle.LifeLength = 1f;
            rainParticle.AddQuantity = 25 * plevel;
            rainParticle.MinSize = 0.15f * (0.5f + conds.Rainfall); // * weatherData.PrecParticleSize;
            rainParticle.MaxSize = 0.22f * (0.5f + conds.Rainfall); // weatherData.PrecParticleSize;
            rainParticle.Color = rainParticleColor;

            rainParticle.MinVelocity.Set(-0.025f + 8 * weatherData.curWindSpeed.X, -10f, -0.025f + 8 * weatherData.curWindSpeed.Z);
            rainParticle.AddVelocity.Set(0.05f + 8 * weatherData.curWindSpeed.X, 0.05f, 0.05f + 8 * weatherData.curWindSpeed.Z);

            manager.Spawn(rainParticle);


            splashParticles.MinVelocity = new Vec3f(-1f, 3, -1f);
            splashParticles.AddVelocity = new Vec3f(2, 0, 2);
            splashParticles.LifeLength = 0.1f;
            splashParticles.MinSize = 0.07f * (0.5f + 0.65f * conds.Rainfall);// weatherData.PrecParticleSize;
            splashParticles.MaxSize = 0.2f * (0.5f + 0.65f * conds.Rainfall); // weatherData.PrecParticleSize;
            splashParticles.ShouldSwimOnLiquid = true;
            splashParticles.Color = rainParticleColor;

            float cnt = 100 * plevel;

            for (int i = 0; i < cnt; i++)
            {
                double px = particlePos.X + (rand.NextDouble() * rand.NextDouble()) * 60 * (1 - 2 * rand.Next(2));
                double pz = particlePos.Z + (rand.NextDouble() * rand.NextDouble()) * 60 * (1 - 2 * rand.Next(2));

                int py = capi.World.BlockAccessor.GetRainMapHeightAt((int)px, (int)pz);

                Block block = capi.World.BlockAccessor.GetBlock((int)px, py, (int)pz, BlockLayersAccess.Fluid);

                if (block.IsLiquid())
                {
                    splashParticles.MinPos.Set(px, py + block.TopMiddlePos.Y - 1 / 8f, pz);
                    splashParticles.AddVelocity.Y = 1.5f;
                    splashParticles.LifeLength = 0.17f;
                    splashParticles.Color = onwaterSplashParticleColor;
                }
                else
                {
                    if (block.BlockId == 0) block = capi.World.BlockAccessor.GetBlock((int)px, py, (int)pz);   // block read from LiquidsLayer could be ice, in which case no need to read from the physical blocks layer

                    double b = 0.75 + 0.25 * rand.NextDouble();
                    int ca = 230 - rand.Next(100);
                    int cr = (int)(((rainParticleColor >> 16) & 0xff) * b);
                    int cg = (int)(((rainParticleColor >> 8) & 0xff) * b);
                    int cb = (int)(((rainParticleColor >> 0) & 0xff) * b);

                    splashParticles.Color = (ca << 24) | (cr << 16) | (cg << 8) | cb;
                    splashParticles.AddVelocity.Y = 0f;
                    splashParticles.LifeLength = 0.1f;
                    splashParticles.MinPos.Set(px, py + block.TopMiddlePos.Y + 0.05, pz);
                }

                manager.Spawn(splashParticles);
            }
        }

        private void SpawnSnowParticles(IAsyncParticleManager manager, WeatherDataSnapshot weatherData, ClimateCondition conds, EntityPos plrPos, float plevel)
        {
            snowParticle.WindAffected = true;
            snowParticle.WindAffectednes = 1f;

            float wetness = 2.5f * GameMath.Clamp(ws.clientClimateCond.Temperature + 1, 0, 4) / 4f;

            float mx = (float)plrPos.Motion.X * 60;
            float mz = (float)plrPos.Motion.Z * 60;

            float horSpeedSqrt = (float)Math.Pow(mx * mx + mz * mz, 0.25f);

            float dx = (float)(mx) - Math.Max(0, (30 - 9 * wetness) * weatherData.curWindSpeed.X - 5 * horSpeedSqrt);
            float dy = (float)(plrPos.Motion.Y * 60);
            float dz = (float)(mz) - Math.Max(0, (30 - 9 * wetness) * weatherData.curWindSpeed.Z - 5 * horSpeedSqrt);

            float windintensity = weatherData.curWindSpeed.Length();



            snowParticle.MinVelocity.Set(-0.5f + 10 * weatherData.curWindSpeed.X, -1f, -0.5f + 10 * weatherData.curWindSpeed.Z);
            snowParticle.AddVelocity.Set(1f + 10 * weatherData.curWindSpeed.X, 0.05f, 1f + 10 * weatherData.curWindSpeed.Z);
            snowParticle.Color = ColorUtil.ToRgba(255, 255, 255, 255);

            snowParticle.MinQuantity = 100 * plevel * (1 + wetness / 3);
            snowParticle.AddQuantity = 25 * plevel * (1 + wetness / 3);
            snowParticle.ParentVelocity = parentVeloSnow;
            snowParticle.ShouldDieInLiquid = true;

            snowParticle.LifeLength = Math.Max(1f, 4f - wetness - windintensity);
            snowParticle.Color = ColorUtil.ColorOverlay(ColorUtil.ToRgba(255, 255, 255, 255), rainParticle.Color, wetness / 4f);
            snowParticle.GravityEffect = 0.005f * (1 + 20 * wetness);
            snowParticle.MinSize = 0.1f * conds.Rainfall;
            snowParticle.MaxSize = 0.3f * conds.Rainfall / (1 + wetness);


            float hrange = 20;
            float vrange = 23 + windintensity * 5;
            dy -= Math.Min(10, horSpeedSqrt) + windintensity * 5;

            snowParticle.MinVelocity.Y = -2f;

            snowParticle.MinPos.Set(particlePos.X - hrange + dx, particlePos.Y + vrange + dy, particlePos.Z - hrange + dz);
            snowParticle.AddPos.Set(2 * hrange + dx, -0.66f * vrange + dy, 2 * hrange + dz);

            manager.Spawn(snowParticle);
        }
    }
}
