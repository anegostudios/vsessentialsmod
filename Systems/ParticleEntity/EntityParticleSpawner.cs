using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common.CommandAbbr;
using System.Collections.Generic;
using Vintagestory.API.Config;
using Vintagestory.API.Common.Entities;
using System.Text;

namespace Vintagestory.GameContent
{
    public class EntityParticleSpawner : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        ICoreClientAPI capi;
        Random rand = new Random();


        NormalizedSimplexNoise grasshopperNoise;
        NormalizedSimplexNoise cicadaNoise;
        NormalizedSimplexNoise matingGnatsSwarmNoise;
        NormalizedSimplexNoise coquiNoise;
        NormalizedSimplexNoise waterstriderNoise;

        Queue<Action> SimTickExecQueue = new Queue<Action>();

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;

            grasshopperNoise = NormalizedSimplexNoise.FromDefaultOctaves(4, 0.01, 0.9, api.World.Seed*100);
            coquiNoise = NormalizedSimplexNoise.FromDefaultOctaves(4, 0.0025, 0.9, api.World.Seed * 101);
            waterstriderNoise = NormalizedSimplexNoise.FromDefaultOctaves(4, 0.01, 0.9, api.World.Seed * 102);
            matingGnatsSwarmNoise = NormalizedSimplexNoise.FromDefaultOctaves(4, 0.01, 0.9, api.World.Seed * 103);
            cicadaNoise = NormalizedSimplexNoise.FromDefaultOctaves(4, 0.01, 0.9, api.World.Seed * 104);

            sys = api.ModLoader.GetModSystem<EntityParticleSystem>();
            sys.OnSimTick += Sys_OnSimTick;

            api.ChatCommands
                .GetOrCreate("debug")
                .BeginSub("eps")
                    .BeginSub("testspawn")
                        .WithArgs(api.ChatCommands.Parsers.WordRange("type", "gh", "ws", "coq", "mg", "cic", "fis"))
                        .HandleWith(handleSpawn)
                    .EndSub()
                    .BeginSub("count")
                        .HandleWith(handleCount)
                    .EndSub()
                    .BeginSub("clear")
                        .HandleWith(handleClear)
                    .EndSub()
                    .BeginSub("testnoise")
                        .HandleWith(handleTestnoise)
                        .WithArgs(api.ChatCommands.Parsers.OptionalWordRange("clear", "clear"))
                    .EndSub()

                .EndSub()
            ;
        }

        private TextCommandResult handleCount(TextCommandCallingArgs args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var val in sys.Count.Dict)
            {
                sb.AppendLine(string.Format("{0}: {1}", val.Key, val.Value));
            }

            if (sb.Length == 0) return TextCommandResult.Success("No entityparticle alive");

            return TextCommandResult.Success(sb.ToString());
        }

        private TextCommandResult handleTestnoise(TextCommandCallingArgs args)
        {
            var pos = capi.World.Player.Entity.Pos.XYZ.AsBlockPos;
            var block = capi.World.GetBlock(new AssetLocation("creativeblock-35"));

            bool clear = args.Parsers[0].IsMissing ? false : true;

            for (int dx = -200; dx <= 200; dx++)
            {
                for (int dz = -200; dz <= 200; dz++)
                {
                    var noise = matingGnatsSwarmNoise.Noise(pos.X+dx, pos.Z+dz);
                    if (clear || noise < 0.65)
                    {
                        capi.World.BlockAccessor.SetBlock(0, new BlockPos(pos.X + dx, 160, pos.Z + dz));
                    }
                    else
                    {
                        capi.World.BlockAccessor.SetBlock(block.Id, new BlockPos(pos.X + dx, 160, pos.Z + dz));
                    }
                }
            }
            return TextCommandResult.Success("testnoise");
        }

        private TextCommandResult handleClear(TextCommandCallingArgs args)
        {
            sys.Clear();
            return TextCommandResult.Success("cleared");
        }

        private TextCommandResult handleSpawn(TextCommandCallingArgs args)
        {
            string type = args[0] as string;

            SimTickExecQueue.Enqueue(() => {
                var pos = capi.World.Player.Entity.Pos;
                var climate = capi.World.BlockAccessor.GetClimateAt(pos.AsBlockPos);
                float cohesion = (float)GameMath.Max(rand.NextDouble() * 1.1, 0.25);

                Vec3d centerPos = pos.XYZ.AddCopy(0, 1.5f, 0);

                for (int i = 0; i < 20; i++)
                {
                    double x = pos.X + (rand.NextDouble() - 0.5) * 10;
                    double z = pos.Z + (rand.NextDouble() - 0.5) * 10;
                    double y = capi.World.BlockAccessor.GetRainMapHeightAt((int)x, (int)z);

                    if (type == "gh")
                    {
                        var gh = new EntityParticleGrasshopper(capi, x, y + 1 + rand.NextDouble() * 0.25, z);
                        sys.SpawnParticle(gh);
                    }
                    if (type == "coq")
                    {
                        var gh = new EntityParticleCoqui(capi, x, y + 1 + rand.NextDouble() * 0.25, z);
                        sys.SpawnParticle(gh);
                    }
                    if (type == "ws")
                    {
                        var block = capi.World.BlockAccessor.GetBlock((int)x, (int)y, (int)z, BlockLayersAccess.Fluid);
                        if (block.LiquidCode == "water" && block.PushVector == null)
                        {
                            var ws = new EntityParticleWaterStrider(capi, x, y+block.LiquidLevel/8f, z);
                            sys.SpawnParticle(ws);
                        }
                    }
                    if (type == "fis")
                    {
                        x = pos.X + (rand.NextDouble() - 0.5) * 2;
                        z = pos.Z + (rand.NextDouble() - 0.5) * 2;
                        var block = capi.World.BlockAccessor.GetBlock((int)x, (int)y, (int)z, BlockLayersAccess.Fluid);
                        if (block.LiquidCode == "saltwater" && block.PushVector == null)
                        {
                            var ws = new EntityParticleFish(capi, x, y - block.LiquidLevel, z);
                            sys.SpawnParticle(ws);
                        }
                    }

                    if (type == "mg")
                    {
                        sys.SpawnParticle(new EntityParticleMatingGnats(capi, cohesion, centerPos.X, centerPos.Y, centerPos.Z));
                    }

                    if (type == "cic")
                    {
                        spawnCicadas(pos, climate);
                    }
                }
            });

            return TextCommandResult.Success(type + " spawned.");
        }

        EntityParticleSystem sys;



        float accum = 0;
        private void Sys_OnSimTick(float dt)
        {
            accum += dt;

            while (SimTickExecQueue.Count > 0)
            {
                SimTickExecQueue.Dequeue()();
            }

            if (accum > 0.5f)
            {
                accum = 0;
                var pos = capi.World.Player.Entity.Pos;
                var climate = capi.World.BlockAccessor.GetClimateAt(pos.AsBlockPos);

                spawnGrasshoppers(pos, climate);
                spawnWaterStriders(pos, climate);
                spawnCoquis(pos, climate);
                spawnMatingGnatsSwarm(pos, climate);
                spawnCicadas(pos, climate);
                //spawnFish(pos, climate);
            }
        }

        private void spawnWaterStriders(EntityPos pos, ClimateCondition climate)
        {
            if (climate.Temperature > 35 || climate.Temperature < 19 || climate.Rainfall > 0.1f || climate.WorldgenRainfall < 0.5) return;

            var noise = waterstriderNoise.Noise(pos.X, pos.Z);
            if (noise < 0.5) return;


            if (sys.Count["waterStrider"] > 50) return;

            for (int i = 0; i < 100; i++)
            {
                double x = pos.X + (rand.NextDouble() - 0.5) * 60;
                double z = pos.Z + (rand.NextDouble() - 0.5) * 60;
                double y = capi.World.BlockAccessor.GetRainMapHeightAt((int)x, (int)z);

                if (pos.HorDistanceTo(new Vec3d(x, y, z)) < 3) continue;

                var block = capi.World.BlockAccessor.GetBlock((int)x, (int)y, (int)z, BlockLayersAccess.Fluid);
                var belowblock = capi.World.BlockAccessor.GetBlock((int)x, (int)y - 1, (int)z);
                var aboveblock = capi.World.BlockAccessor.GetBlock((int)x, (int)y + 1, (int)z);
                if (block.LiquidCode == "water" && block.PushVector == null && belowblock.Replaceable < 6000 && aboveblock.Id == 0)
                {
                    var ws = new EntityParticleWaterStrider(capi, x, y + block.LiquidLevel / 8f, z);
                    sys.SpawnParticle(ws);
                }
            }
        }

        private void spawnFish(EntityPos pos, ClimateCondition climate)
        {
            if (climate.Temperature > 40 || climate.Temperature < 0) return;

            // var noise = fishNoise.Noise(pos.X, pos.Z);
            // if (noise < 0.5) return;
            var bpos = new BlockPos();

            if (sys.Count["fish"] > 100) return;

            for (int i = 0; i < 100; i++)
            {
                double x = pos.X + (rand.NextDouble() - 0.5) * 40;
                double z = pos.Z + (rand.NextDouble() - 0.5) * 40;
                bpos.Set((int)x, 0, (int)z);
                var y = capi.World.BlockAccessor.GetTerrainMapheightAt(bpos);
                bpos.Y = Math.Min(capi.World.SeaLevel - 1, y + 2);
                if (bpos.HorDistanceSqTo(pos.X, pos.Z) < 3) continue;

                var block = capi.World.BlockAccessor.GetBlock(bpos, BlockLayersAccess.Fluid);
                var belowBlock = capi.World.BlockAccessor.GetBlock(bpos.DownCopy());
                if (block.LiquidCode == "saltwater" && belowBlock.Code.PathStartsWith("coral"))
                {
                    var fishAmount = 10 + rand.Next(10);
                    for (var j = 0; j < fishAmount; j++)
                    {
                        var offX = rand.NextDouble() - 0.5;
                        var offZ = rand.NextDouble() - 0.5;
                        var ws = new EntityParticleFish(capi, x + offX, bpos.Y, z + offZ);
                        sys.SpawnParticle(ws);
                    }
                }
            }
        }

        void spawnGrasshoppers(EntityPos pos, ClimateCondition climate)
        {
            if (climate.Temperature >= 30 || climate.Temperature < 18 || climate.Rainfall > 0.1f || climate.WorldgenRainfall < 0.5) return;

            var noise = grasshopperNoise.Noise(pos.X, pos.Z);
            if (noise < 0.7) return;

            if (sys.Count["grassHopper"] > 40) return;

            for (int i = 0; i < 100; i++)
            {
                double x = pos.X + (rand.NextDouble() - 0.5) * 60;
                double z = pos.Z + (rand.NextDouble() - 0.5) * 60;
                double y = capi.World.BlockAccessor.GetRainMapHeightAt((int)x, (int)z);

                if (pos.HorDistanceTo(new Vec3d(x, y, z)) < 3) continue;

                var block = capi.World.BlockAccessor.GetBlock((int)x, (int)y + 1, (int)z);
                var belowblock = capi.World.BlockAccessor.GetBlock((int)x, (int)y, (int)z);
                if (block.BlockMaterial == EnumBlockMaterial.Plant && belowblock.BlockMaterial == EnumBlockMaterial.Soil)
                {
                    var gh = new EntityParticleGrasshopper(capi, x, y + 1.01 + rand.NextDouble() * 0.25, z);
                    sys.SpawnParticle(gh);
                }
            }
        }

        void spawnCicadas(EntityPos pos, ClimateCondition climate)
        {
            if (climate.Temperature > 33 || climate.Temperature < 22 || climate.WorldGenTemperature < 10 || climate.Rainfall > 0.1f || climate.WorldgenRainfall < 0.5) return;

            var noise = cicadaNoise.Noise(pos.X, pos.Z, (int)capi.World.Calendar.Year); // Change the location of the cicadas every year
            if (noise < 0.7) return;
            if (sys.Count["cicada"] > 40) return;

            for (int i = 0; i < 400; i++)
            {
                double x = pos.X + (rand.NextDouble() - 0.5) * 50;
                double z = pos.Z + (rand.NextDouble() - 0.5) * 50;
                double y = pos.Y + (rand.NextDouble() - 0.5) * 10;

                if (pos.HorDistanceTo(new Vec3d(x, y, z)) < 2) continue;

                var block = capi.World.BlockAccessor.GetBlock((int)x, (int)y, (int)z);
                var blockbelow = capi.World.BlockAccessor.GetBlock((int)x, (int)y-1, (int)z);
                if (block.BlockMaterial == EnumBlockMaterial.Wood && block.Variant["type"] == "grown" && blockbelow.Id == block.Id)
                {
                    var face = BlockFacing.HORIZONTALS[rand.Next(4)].Normalf;
                    double sx = (int)x + 0.5f + face.X * 0.52f;
                    double sy = y + 0.1 + rand.NextDouble() * 0.8;
                    double sz = (int)z + 0.5f + face.Z * 0.52f;
                    var sblock = capi.World.BlockAccessor.GetBlock((int)sx, (int)sy, (int)sz);
                    if (sblock.Replaceable >= 6000)
                    {
                        var gh = new EntityParticleCicada(capi, sx, sy, sz);
                        sys.SpawnParticle(gh);
                    } else
                    {
                        sx += face.X;
                        sz += face.Z;
                        if (capi.World.BlockAccessor.GetBlock((int)sx, (int)sy, (int)sz).Replaceable >= 6000)
                        {
                            var gh = new EntityParticleCicada(capi, sx, sy, sz);
                            sys.SpawnParticle(gh);
                        }
                    }
                }
            }
        }

        void spawnCoquis(EntityPos pos, ClimateCondition climate)
        {
            if (climate.WorldGenTemperature < 30 || climate.WorldgenRainfall < 0.7) return;

            var noise = coquiNoise.Noise(pos.X, pos.Z);
            if (noise < 0.8) return;

            if (sys.Count["coqui"] > 60) return;

            for (int i = 0; i < 100; i++)
            {
                double x = pos.X + (rand.NextDouble() - 0.5) * 60;
                double z = pos.Z + (rand.NextDouble() - 0.5) * 60;
                double y = capi.World.BlockAccessor.GetRainMapHeightAt((int)x, (int)z);

                if (pos.HorDistanceTo(new Vec3d(x, y, z)) < 3) continue;

                var block = capi.World.BlockAccessor.GetBlock((int)x, (int)y + 1, (int)z);
                var belowblock = capi.World.BlockAccessor.GetBlock((int)x, (int)y, (int)z);
                if (block.BlockMaterial == EnumBlockMaterial.Plant && belowblock.BlockMaterial == EnumBlockMaterial.Soil)
                {
                    var gh = new EntityParticleCoqui(capi, x, y + 1.01 + rand.NextDouble() * 0.25, z);
                    sys.SpawnParticle(gh);
                }
            }
        }

        void spawnMatingGnatsSwarm(EntityPos pos, ClimateCondition climate)
        {
            if (climate.Temperature < 17 || climate.Rainfall > 0.1f || climate.WorldgenRainfall < 0.6 || GlobalConstants.CurrentWindSpeedClient.Length() > 0.35f) return;

            var noise = matingGnatsSwarmNoise.Noise(pos.X, pos.Z);
            if (noise < 0.5) return;


            if (sys.Count["matinggnats"] > 200) return;

            int spawns = 0;
            for (int i = 0; i < 100 && spawns < 6; i++)
            {
                double x = pos.X + (rand.NextDouble() - 0.5) * 24;
                double z = pos.Z + (rand.NextDouble() - 0.5) * 24;
                double y = capi.World.BlockAccessor.GetRainMapHeightAt((int)x, (int)z);

                if (pos.HorDistanceTo(new Vec3d(x, y, z)) < 2) continue;

                var ab2block = capi.World.BlockAccessor.GetBlock((int)x, (int)y + 2, (int)z);
                var abblock = capi.World.BlockAccessor.GetBlock((int)x, (int)y + 1, (int)z);
                var block = capi.World.BlockAccessor.GetBlock((int)x, (int)y, (int)z, BlockLayersAccess.Fluid);
                var belowf2block = capi.World.BlockAccessor.GetBlock((int)x, (int)y - 2, (int)z, BlockLayersAccess.Fluid);

                if (block.LiquidCode == "water" && abblock.Id==0 && ab2block.Id == 0 && belowf2block.Id == 0)
                {
                    float cohesion = (float)GameMath.Max(rand.NextDouble() * 1.1, 0.25)/2;
                    int cnt = 10 + rand.Next(21);
                    for (int j = 0; j < cnt; j++)
                    {
                        sys.SpawnParticle(new EntityParticleMatingGnats(capi, cohesion, (int)x + 0.5, y + 1.5 + rand.NextDouble() * 0.5, (int)z + 0.5));
                    }

                    spawns++;
                }
            }
        }
    }
}
