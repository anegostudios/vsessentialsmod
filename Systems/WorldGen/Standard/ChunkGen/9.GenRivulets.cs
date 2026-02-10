using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.ServerMods
{
    public class GenRivulets : ModStdWorldGen
    {
        ICoreServerAPI api;
        LCGRandom rnd;
        IWorldGenBlockAccessor blockAccessor;
        int regionsize;
        int chunkMapSizeY;
        BlockPos chunkBase = new BlockPos(API.Config.Dimensions.NormalWorld);
        BlockPos chunkend = new BlockPos(API.Config.Dimensions.NormalWorld);
        List<Cuboidi> structuresIntersectingChunk = new List<Cuboidi>();

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override double ExecuteOrder()
        {
            return 0.9;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;

            if (TerraGenConfig.DoDecorationPass)
            {
                api.Event.InitWorldGenerator(initWorldGen, "standard");
                api.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.Vegetation, "standard");
                api.Event.GetWorldgenBlockAccessor(OnWorldGenBlockAccessor);
            }
        }


        private void OnWorldGenBlockAccessor(IChunkProviderThread chunkProvider)
        {
            blockAccessor = chunkProvider.GetBlockAccessor(true);
            regionsize = blockAccessor.RegionSize;
        }

        private void initWorldGen()
        {
            LoadGlobalConfig(api);
            rnd = new LCGRandom(api.WorldManager.Seed);
            chunkMapSizeY = api.WorldManager.MapSizeY / chunksize;
        }

        private void OnChunkColumnGen(IChunkColumnGenerateRequest request)
        {
            var chunks = request.Chunks;
            int chunkX = request.ChunkX;
            int chunkZ = request.ChunkZ;

            blockAccessor.BeginColumn();
            var mapChunk = chunks[0].MapChunk;
            IntDataMap2D climateMap = mapChunk.MapRegion.ClimateMap;
            int regionChunkSize = api.WorldManager.RegionSize / chunksize;
            float fac = (float)climateMap.InnerSize / regionChunkSize;
            int rlX = chunkX % regionChunkSize;
            int rlZ = chunkZ % regionChunkSize;

            int climateUpLeft = climateMap.GetUnpaddedInt((int)(rlX * fac), (int)(rlZ * fac));
            int climateUpRight = climateMap.GetUnpaddedInt((int)(rlX * fac + fac), (int)(rlZ * fac));
            int climateBotLeft = climateMap.GetUnpaddedInt((int)(rlX * fac), (int)(rlZ * fac + fac));
            int climateBotRight = climateMap.GetUnpaddedInt((int)(rlX * fac + fac), (int)(rlZ * fac + fac));

            int climateMid = GameMath.BiLerpRgbColor(0.5f, 0.5f, climateUpLeft, climateUpRight, climateBotLeft, climateBotRight);

            structuresIntersectingChunk.Clear();
            api.World.BlockAccessor.WalkStructures(chunkBase.Set(chunkX * chunksize, 0, chunkZ * chunksize), chunkend.Set(chunkX * chunksize + chunksize, chunkMapSizeY * chunksize, chunkZ * chunksize + chunksize), (struc) =>
            {
                if (struc.SuppressRivulets)
                {
                    structuresIntersectingChunk.Add(struc.Location.Clone().GrowBy(1, 1, 1));
                }
            });

            // 16-23 bits = Red = temperature
            // 8-15 bits = Green = rain
            // 0-7 bits = Blue = humidity
            int rain = (climateMid >> 8) & 0xff;
            int humidity = climateMid & 0xff;
            int temp = (climateMid >> 16) & 0xff;

            int geoActivity = getGeologicActivity(chunkX * chunksize + chunksize / 2, chunkZ * chunksize + chunksize / 2);
            float geoActivityYThreshold = getGeologicActivity(chunkX * chunksize + chunksize / 2, chunkZ * chunksize + chunksize / 2) / 2f * api.World.BlockAccessor.MapSizeY / 256f;

            int quantityWaterRivulets = 2 * ((int)(160 * (rain + humidity) / 255f) * (api.WorldManager.MapSizeY / chunksize) - Math.Max(0, 100 - temp));
            int quantityWaterRivuletMountainSide = GameMath.RoundRandom(rnd, quantityWaterRivulets / 400f);
            int quantityLavaRivers = (int)(500 * geoActivity/255f * (api.WorldManager.MapSizeY / chunksize));

            var chance = gcfg.waterRapidsChance * 2;

            float sealeveltemp = Climate.GetScaledAdjustedTemperatureFloat(temp, 0);
            rnd.InitPositionSeed(chunkX, chunkZ);
            if (sealeveltemp >= -15)
            {
                while (quantityWaterRivulets-- > 0)
                {
                    tryGenRivulet(chunks, chunkX, chunkZ, geoActivityYThreshold, false);
                }

                while (quantityWaterRivuletMountainSide-- > 0)
                {
                    int blockid = rnd.NextDouble() < chance ? gcfg.rivuletRapidWaterBlockId : gcfg.rivuletWaterBlockId;
                    tryGenMountainSideRivers(chunks, chunkX, chunkZ, blockid);
                }
            }

            while (quantityLavaRivers-- > 0)
            {
                tryGenRivulet(chunks, chunkX, chunkZ, geoActivityYThreshold + 10, true);
            }
        }

        private void tryGenRivulet(IServerChunk[] chunks, int chunkX, int chunkZ, float geoActivityYThreshold, bool lava)
        {
            var mapChunk = chunks[0].MapChunk;
            int fx, fy, fz;
            int surfaceY = (int)(TerraGenConfig.seaLevel * 1.1f);
            int aboveSurfaceHeight = api.WorldManager.MapSizeY - surfaceY;

            int dx = 1 + rnd.NextInt(chunksize - 2);
            int y = Math.Min(1 + rnd.NextInt(surfaceY) + rnd.NextInt(aboveSurfaceHeight) * rnd.NextInt(aboveSurfaceHeight), api.WorldManager.MapSizeY - 2);
            int dz = 1 + rnd.NextInt(chunksize - 2);

            ushort hereSurfaceY = mapChunk.WorldGenTerrainHeightMap[dz * chunksize + dx];
            if (y > hereSurfaceY && rnd.NextInt(2) == 0) return; // Half as common overground

            // Water only above y-threshold, Lava only below y-threshold
            if (y < geoActivityYThreshold && !lava || y > geoActivityYThreshold && lava) return;

            int quantitySolid = 0;
            int quantityAir = 0;
            for (int i = 0; i < BlockFacing.NumberOfFaces; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                fx = dx + facing.Normali.X;
                fy = y + facing.Normali.Y;
                fz = dz + facing.Normali.Z;

                Block block = api.World.Blocks[
                    chunks[fy / chunksize].Data.GetBlockIdUnsafe((chunksize * (fy % chunksize) + fz) * chunksize + fx)
                ];

                bool solid = block.BlockMaterial == EnumBlockMaterial.Stone;
                quantitySolid += solid ? 1 : 0;
                quantityAir += (block.BlockMaterial == EnumBlockMaterial.Air) ? 1 : 0;

                if (!solid)
                {
                    if (facing == BlockFacing.UP) quantitySolid = 0;   // We don't place rivulets on flat ground!
                    else if (facing == BlockFacing.DOWN)   // Nor in 1-block thick ceilings
                    {
                        fy = y + 1;
                        block = api.World.Blocks[chunks[fy / chunksize].Data.GetBlockIdUnsafe((chunksize * (fy % chunksize) + fz) * chunksize + fx)];
                        if (block.BlockMaterial != EnumBlockMaterial.Stone) quantitySolid = 0;
                    }
                }
            }

            if (quantitySolid != 5 || quantityAir != 1) return;

            BlockPos pos = new BlockPos(chunkX * chunksize + dx, y, chunkZ * chunksize + dz);
            for (int i = 0; i < structuresIntersectingChunk.Count; i++)
            {
                if (structuresIntersectingChunk[i].Contains(pos)) return;
            }
            if (GetIntersectingStructure(pos.X, pos.Z, RivuletsHashCode) != null) return;

            var chunk = chunks[y / chunksize];
            var index = (chunksize * (y % chunksize) + dz) * chunksize + dx;
            Block existing = api.World.GetBlock(chunk.Data.GetBlockId(index, BlockLayersAccess.Solid));
            if (existing.EntityClass != null)
            {
                chunk.RemoveBlockEntity(pos);
            }
            chunk.Data.SetBlockAir(index);
            chunk.Data.SetFluid(index, y < geoActivityYThreshold ? gcfg.lavaBlockId : gcfg.waterBlockId);

            blockAccessor.ScheduleBlockUpdate(pos);
        }


        private void tryGenMountainSideRivers(IServerChunk[] chunks, int chunkX, int chunkZ, int liquidBlockID)
        {
            var mapChunk = chunks[0].MapChunk;
            int fx, fy, fz;
            float minY = TerraGenConfig.seaLevel * 1.1f + 10;

            // pick a spot so we have enough blocks around to check for its flow direction in the same chunk -+4
            int dx = 4 + rnd.NextInt(chunksize - 8);
            int dz = 4 + rnd.NextInt(chunksize - 8);
            int y = mapChunk.WorldGenTerrainHeightMap[dz * chunksize + dx] - 2;

            if (y < minY) return;
            if (liquidBlockID == gcfg.rivuletRapidWaterBlockId && y - TerraGenConfig.seaLevel > 20) return; // Normal rivulets only at lower heights

            Vec3i dir = null;
            int len = 0;
            for (int i = 0; i < BlockFacing.HORIZONTALS.Length; i++)
            {
                BlockFacing facing = BlockFacing.HORIZONTALS[i];

                // All direct neighbours need to be solid
                fx = dx + facing.Normali.X;
                fy = y;
                fz = dz + facing.Normali.Z;
                Block block = api.World.Blocks[chunks[fy / chunksize].Data.GetBlockIdUnsafe((chunksize * (fy % chunksize) + fz) * chunksize + fx)];
                if (!block.SideSolid.All) return;

                // In one direction there needs to be a path towards the surface
                if (dir != null) continue;
                for (len = 1; len <= 4; len++)
                {
                    fx = dx + facing.Normali.X * len;
                    fy = y;
                    fz = dz + facing.Normali.Z * len;

                    block = api.World.Blocks[chunks[fy / chunksize].Data.GetBlockIdUnsafe((chunksize * (fy % chunksize) + fz) * chunksize + fx)];
                    bool belowSolid = api.World.Blocks[chunks[(fy - 1) / chunksize].Data.GetBlockIdUnsafe((chunksize * ((fy - 1) % chunksize) + fz) * chunksize + fx)].SideSolid.All;
                    bool solid = block.SideSolid.All;
                    if ((len < 2 && !solid) || !belowSolid) break;

                    if (len>=2 && block.BlockMaterial == EnumBlockMaterial.Air)
                    {
                        dir = facing.Normali;
                        break;
                    }
                }
            }

            if (dir == null) return;

            for (int k = 0; k <= len; k++)
            {
                BlockPos pos = new BlockPos(chunkX * chunksize + dx + k*dir.X, y, chunkZ * chunksize + dz + k * dir.Z);

                for (int i = 0; i < structuresIntersectingChunk.Count; i++)
                {
                    if (structuresIntersectingChunk[i].Contains(pos)) return;
                }
                if (GetIntersectingStructure(pos.X, pos.Z, RivuletsHashCode) != null) return;
            }

            for (int k = 0; k <= len; k++)
            {
                BlockPos pos = new BlockPos(chunkX * chunksize + dx + k * dir.X, y, chunkZ * chunksize + dz + k * dir.Z);

                blockAccessor.SetBlock(0, pos);
                if (k == 0)
                {
                    blockAccessor.SetBlock(liquidBlockID, pos, BlockLayersAccess.Fluid);
                    blockAccessor.ScheduleBlockUpdate(pos);
                }
            }
        }

        private int getGeologicActivity(int posx, int posz)
        {
            var climateMap = blockAccessor.GetMapRegion(posx / regionsize, posz / regionsize)?.ClimateMap;
            if (climateMap == null) return 0;
            int regionChunkSize = regionsize / chunksize;
            float fac = (float)climateMap.InnerSize / regionChunkSize;
            int rlX = (posx / chunksize) % regionChunkSize;
            int rlZ = (posz / chunksize) % regionChunkSize;

            return climateMap.GetUnpaddedInt((int)(rlX * fac), (int)(rlZ * fac)) & 0xff;
        }
    }
}
