using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

public class ModSystemAmbientParticles : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    SimpleParticleProperties liquidParticles;
    SimpleParticleProperties summerAirParticles;
    SimpleParticleProperties fireflyParticles;

    ClampedSimplexNoise fireflyLocationNoise;
    ClampedSimplexNoise fireflyrateNoise;

    ICoreClientAPI capi;

    public event ActionBoolReturn ShouldSpawnAmbientParticles;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        capi.Event.RegisterGameTickListener(OnSlowTick, 1000);
        capi.Event.RegisterAsyncParticleSpawner(AsyncParticleSpawnTick);

        liquidParticles = new SimpleParticleProperties()
        {
            MinSize = 0.1f,
            MaxSize = 0.1f,
            MinQuantity = 1,
            GravityEffect = 0f,
            LifeLength = 2f,
            ParticleModel = EnumParticleModel.Quad,
            ShouldDieInAir = true,
            VertexFlags = 1 << 9
        };

        summerAirParticles = new SimpleParticleProperties()
        {
            Color = ColorUtil.ToRgba(35, 230, 230, 150),
            ParticleModel = EnumParticleModel.Quad,
            MinSize = 0.05f,
            MaxSize = 0.10f,
            GravityEffect = 0,
            LifeLength = 2f,
            WithTerrainCollision = false,
            ShouldDieInLiquid = true,
            MinVelocity = new Vec3f(-0.125f, -0.125f, -0.125f),
            MinQuantity = 1,
            AddQuantity = 0,
        };
        summerAirParticles.AddVelocity = new Vec3f(0.25f, 0.25f, 0.25f);
        summerAirParticles.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.CLAMPEDPOSITIVESINUS, GameMath.PI);
        summerAirParticles.MinPos = new Vec3d();

        fireflyParticles = new SimpleParticleProperties()
        {
            Color = ColorUtil.ToRgba(150, 150, 250, 139),
            ParticleModel = EnumParticleModel.Quad,
            MinSize = 0.1f,
            MaxSize = 0.1f,
            GravityEffect = 0,
            LifeLength = 2f,
            ShouldDieInLiquid = true,
            MinVelocity = new Vec3f(-0.25f, -0.125f / 2, -0.25f),
            MinQuantity = 2,
            AddQuantity = 0,
            LightEmission = ColorUtil.ToRgba(255, 77, 250, 139)
        };

        fireflyParticles.AddVelocity = new Vec3f(0.5f, 0.25f / 2, 0.5f);
        fireflyParticles.VertexFlags = (byte)255;
        fireflyParticles.AddPos.Set(1, 1, 1);
        fireflyParticles.AddQuantity = 8;
        fireflyParticles.addLifeLength = 1;
        fireflyParticles.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.CLAMPEDPOSITIVESINUS, GameMath.PI);
        fireflyParticles.RandomVelocityChange = true;
        fireflyLocationNoise = new ClampedSimplexNoise(new double[] { 1 }, new double[] { 5 }, capi.World.Rand.Next());
        fireflyrateNoise = new ClampedSimplexNoise(new double[] { 1 }, new double[] { 5 }, capi.World.Rand.Next());
    }

    ClimateCondition climate = new ClimateCondition();
    private void OnSlowTick(float dt)
    {
        climate = capi.World.BlockAccessor.GetClimateAt(capi.World.Player.Entity.Pos.AsBlockPos, EnumGetClimateMode.NowValues);
        if (climate == null) climate = new ClimateCondition();

        spawnParticles = capi.Settings.Bool["ambientParticles"];
    }

    bool spawnParticles = false;

    Vec3d position = new Vec3d();
    BlockPos blockPos = new BlockPos();
    private bool AsyncParticleSpawnTick(float dt, IAsyncParticleManager manager)
    {
        if (!spawnParticles) return true;
        if (ShouldSpawnAmbientParticles != null && !ShouldSpawnAmbientParticles()) return true;

        int particleLevel = capi.Settings.Int["particleLevel"];

        var world = capi.World;
        var eplr = world.Player.Entity;

        ClimateCondition conds = world.BlockAccessor.GetClimateAt(blockPos.Set((int)eplr.Pos.X, (int)eplr.Pos.Y, (int)eplr.Pos.Z), EnumGetClimateMode.NowValues);

        float tries = 0.5f * particleLevel;
        while (tries-- > 0)
        {
            double offX = world.Rand.NextDouble() * 32 - 16;
            double offY = world.Rand.NextDouble() * 20 - 10;
            double offZ = world.Rand.NextDouble() * 32 - 16;

            position.Set(eplr.Pos.X, eplr.Pos.Y, eplr.Pos.Z).Add(offX, offY, offZ);
            blockPos.Set(position);

            if (!world.BlockAccessor.IsValidPos(blockPos)) continue;

            double chance = 0.05 + Math.Max(0, world.Calendar.DayLightStrength) * 0.4;

            if (conds.Rainfall <= 0.01 && GlobalConstants.CurrentWindSpeedClient.X < 0.2f && world.Rand.NextDouble() < chance && climate.Temperature >= 14 && climate.WorldgenRainfall >= 0.4f && blockPos.Y > world.SeaLevel && manager.BlockAccess.GetBlock(blockPos).Id == 0)
            {
                var cs = world.BlockAccessor.ChunkSize;
                IMapChunk mapchunk = manager.BlockAccess.GetMapChunk(blockPos.X / cs, blockPos.Z / cs);
                if (mapchunk != null && blockPos.Y > mapchunk.RainHeightMap[(blockPos.Z % cs) * cs + (blockPos.X % cs)])
                {
                    summerAirParticles.MinPos.Set(position);
                    summerAirParticles.RandomVelocityChange = true;
                    manager.Spawn(summerAirParticles);
                }

                continue;
            }


            Block block = manager.BlockAccess.GetBlock(blockPos, BlockLayersAccess.Fluid);
            if (block.IsLiquid() && block.LiquidLevel >= 7)
            {
                liquidParticles.MinVelocity = new Vec3f(
                    (float)world.Rand.NextDouble() / 16 - 0.125f / 4f,
                    (float)world.Rand.NextDouble() / 16 - 0.125f / 4f,
                    (float)world.Rand.NextDouble() / 16 - 0.125f / 4f
                );

                liquidParticles.MinPos = position;

                if (world.Rand.Next(3) > 0)
                {
                    liquidParticles.RandomVelocityChange = false;
                    liquidParticles.Color = ColorUtil.HsvToRgba(110, 40 + world.Rand.Next(50), 200 + world.Rand.Next(30), 50 + world.Rand.Next(40));
                }
                else
                {
                    liquidParticles.RandomVelocityChange = true;
                    liquidParticles.Color = ColorUtil.HsvToRgba(110, 20 + world.Rand.Next(25), 100 + world.Rand.Next(15), 100 + world.Rand.Next(40));
                }

                manager.Spawn(liquidParticles);
                continue;
            }
        }



        if (conds.Rainfall < 0.15 && conds.Temperature > 5)
        {
            // 0..2f
            double noise = (fireflyrateNoise.Noise(world.Calendar.TotalDays / 3.0, 0) - 0.4f) * 4;
            float f = Math.Max(0, (float)(noise - Math.Abs(GlobalConstants.CurrentWindSpeedClient.X) * 2));

            int itries = GameMath.RoundRandom(world.Rand, f * 0.01f * particleLevel);

            while (itries-- > 0)
            {
                double offX = world.Rand.NextDouble() * 80 - 40;
                double offY = world.Rand.NextDouble() * 80 - 40;
                double offZ = world.Rand.NextDouble() * 80 - 40;

                position.Set(eplr.Pos.X, eplr.Pos.Y, eplr.Pos.Z).Add(offX, offY, offZ);
                blockPos.Set(position);

                if (!world.BlockAccessor.IsValidPos(blockPos)) continue;

                double posrnd = Math.Max(0, fireflyLocationNoise.Noise(blockPos.X / 60.0, blockPos.Z / 60.0, world.Calendar.TotalDays / 5 - 0.8f) - 0.5f) * 2;
                double chance = Math.Max(0, 1 - world.Calendar.DayLightStrength * 2) * posrnd;
                int prevY = blockPos.Y;
                blockPos.Y = manager.BlockAccess.GetTerrainMapheightAt(blockPos);
                Block block = manager.BlockAccess.GetBlock(blockPos);

                if (world.Rand.NextDouble() <= chance && climate.Temperature >= 8 && climate.Temperature <= 29 && climate.WorldgenRainfall >= 0.4f && block.Fertility > 30 && blockPos.Y > world.SeaLevel)
                {
                    blockPos.Y += world.Rand.Next(4);
                    position.Y += blockPos.Y - prevY;

                    block = manager.BlockAccess.GetBlock(blockPos);
                    Cuboidf[] collboxes = block.GetCollisionBoxes(manager.BlockAccess, blockPos);
                    if (collboxes == null || collboxes.Length == 0)
                    {
                        fireflyParticles.AddVelocity.X = 0.5f + GlobalConstants.CurrentWindSpeedClient.X;
                        fireflyParticles.MinPos = position;

                        manager.Spawn(fireflyParticles);
                        continue;
                    }
                }
            }
        }

        return true;
    }
}
