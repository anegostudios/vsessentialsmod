using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

#nullable disable

namespace Vintagestory.ServerMods
{
    public class SpawnOppurtunity
    {
        public EntityProperties ForType;
        public Vec3d Pos;
    }

    public class GenCreatures : ModStdWorldGen
    {
        protected ICoreServerAPI api;
        protected Random rnd;
        protected int worldheight;
        protected IWorldGenBlockAccessor wgenBlockAccessor;
        protected Dictionary<EntityProperties, EntityProperties[]> entityTypeGroups = new Dictionary<EntityProperties, EntityProperties[]>();

        public Dictionary<string, MapLayerBase> animalMapGens = new Dictionary<string, MapLayerBase>();
        protected int noiseSizeDensityMap;
        protected int regionSize;


        protected int climateUpLeft;
        protected int climateUpRight;
        protected int climateBotLeft;
        protected int climateBotRight;

        protected int forestUpLeft;
        protected int forestUpRight;
        protected int forestBotLeft;
        protected int forestBotRight;

        protected int shrubsUpLeft;
        protected int shrubsUpRight;
        protected int shrubsBotLeft;
        protected int shrubsBotRight;
        protected List<SpawnOppurtunity> spawnPositions = new List<SpawnOppurtunity>();


        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
        public override double ExecuteOrder() => 0.1;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;

            if (TerraGenConfig.DoDecorationPass)
            {
                api.Event.InitWorldGenerator(initWorldGen, "standard");
                api.Event.GetWorldgenBlockAccessor(OnWorldGenBlockAccessor);

                api.Event.OnTrySpawnEntity += Event_OnTrySpawnEntity;
            }
        }

        protected void OnWorldGenBlockAccessor(IChunkProviderThread chunkProvider)
        {
            wgenBlockAccessor = chunkProvider.GetBlockAccessor(true);
        }


        protected void OnMapRegionGen(IMapRegion mapRegion, int regionX, int regionZ, ITreeAttribute chunkGenParams = null)
        {
            int noiseSize = api.WorldManager.RegionSize / TerraGenConfig.blockPatchesMapScale;

            foreach (var val in animalMapGens)
            {
                var map = ByteDataMap2D.CreateEmpty();
                map.Size = noiseSize + 1;
                map.BottomRightPadding = 1;
                var data = val.Value.GenLayer(regionX * noiseSize, regionZ * noiseSize, noiseSize + 1, noiseSize + 1);
                byte[] bytes = new byte[data.Length];
                for (int i = 0; i < data.Length; i++) bytes[i] = (byte)data[i];
                map.Data = bytes;

                mapRegion.AnimalSpawnMaps[val.Key] = map;
            }
        }


        protected void initWorldGen()
        {
            LoadGlobalConfig(api);
            rnd = new Random(api.WorldManager.Seed - 18722);
            worldheight = api.WorldManager.MapSizeY;

            Dictionary<AssetLocation, EntityProperties> entityTypesByCode = new Dictionary<AssetLocation, EntityProperties>();

            for (int i = 0; i < api.World.EntityTypes.Count; i++)
            {
                entityTypesByCode[api.World.EntityTypes[i].Code] = api.World.EntityTypes[i];
            }

            Dictionary<AssetLocation, Block[]> searchCache = new Dictionary<AssetLocation, Block[]>();

            LCGRandom lcgrnd = new LCGRandom();
            lcgrnd.SetWorldSeed(api.WorldManager.Seed); // Must be the same seed as in EntitySpawner.cs

            for (int i = 0; i < api.World.EntityTypes.Count; i++)
            {
                EntityProperties type = api.World.EntityTypes[i];
                WorldGenSpawnConditions conds = type.Server?.SpawnConditions?.Worldgen;
                if (conds == null) continue;

                List<EntityProperties> grouptypes = new List<EntityProperties>();
                grouptypes.Add(type);

                conds.Initialise(lcgrnd, api.World, type.Code.ToShortString(), searchCache);

                AssetLocation[] companions = conds.Companions;
                if (companions == null) continue;

                for (int j = 0; j < companions.Length; j++)
                {
                    if (entityTypesByCode.TryGetValue(companions[j], out EntityProperties cptype))
                    {
                        grouptypes.Add(cptype);
                    }
                }

                entityTypeGroups[type] = grouptypes.ToArray();
            }

            loadAnimalMaps();

            api.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.PreDone, "standard");
            api.Event.MapRegionGeneration(OnMapRegionGen, "standard");
            api.Event.MapRegionGeneration(OnMapRegionGen, "superflat");
        }

        protected void loadAnimalMaps()
        {
            regionSize = api.WorldManager.RegionSize;
            noiseSizeDensityMap = regionSize / TerraGenConfig.animalMapScale;

            HashSet<string> mapCodes = new HashSet<string>();
            foreach (var val in api.World.EntityTypes)
            {
                var spc = val.Server.SpawnConditions;
                if (spc == null) continue;

                if (spc.Climate?.RandomMapCodePool != null)
                {
                    mapCodes.AddRange(spc.Climate.RandomMapCodePool);
                }
                else
                {
                    if (spc.Worldgen?.RandomMapCodePool != null) mapCodes.AddRange(spc.Worldgen.RandomMapCodePool);
                    if (spc.Runtime?.RandomMapCodePool != null) mapCodes.AddRange(spc.Runtime.RandomMapCodePool);
                }
            }

            animalMapGens.Clear();
            foreach (var mapcode in mapCodes)
            {
                int hs = mapcode.GetHashCode();
                int seed = api.World.Seed + 12897 + hs;
                animalMapGens[mapcode] = new MapLayerWobbled(seed, 2, 0.9f, TerraGenConfig.animalMapScale, 4000, -2500);
            }
        }

        protected void OnChunkColumnGen(IChunkColumnGenerateRequest request)
        {
            var chunks = request.Chunks;
            int chunkX = request.ChunkX;
            int chunkZ = request.ChunkZ;

            if (GetIntersectingStructure(chunkX * chunksize + chunksize / 2, chunkZ * chunksize + chunksize / 2, CreaturesHashCode) != null)
            {
                return;
            }
            wgenBlockAccessor.BeginColumn();
            IntDataMap2D climateMap = chunks[0].MapChunk.MapRegion.ClimateMap;
            ushort[] heightMap = chunks[0].MapChunk.WorldGenTerrainHeightMap;

            int regionChunkSize = api.WorldManager.RegionSize / chunksize;
            int rlX = chunkX % regionChunkSize;
            int rlZ = chunkZ % regionChunkSize;

            float facC = (float)climateMap.InnerSize / regionChunkSize;
            climateUpLeft = climateMap.GetUnpaddedInt((int)(rlX * facC), (int)(rlZ * facC));
            climateUpRight = climateMap.GetUnpaddedInt((int)(rlX * facC + facC), (int)(rlZ * facC));
            climateBotLeft = climateMap.GetUnpaddedInt((int)(rlX * facC), (int)(rlZ * facC + facC));
            climateBotRight = climateMap.GetUnpaddedInt((int)(rlX * facC + facC), (int)(rlZ * facC + facC));

            IntDataMap2D forestMap = chunks[0].MapChunk.MapRegion.ForestMap;
            float facF = (float)forestMap.InnerSize / regionChunkSize;
            forestUpLeft = forestMap.GetUnpaddedInt((int)(rlX * facF), (int)(rlZ * facF));
            forestUpRight = forestMap.GetUnpaddedInt((int)(rlX * facF + facF), (int)(rlZ * facF));
            forestBotLeft = forestMap.GetUnpaddedInt((int)(rlX * facF), (int)(rlZ * facF + facF));
            forestBotRight = forestMap.GetUnpaddedInt((int)(rlX * facF + facF), (int)(rlZ * facF + facF));

            IntDataMap2D shrubMap = chunks[0].MapChunk.MapRegion.ShrubMap;
            float facS = (float)shrubMap.InnerSize / regionChunkSize;
            shrubsUpLeft = shrubMap.GetUnpaddedInt((int)(rlX * facS), (int)(rlZ * facS));
            shrubsUpRight = shrubMap.GetUnpaddedInt((int)(rlX * facS + facS), (int)(rlZ * facS));
            shrubsBotLeft = shrubMap.GetUnpaddedInt((int)(rlX * facS), (int)(rlZ * facS + facS));
            shrubsBotRight = shrubMap.GetUnpaddedInt((int)(rlX * facS + facS), (int)(rlZ * facS + facS));

            Vec3d posAsVec = new Vec3d();
            BlockPos pos = new BlockPos(Dimensions.NormalWorld);

            foreach (var val in entityTypeGroups)
            {
                EntityProperties entitytype = val.Key;
                float tries = entitytype.Server.SpawnConditions.Worldgen.TriesPerChunk.nextFloat(1, rnd);
                if (tries == 0f) continue;

                var scRuntime = entitytype.Server.SpawnConditions.Runtime;   // the Group ("hostile"/"neutral"/"passive") is only held in the Runtime spawn conditions
                if (scRuntime == null || scRuntime.Group != "hostile") tries *= gcfg.neutralCreatureSpawnMultiplier;

                while (tries-- > rnd.NextDouble())
                {
                    int dx = rnd.Next(chunksize);
                    int dz = rnd.Next(chunksize);

                    pos.Set(chunkX * chunksize + dx, 0, chunkZ * chunksize + dz);

                    pos.Y =
                        entitytype.Server.SpawnConditions.Worldgen.TryOnlySurface ?
                        heightMap[dz * chunksize + dx] + 1 :
                        rnd.Next(worldheight)
                    ;
                    posAsVec.Set(pos.X + 0.5, pos.Y + 0.005, pos.Z + 0.5);

                    TrySpawnGroupAt(request.Chunks[0].MapChunk.MapRegion, pos, posAsVec, entitytype, val.Value);
                }
            }
        }




        protected void TrySpawnGroupAt(IMapRegion mr, BlockPos origin, Vec3d posAsVec, EntityProperties entityType, EntityProperties[] grouptypes)
        {
            BlockPos pos = origin.Copy();
            int climate;
            float temp;
            float rain;
            float forestDensity;
            float shrubDensity;
            float xRel, zRel;

            int spawned = 0;

            WorldGenSpawnConditions sc = entityType.Server.SpawnConditions.Worldgen;
            string mapcode = entityType.Server.SpawnConditions.Climate?.MapCode ?? entityType.Server.SpawnConditions.Worldgen.MapCode;
            if (mapcode != null && GetAnimalMapDensity(mapcode, posAsVec.XInt, posAsVec.ZInt, mr) < 128) return;

            spawnPositions.Clear();

            int nextGroupSize = 0;
            int tries = 10;
            while (nextGroupSize <= 0 && tries-- > 0)
            {
                float val = sc.HerdSize.nextFloat();
#if PERFTEST
                val *= 40;
#endif
                nextGroupSize = (int)val + ((val - (int)val) > rnd.NextDouble() ? 1 : 0);
            }


            for (int i = 0; i < nextGroupSize * 4 + 5; i++)
            {
                if (spawned >= nextGroupSize) break;

                EntityProperties typeToSpawn = entityType;

                // First entity with valid spawnpos (typically the male) must be the dominant creature, every subsequent only 20% chance for males (or even lower if more than 5 companion types)
                double dominantChance = spawned == 0 ? 1 : Math.Min(0.2, 1f / grouptypes.Length);

                if (grouptypes.Length > 1 && rnd.NextDouble() > dominantChance)
                {
                    typeToSpawn = grouptypes[1 + rnd.Next(grouptypes.Length - 1)];
                }

                IBlockAccessor blockAccessor = wgenBlockAccessor.GetChunkAtBlockPos(pos) == null ? api.World.BlockAccessor : wgenBlockAccessor;

                IMapChunk mapchunk = blockAccessor.GetMapChunkAtBlockPos(pos);
                if (mapchunk != null)
                {
                    if (sc.TryOnlySurface)
                    {
                        ushort[] heightMap = mapchunk.WorldGenTerrainHeightMap;
                        pos.Y = heightMap[(pos.Z % chunksize) * chunksize + (pos.X % chunksize)] + 1;
                    }

                    if (CanSpawnAtPosition(blockAccessor, typeToSpawn, pos, sc))
                    {
                        posAsVec.Set(pos.X + 0.5, pos.Y + 0.005, pos.Z + 0.5);

                        xRel = (float)(posAsVec.X % chunksize) / chunksize;
                        zRel = (float)(posAsVec.Z % chunksize) / chunksize;

                        climate = GameMath.BiLerpRgbColor(xRel, zRel, climateUpLeft, climateUpRight, climateBotLeft, climateBotRight);
                        temp = Climate.GetScaledAdjustedTemperatureFloat((climate >> 16) & 0xff, (int)posAsVec.Y - TerraGenConfig.seaLevel);
                        rain = ((climate >> 8) & 0xff) / 255f;
                        forestDensity = GameMath.BiLerp(forestUpLeft, forestUpRight, forestBotLeft, forestBotRight, xRel, zRel) / 255f;
                        shrubDensity = GameMath.BiLerp(shrubsUpLeft, shrubsUpRight, shrubsBotLeft, shrubsBotRight, xRel, zRel) / 255f;


                        if (CanSpawnAtConditions(blockAccessor, typeToSpawn, pos, posAsVec, sc, rain, temp, forestDensity, shrubDensity))
                        {
                            spawnPositions.Add(new SpawnOppurtunity() { ForType = typeToSpawn, Pos = posAsVec.Clone() });
                            spawned++;
                        }

                    }
                }

                pos.X = origin.X + ((rnd.Next(11) - 5) + (rnd.Next(11) - 5)) / 2;
                pos.Z = origin.Z + ((rnd.Next(11) - 5) + (rnd.Next(11) - 5)) / 2;
            }


            // Only spawn if the group reached the minimum group size
            if (spawnPositions.Count >= nextGroupSize)
            {
                long herdId = api.WorldManager.GetNextUniqueId();

                foreach (SpawnOppurtunity so in spawnPositions)
                {
                    Entity ent = CreateEntity(so.ForType, so.Pos);
                    if (ent is EntityAgent)
                    {
                        (ent as EntityAgent).HerdId = herdId;
                    }

                    if (!api.Event.TriggerTrySpawnEntity(wgenBlockAccessor, ref so.ForType, so.Pos, herdId)) continue;
#if DEBUG
                    api.Logger.VerboseDebug("worldgen spawned one " + so.ForType.Code.Path);
#endif

                    if (wgenBlockAccessor.GetChunkAtBlockPos(pos) == null)
                    {
                        api.World.SpawnEntity(ent);
                    }
                    else
                    {
                        wgenBlockAccessor.AddEntity(ent);
                    }
                }

            }
        }


        protected Entity CreateEntity(EntityProperties entityType, Vec3d spawnPosition)
        {
            Entity entity = api.ClassRegistry.CreateEntity(entityType);
            entity.Pos.SetPosWithDimension(spawnPosition);
            entity.Pos.SetYaw((float)rnd.NextDouble() * GameMath.TWOPI);
            entity.PositionBeforeFalling.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            entity.Attributes.SetString("origin", "worldgen");
            return entity;
        }





        protected bool CanSpawnAtPosition(IBlockAccessor blockAccessor, EntityProperties type, BlockPos pos, BaseSpawnConditions sc)
        {
            if (!blockAccessor.IsValidPos(pos)) return false;
            Block block = blockAccessor.GetBlock(pos);
            if (!sc.CanSpawnInside(block)) return false;

            pos.Y--;

            Block belowBlock = blockAccessor.GetBlock(pos);
            if (!belowBlock.CanCreatureSpawnOn(blockAccessor, pos, type, sc))
            {
                pos.Y++;
                return false;
            }

            pos.Y++;
            return true;
        }

        protected bool CanSpawnAtConditions(IBlockAccessor blockAccessor, EntityProperties type, BlockPos pos, Vec3d posAsVec, BaseSpawnConditions sc, float rain, float temp, float forestDensity, float shrubsDensity)
        {
            float? lightLevel = blockAccessor.GetLightLevel(pos, EnumLightLevelType.MaxLight);

            if (lightLevel == null) return false;
            if (sc.MinLightLevel > lightLevel || sc.MaxLightLevel < lightLevel) return false;
            if (sc.MinTemp > temp || sc.MaxTemp < temp) return false;
            if (sc.MinRain > rain || sc.MaxRain < rain) return false;
            if (sc.MinForest > forestDensity || sc.MaxForest < forestDensity) return false;
            if (sc.MinShrubs > shrubsDensity || sc.MaxShrubs < shrubsDensity) return false;
            if (sc.MinForestOrShrubs > Math.Max(forestDensity, shrubsDensity)) return false;

            double yRel =
                pos.Y > TerraGenConfig.seaLevel ?
                1 + ((double)pos.Y - TerraGenConfig.seaLevel) / (api.World.BlockAccessor.MapSizeY - TerraGenConfig.seaLevel) :
                (double)pos.Y / TerraGenConfig.seaLevel
            ;
            if (sc.MinY > yRel || sc.MaxY < yRel) return false;

            Cuboidf collisionBox = type.SpawnCollisionBox.OmniNotDownGrowBy(0.1f);

            return !IsColliding(collisionBox, posAsVec);
        }



        protected bool Event_OnTrySpawnEntity(IBlockAccessor ba, ref EntityProperties properties, Vec3d spawnPosition, long herdId)
        {
            var spc = properties.Server.SpawnConditions;
            if (spc == null) return true;

            string mapCode = spc.Climate?.MapCode ?? spc.Runtime?.MapCode;
            if (mapCode != null)
            {
                var mr = ba.GetMapRegion((int)spawnPosition.X / ba.RegionSize, (int)spawnPosition.Z / ba.RegionSize);
                float dens = GetAnimalMapDensity(mapCode, (int)spawnPosition.X, (int)spawnPosition.Z, mr);
                if (dens < 128) return false;
            }

            return true;
        }


        /// <summary>
        /// Returns 0..255
        /// </summary>
        /// <param name="code"></param>
        /// <param name="posX"></param>
        /// <param name="posZ"></param>
        /// <param name="mapregion"></param>
        /// <returns></returns>
        public float GetAnimalMapDensity(string code, int posX, int posZ, IMapRegion mapregion)
        {
            if (mapregion == null) return 0;
            int lx = posX % regionSize;
            int lz = posZ % regionSize;

            mapregion.AnimalSpawnMaps.TryGetValue(code, out ByteDataMap2D map);
            if (map != null)
            {
                float posXInRegionOre = GameMath.Clamp((float)lx / regionSize * noiseSizeDensityMap, 0, noiseSizeDensityMap - 1);
                float posZInRegionOre = GameMath.Clamp((float)lz / regionSize * noiseSizeDensityMap, 0, noiseSizeDensityMap - 1);

                float density = map.GetUnpaddedLerped(posXInRegionOre, posZInRegionOre);

                return density;
            }

            return 0;
        }




        // Custom implementation for mixed generating/loaded chunk access, since we can spawn entities just fine in either loaded or still generating chunks
        public bool IsColliding(Cuboidf entityBoxRel, Vec3d pos)
        {
            BlockPos blockPos = new BlockPos(Dimensions.NormalWorld);
            IBlockAccessor blockAccess;
            const int chunksize = GlobalConstants.ChunkSize;

            Cuboidd entityCuboid = entityBoxRel.ToDouble().Translate(pos);
            Vec3d blockPosAsVec = new Vec3d();

            int minX = (int)(entityBoxRel.X1 + pos.X);
            int minY = (int)(entityBoxRel.Y1 + pos.Y);
            int minZ = (int)(entityBoxRel.Z1 + pos.Z);
            int maxX = (int)Math.Ceiling(entityBoxRel.X2 + pos.X);
            int maxY = (int)Math.Ceiling(entityBoxRel.Y2 + pos.Y);
            int maxZ = (int)Math.Ceiling(entityBoxRel.Z2 + pos.Z);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        blockAccess = wgenBlockAccessor;
                        IWorldChunk chunk = wgenBlockAccessor.GetChunkAtBlockPos(x, y, z);
                        if (chunk == null)
                        {
                            chunk = api.World.BlockAccessor.GetChunkAtBlockPos(x, y, z);
                            blockAccess = api.World.BlockAccessor;
                        }
                        if (chunk == null) return true;

                        int index = ((y % chunksize) * chunksize + (z % chunksize)) * chunksize + (x % chunksize);
                        Block block = api.World.Blocks[chunk.UnpackAndReadBlock(index, BlockLayersAccess.Default)];

                        blockPos.Set(x, y, z);
                        blockPosAsVec.Set(x, y, z);

                        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccess, blockPos);
                        for (int i = 0; collisionBoxes != null && i < collisionBoxes.Length; i++)
                        {
                            Cuboidf collBox = collisionBoxes[i];
                            if (collBox != null && entityCuboid.Intersects(collBox, blockPosAsVec)) return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
