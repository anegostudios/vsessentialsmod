using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Datastructures;
using Vintagestory.ServerMods.NoObf;

#nullable disable

namespace Vintagestory.ServerMods
{
    public delegate bool TryGenerateHandler(SchematicPlacementData data);

    public class WorldGenStoryStructure : WorldGenStructureBase
    {
        [JsonProperty]
        public string Group;
        [JsonProperty]
        public string RequireLandform;
        [JsonProperty]
        public int LandformRadius;
        [JsonProperty]
        public int GenerationRadius;
        [JsonProperty]
        public string DependsOnStructure;
        [JsonProperty]
        public int MinSpawnDistX;
        [JsonProperty]
        public int MaxSpawnDistX;
        [JsonProperty]
        public int MinSpawnDistZ;
        [JsonProperty]
        public int MaxSpawnDistZ;
        [JsonProperty]
        public int ExtraLandClaimX;
        [JsonProperty]
        public int ExtraLandClaimZ;

        [JsonProperty]
        public Dictionary<string, int> SkipGenerationCategories;
        public Dictionary<int, int> SkipGenerationFlags;

        [JsonProperty]
        public int? ForceRain;

        [JsonProperty]
        public int? ForceTemperature;

        [JsonProperty]
        public bool GenerateGrass;

        [JsonProperty]
        public Cuboidi[] CustomLandClaims;

        [JsonProperty]
        public bool ExcludeSchematicSizeProtect;

        [JsonProperty]
        public bool UseWorldgenHeight;


        internal BlockSchematicPartial schematicData;

        [JsonProperty]
        public AssetLocation[] ReplaceWithBlocklayers;
        internal int[] replacewithblocklayersBlockids = Array.Empty<int>();

        internal Dictionary<int, Dictionary<int, int>> resolvedRockTypeRemaps = null;

        [JsonProperty]
        public bool DisableSurfaceTerrainBlending;

        public void Init(ICoreServerAPI api, WorldGenStoryStructuresConfig scfg, RockStrataConfig rockstrata, BlockLayerConfig blockLayerConfig)
        {
            schematicData = LoadSchematics<BlockSchematicPartial>(api, Schematics, null)[0];
            // schematicData.Init(api.World.BlockAccessor);     // radfast note: do not Init() here for performance; .Init() will be called in BlockSchematicStructure.Unpack()
            schematicData.blockLayerConfig = blockLayerConfig;

            scfg.SchematicYOffsets.TryGetValue("story/" + schematicData.FromFile.GetNameWithDomain().Replace(".json", ""), out var offset);
            schematicData.OffsetY = offset;

            if (SkipGenerationCategories != null)
            {
                SkipGenerationFlags = new Dictionary<int, int>();
                foreach (var category in SkipGenerationCategories)
                {
                    int key = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(category.Key.ToLowerInvariant())));
                    if (!ModStdWorldGen.AllHashCodes.Contains(key))
                    {
                        api.Logger.Warning($"Unknown skip generation category for story structure {Code}: {category.Key}");
                    }
                    SkipGenerationFlags.Add(key, category.Value);
                }
            }

            // For rocktyped structures
            if (RockTypeRemapGroup != null)
            {
                resolvedRockTypeRemaps = scfg.resolvedRocktypeRemapGroups[RockTypeRemapGroup];
            }

            if (RockTypeRemaps != null)
            {
                if (resolvedRockTypeRemaps != null)
                {
                    var ownRemaps = WorldGenStructuresConfigBase.ResolveRockTypeRemaps(RockTypeRemaps, rockstrata, api);
                    foreach (var val in resolvedRockTypeRemaps)
                    {
                        ownRemaps[val.Key] = val.Value;
                    }

                    resolvedRockTypeRemaps = ownRemaps;
                }
                else
                {
                    resolvedRockTypeRemaps = WorldGenStructuresConfigBase.ResolveRockTypeRemaps(RockTypeRemaps, rockstrata, api);
                }
            }

            if (ReplaceWithBlocklayers != null)
            {
                replacewithblocklayersBlockids = new int[ReplaceWithBlocklayers.Length];
                for (var i = 0; i < replacewithblocklayersBlockids.Length; i++)
                {
                    var block = api.World.GetBlock(ReplaceWithBlocklayers[i]);
                    if (block == null)
                    {
                        throw new Exception(string.Format("Schematic with code {0} has replace block layer {1} defined, but no such block found!",
                            Code, ReplaceWithBlocklayers[i]));
                    }

                    replacewithblocklayersBlockids[i] = block.Id;
                }
            }
        }
    }

    public class WorldGenStructure : WorldGenStructureBase
    {
        [JsonProperty]
        public string Group;
        [JsonProperty]
        public int MinGroupDistance = 0;
        [JsonProperty]
        public float Chance = 0.05f;
        [JsonProperty]
        public int MinTemp = -30;
        [JsonProperty]
        public int MaxTemp = 40;
        [JsonProperty]
        public float MinRain = 0;
        [JsonProperty]
        public float MaxRain = 1;
        [JsonProperty]
        public float MinForest = 0;
        [JsonProperty]
        public float MaxForest = 1;

        [JsonProperty]
        public AssetLocation[] ReplaceWithBlocklayers;
        [JsonProperty]
        public bool PostPass = false;
        [JsonProperty]
        public bool SuppressTrees = false;
        [JsonProperty]
        public bool SuppressWaterfalls = false;
        [JsonProperty]
        public int StoryMaxFromCenter = 0;


        internal BlockSchematicStructure[][] schematicDatas;
        internal int[] replacewithblocklayersBlockids = Array.Empty<int>();
        internal HashSet<int> insideblockids = new HashSet<int>();

        internal Dictionary<int, Dictionary<int, int>> resolvedRockTypeRemaps = null;

        TryGenerateHandler[] Generators;

        public WorldGenStructure()
        {
            Generators = new TryGenerateHandler[]
            {
                TryGenerateRuinAtSurface,
                TryGenerateAtSurface,
                TryGenerateUnderwater,
                TryGenerateUnderground,
                TryGenerateInShallowWater,
            };
        }

        LCGRandom rand;

        int unscaledMinForest;
        int unscaledMaxForest;
        int unscaledMinRain;
        int unscaledMaxRain;
        int unscaledMinTemp;
        int unscaledMaxTemp;

        GenStructures genStructuresSys;

        public void Init(ICoreServerAPI api, BlockLayerConfig config, RockStrataConfig rockstrata, WorldGenStructuresConfig structureConfig, LCGRandom rand)
        {
            this.rand = rand;

            genStructuresSys = api.ModLoader.GetModSystem<GenStructures>();

            unscaledMinRain = (int)(MinRain * 255);
            unscaledMaxRain = (int)(MaxRain * 255);
            unscaledMinTemp = Climate.DescaleTemperature(MinTemp);
            unscaledMaxTemp = Climate.DescaleTemperature(MaxTemp);
            unscaledMinForest = (int)(MinForest * 255);
            unscaledMaxForest = (int)(MaxForest * 255);


            schematicDatas = LoadSchematicsWithRotations<BlockSchematicStructure>(api, this, config, structureConfig, structureConfig.SchematicYOffsets);

            if (schematicDatas.Length == 0)
            {
                throw new InvalidOperationException(string.Format("WorldgenStructure group {0} schematics resolves to zero schematidDatas from {1}", Group, Code));
            }

            if (ReplaceWithBlocklayers != null)
            {
                replacewithblocklayersBlockids = new int[ReplaceWithBlocklayers.Length];
                for (int i = 0; i < replacewithblocklayersBlockids.Length; i++)
                {
                    Block block = api.World.GetBlock(ReplaceWithBlocklayers[i]);
                    if (block == null)
                    {
                        throw new Exception(string.Format("Schematic with code {0} has replace block layer {1} defined, but no such block found!", Code, ReplaceWithBlocklayers[i]));
                    }

                    replacewithblocklayersBlockids[i] = block.Id;
                }
            }

            if (InsideBlockCodes != null)
            {
                for (int i = 0; i < InsideBlockCodes.Length; i++)
                {
                    Block block = api.World.GetBlock(InsideBlockCodes[i]);
                    if (block == null)
                    {
                        throw new Exception(string.Format("Schematic with code {0} has inside block {1} defined, but no such block found!", Code, InsideBlockCodes[i]));
                    }
                    else
                    {
                        insideblockids.Add(block.Id);
                    }

                }
            }

            // For rocktyped ruins
            if (RockTypeRemapGroup != null)
            {
                resolvedRockTypeRemaps = structureConfig.resolvedRocktypeRemapGroups[RockTypeRemapGroup];
            }

            if (RockTypeRemaps != null)
            {
                if (resolvedRockTypeRemaps != null)
                {
                    var ownRemaps = WorldGenStructuresConfigBase.ResolveRockTypeRemaps(RockTypeRemaps, rockstrata, api);
                    foreach (var val in resolvedRockTypeRemaps)
                    {
                        ownRemaps[val.Key] = val.Value;
                    }
                    resolvedRockTypeRemaps = ownRemaps;
                } else
                {
                    resolvedRockTypeRemaps = WorldGenStructuresConfigBase.ResolveRockTypeRemaps(RockTypeRemaps, rockstrata, api);
                }

            }
        }

        BlockPos tmpPos = new BlockPos(Dimensions.NormalWorld);
        public Cuboidi LastPlacedSchematicLocation = new Cuboidi();
        public BlockSchematicStructure LastPlacedSchematic;

        internal bool TryGenerate(SchematicPlacementData data, int forest)
        {
            const int chunksize = GlobalConstants.ChunkSize;

            int climate = GameMath.BiLerpRgbColor((float)(data.StartPos.X % chunksize) / chunksize, (float)(data.StartPos.Z % chunksize) / chunksize, data.ClimateUpLeft, data.ClimateUpRight, data.ClimateBotLeft, data.ClimateBotRight);

            int rain = Climate.GetRainFall((climate >> 8) & 0xff, data.StartPos.Y);
            int unscaledtempsealevel = (climate >> 16) & 0xff;

            int temp = Climate.GetScaledAdjustedTemperature(unscaledtempsealevel, data.StartPos.Y - TerraGenConfig.seaLevel);
            int unscaledtemp = Climate.DescaleTemperature(temp);

            if (rain < unscaledMinRain || rain > unscaledMaxRain || unscaledtemp < unscaledMinTemp || unscaledtemp > unscaledMaxTemp || forest < unscaledMinForest || forest > unscaledMaxForest) return false;

            // Hardcoding crime here. Please don't look. Takes these tasty cookies as a bribe. (Prevent generation of schematics on glaciers)
            if (unscaledtemp < 20 && data.StartPos.Y > data.World.SeaLevel + 15) return false;

            rand.InitPositionSeed(data.StartPos.X, data.StartPos.Z);

            bool generated = Generators[(int)Placement](data);

            if (generated && Placement == EnumStructurePlacement.SurfaceRuin)
            {
                float rainValMoss = Math.Max(0, (rain - 50) / 255f);
                float tempValMoss = Math.Max(0, (unscaledtemp - 50) / 255f);
                float mossGrowthChance = 1.5f * rainValMoss * tempValMoss + 1f * rainValMoss * GameMath.Clamp((tempValMoss + 0.33f) / 1.33f, 0, 1);

                int mossTries = (int)(10 * mossGrowthChance * GameMath.Sqrt(LastPlacedSchematicLocation.SizeXYZ));
                int sizex = LastPlacedSchematic.SizeX;
                int sizey = LastPlacedSchematic.SizeY;
                int sizez = LastPlacedSchematic.SizeZ;
                BlockPos tmpPos = new BlockPos(data.StartPos.dimension);

                Block mossDecor = data.BlockAccessor.GetBlock(new AssetLocation("attachingplant-spottymoss"));

                while (mossTries-- > 0)
                {
                    int dx = rand.NextInt(sizex);
                    int dy = rand.NextInt(sizey);
                    int dz = rand.NextInt(sizez);
                    tmpPos.Set(data.StartPos.X + dx, data.StartPos.Y + dy, data.StartPos.Z + dz);
                    var block = data.BlockAccessor.GetBlock(tmpPos);
                    if (block.BlockMaterial == EnumBlockMaterial.Stone)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            var face = BlockFacing.ALLFACES[i];
                            if (!block.SideSolid[i]) continue;

                            var nblock = data.BlockAccessor.GetBlockOnSide(tmpPos, face);
                            if (!nblock.SideSolid[face.Opposite.Index])
                            {
                                data.BlockAccessor.SetDecor(mossDecor, tmpPos, face);
                                break;
                            }
                        }
                    }
                }
            }

            return generated;
        }

        /// <summary>
        /// Finds the side with lowest summed height values along the border of the to be placed schematic.
        /// The search is done on each side +2.
        /// Requires <see cref="BlockSchematic.EntranceRotation"/>
        /// </summary>
        /// <param name="data"></param>
        /// <param name="schematics">array of schematics[4] N=0, E=1, S=2, W=3</param>
        /// <returns>The index for the rotation of the desired side to pick from the <see cref="schematicDatas"/>[num][rotation] North=0 East=1 South=2 West=3</returns>
        private int FindClearEntranceRotation(SchematicPlacementData data, BlockSchematicStructure[] schematics)
        {
            const int chunksize = GlobalConstants.ChunkSize;
            var schematic = schematics[0];
            var entranceRot = GameMath.Clamp(schematics[0].EntranceRotation / 90, 0, 3);
            // pos is in the corner and not centered
            var minX = data.StartPos.X-2;
            var maxX = data.StartPos.X + schematic.SizeX+2;
            var minZ = data.StartPos.Z-2;
            var maxZ = data.StartPos.Z + schematic.SizeZ+2;

            // used to detect whether downwards slope is East-West etc
            int weightedHeightW = 1, weightedHeightE = 1, weightedHeightN = 1, weightedHeightS = 1;
            var x = minX;
            int z;

            IMapChunk mapchunk;
            int lowSide;
            // entrance is east or west
            if (entranceRot == 1 || entranceRot == 3)
            {
                for (z = minZ; z <= maxZ; z++)
                {
                    mapchunk = data.BlockAccessor.GetMapChunk(x / chunksize, z / chunksize);
                    int h = mapchunk.WorldGenTerrainHeightMap[(z % chunksize) * chunksize + (x % chunksize)];
                    weightedHeightW += h;
                }

                x = maxX;
                for (z = minZ; z <= maxZ; z++)
                {
                    mapchunk = data.BlockAccessor.GetMapChunk(x / chunksize, z / chunksize);
                    int h = mapchunk.WorldGenTerrainHeightMap[(z % chunksize) * chunksize + (x % chunksize)];
                    weightedHeightE += h;
                }
            } else if (entranceRot == 0 || entranceRot == 2) // entrance is north or south
            {
                z = minZ;
                for (x = minX; x <= maxX; x++)
                {
                    mapchunk = data.BlockAccessor.GetMapChunk(x / chunksize, z / chunksize);
                    int h = mapchunk.WorldGenTerrainHeightMap[(z % chunksize) * chunksize + (x % chunksize)];
                    weightedHeightN += h;
                }

                z = maxZ;
                for (x = minX; x <= maxX; x++)
                {
                    mapchunk = data.BlockAccessor.GetMapChunk(x / chunksize, z / chunksize);
                    int h = mapchunk.WorldGenTerrainHeightMap[(z % chunksize) * chunksize + (x % chunksize)];
                    weightedHeightS += h;
                }
            }

            // check 2nd rot - rotate the schematic once by 90
            schematic = schematics[1];
            var entranceRot2 = GameMath.Clamp(schematic.EntranceRotation / 90, 0, 3);
            // pos is in the corner and not centered
            minX = data.StartPos.X-2;
            maxX = data.StartPos.X + schematic.SizeX+2;
            minZ = data.StartPos.Z-2;
            maxZ = data.StartPos.Z + schematic.SizeZ+2;
            int weightedHeightW2 = 1, weightedHeightE2 = 1, weightedHeightN2 = 1, weightedHeightS2 = 1;

            // entrance is east or west
            if (entranceRot2 == 1 || entranceRot2 == 3)
            {
                for (z = minZ; z <= maxZ; z++)
                {
                    mapchunk = data.BlockAccessor.GetMapChunk(x / chunksize, z / chunksize);
                    int h = mapchunk.WorldGenTerrainHeightMap[(z % chunksize) * chunksize + (x % chunksize)];
                    weightedHeightW2 += h;
                }

                x = maxX;
                for (z = minZ; z <= maxZ; z++)
                {
                    mapchunk = data.BlockAccessor.GetMapChunk(x / chunksize, z / chunksize);
                    int h = mapchunk.WorldGenTerrainHeightMap[(z % chunksize) * chunksize + (x % chunksize)];
                    weightedHeightE2 += h;
                }
            } else if (entranceRot2 == 0 || entranceRot2 == 2) // entrance is north or south
            {
                z = minZ;
                for (x = minX; x <= maxX; x++)
                {
                    mapchunk = data.BlockAccessor.GetMapChunk(x / chunksize, z / chunksize);
                    int h = mapchunk.WorldGenTerrainHeightMap[(z % chunksize) * chunksize + (x % chunksize)];
                    weightedHeightN2 += h;
                }

                z = maxZ;
                for (x = minX; x <= maxX; x++)
                {
                    mapchunk = data.BlockAccessor.GetMapChunk(x / chunksize, z / chunksize);
                    int h = mapchunk.WorldGenTerrainHeightMap[(z % chunksize) * chunksize + (x % chunksize)];
                    weightedHeightS2 += h;
                }
            }

            // entranceRot E/W
            if (entranceRot == 1 || entranceRot == 3)
            {
                // so entranceRot2 must be N/S
                if (weightedHeightE < weightedHeightW)
                {
                    if (weightedHeightN2 < weightedHeightS2)
                    {
                        lowSide = weightedHeightE < weightedHeightN2 ? 1 : 0;
                    }
                    else
                    {
                        lowSide = weightedHeightE < weightedHeightS2 ? 1 : 2;
                    }
                }
                else // weightedHeightE > weightedHeightW
                {
                    if (weightedHeightN2 < weightedHeightS2)
                    {
                        lowSide = weightedHeightW < weightedHeightN2 ? 3 : 0;
                    }
                    else
                    {
                        lowSide = weightedHeightW < weightedHeightS2 ? 3 : 2;
                    }
                }
            }
            else // entranceRot N/S
            {
                // so entranceRot2 must be E/W
                if (weightedHeightN < weightedHeightS)
                {
                    if (weightedHeightE2 < weightedHeightW2)
                    {
                        lowSide = weightedHeightN < weightedHeightE2 ? 0 : 1;
                    }
                    else // weightedHeightE2 > weightedHeightW2
                    {
                        lowSide = weightedHeightN < weightedHeightW2 ? 0 : 3;
                    }
                }
                else // weightedHeightN > weightedHeightS
                {
                    if (weightedHeightE2 < weightedHeightW2)
                    {
                        lowSide = weightedHeightS < weightedHeightE2 ? 2 : 1;
                    }
                    else
                    {
                        lowSide = weightedHeightS < weightedHeightW2 ? 0 : 3;
                    }
                }
            }

            // calculate the index to get the correct rotation offset by entranceRot (eg. entranceRot = 1, lowestSide = 2 => 1, e: 3, l: 2 => 3)
            return (4 + lowSide - entranceRot) % 4;
        }

        internal bool TryGenerateRuinAtSurface(SchematicPlacementData data)
        {
            if (schematicDatas.Length == 0) return false;
            int num = rand.NextInt(schematicDatas.Length);
            int orient = rand.NextInt(4);
            BlockSchematicStructure schematic = schematicDatas[num][orient];
            schematic.Unpack(data.World.Api, orient);

            data.StartPos.Add(0, schematic.OffsetY, 0);
            int startPosX = data.StartPos.X;
            int startPosZ = data.StartPos.Z;
            var blockAccessor = data.BlockAccessor;
            var tmpPos = this.tmpPos;

            if (schematic.EntranceRotation != -1)
            {
                orient = FindClearEntranceRotation(data, schematicDatas[num]);
                schematic = schematicDatas[num][orient];
                schematic.Unpack(data.World.Api, orient);
            }

            int wdthalf = (int)Math.Ceiling(schematic.SizeX / 2f);
            int lenhalf = (int)Math.Ceiling(schematic.SizeZ / 2f);

            int wdt = schematic.SizeX;
            int len = schematic.SizeZ;

            tmpPos.Set(startPosX + wdthalf, 0, startPosZ + lenhalf);
            int centerY = blockAccessor.GetTerrainMapheightAt(data.StartPos);

            // check if we are to deep underwater
            if (centerY < data.World.SeaLevel - MaxBelowSealevel)
                return false;
            // Probe all 4 corners + center if they either touch the surface or are sightly below ground

            tmpPos.Set(startPosX, 0, startPosZ);
            int topLeftY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            tmpPos.Set(startPosX + wdt, 0, startPosZ);
            int topRightY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            tmpPos.Set(startPosX, 0, startPosZ + len);
            int botLeftY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            tmpPos.Set(startPosX + wdt, 0, startPosZ + len);
            int botRightY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            int maxY = GameMath.Max(centerY, topLeftY, topRightY, botLeftY, botRightY);
            int minY = GameMath.Min(centerY, topLeftY, topRightY, botLeftY, botRightY);

            // improve flatness check for larger structures
            if (wdt >= 30)
            {
                var size = (int)(wdt * 0.15 + 8);
                for (int i = size; i < wdt; i+=size)
                {
                    tmpPos.Set(startPosX + i, 0, startPosZ);
                    var topSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                    tmpPos.Set(startPosX + i, 0, startPosZ + len);
                    var botSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                    tmpPos.Set(startPosX + i, 0, startPosZ + len / 2);
                    var centerSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                    maxY = GameMath.Max(maxY, topSide, botSide, centerSide);
                    minY = GameMath.Min(minY, topSide, botSide, centerSide);
                }
            }
            else if (wdt >= 15) // check center on X
            {
                var size = wdt / 2;
                tmpPos.Set(startPosX + size, 0, startPosZ);
                var topSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                tmpPos.Set(startPosX + size, 0, startPosZ + len);
                var botSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                tmpPos.Set(startPosX + size, 0, startPosZ + len / 2);
                var centerSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                maxY = GameMath.Max(maxY, topSide, botSide, centerSide);
                minY = GameMath.Min(minY, topSide, botSide, centerSide);
            }
            if (len >= 30)
            {
                var size = (int)(len * 0.15 + 8);
                for (int i = size; i < len; i+=size)
                {
                    tmpPos.Set(startPosX + wdt, 0, startPosZ + i);
                    var rightSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                    tmpPos.Set(startPosX, 0, startPosZ + i);
                    var leftSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                    tmpPos.Set(startPosX + wdt / 2, 0, startPosZ + i);
                    var centerSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                    maxY = GameMath.Max(maxY, rightSide, leftSide, centerSide);
                    minY = GameMath.Min(minY, rightSide, leftSide, centerSide);
                }
            }
            else if (len >= 15) // check center on Z
            {
                var size = len / 2;
                tmpPos.Set(startPosX + wdt, 0, startPosZ + size);
                var rightSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                tmpPos.Set(startPosX, 0, startPosZ + size);
                var leftSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                tmpPos.Set(startPosX + wdt / 2, 0, startPosZ + size);
                var centerSide = blockAccessor.GetTerrainMapheightAt(tmpPos);

                maxY = GameMath.Max(maxY, rightSide, leftSide, centerSide);
                minY = GameMath.Min(minY, rightSide, leftSide, centerSide);
            }

            int diff = Math.Abs(maxY - minY);
            if (diff > schematic.MaxYDiff) return false;

            data.StartPos.Y = minY + schematic.OffsetY;


            // Ensure not deeply submerged in water  =>  actually, that's now OK!


            tmpPos.Set(startPosX, data.StartPos.Y + 1, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y + 1, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX, data.StartPos.Y + 1, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y + 1, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            // Generating exactly at "TerrainMapheightAt" means we will place it 1 block below ground.
            // With a offsetY of 0, the structure should literally be fully above ground.
            // But offsetY default is -1 so it will be just a block below the surface unless specified otherwise
            data.StartPos.Y++;

            // if we have above ground blocks check if they are above the ground
            if (!TestAboveGroundCheckPositions(blockAccessor, data.StartPos, schematic.AbovegroundCheckPositions)) return false;

            if (!SatisfiesMinDistance(data.StartPos, data.World)) return false;
            if (WouldOverlapAt(blockAccessor,data.StartPos, schematic, data.StoryStructureLocationCode)) return false;

            LastPlacedSchematicLocation.Set(startPosX, data.StartPos.Y, startPosZ, startPosX + wdt, data.StartPos.Y + schematic.SizeY, startPosZ + len);
            LastPlacedSchematic = schematic;

            data.ReplaceWithBlockLayersBlockids = replacewithblocklayersBlockids;
            data.ResolvedRockTypeRemaps = resolvedRockTypeRemaps;
            schematic.PlaceRespectingBlockLayers(data);

            return true;
        }

        internal bool TryGenerateAtSurface(SchematicPlacementData data)
        {
            int num = rand.NextInt(schematicDatas.Length);
            int orient = rand.NextInt(4);
            BlockSchematicStructure schematic = schematicDatas[num][orient];
            schematic.Unpack(data.World.Api, orient);

            data.StartPos.Add(0, schematic.OffsetY, 0);
            int startPosX = data.StartPos.X;
            int startPosZ = data.StartPos.Z;
            var blockAccessor = data.BlockAccessor;
            var tmpPos = this.tmpPos;

            if (schematic.EntranceRotation != -1)
            {
                orient = FindClearEntranceRotation(data, schematicDatas[num]);
                schematic = schematicDatas[num][orient];
                schematic.Unpack(data.World.Api, orient);
            }

            int wdthalf = (int)Math.Ceiling(schematic.SizeX / 2f);
            int lenhalf = (int)Math.Ceiling(schematic.SizeZ / 2f);
            int wdt = schematic.SizeX;
            int len = schematic.SizeZ;


            tmpPos.Set(startPosX + wdthalf, 0, startPosZ + lenhalf);
            int centerY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            // check if we are to deep underwater
            if (centerY < data.World.SeaLevel - MaxBelowSealevel)
            {
                return false;
            }

            // Probe all 4 corners + center if they are on the same height
            tmpPos.Set(startPosX, 0, startPosZ);
            int topLeftY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            tmpPos.Set(startPosX + wdt, 0, startPosZ);
            int topRightY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            tmpPos.Set(startPosX, 0, startPosZ + len);
            int botLeftY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            tmpPos.Set(startPosX + wdt, 0, startPosZ + len);
            int botRightY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            // Is the ground flat?
            int diff = GameMath.Max(centerY, topLeftY, topRightY, botLeftY, botRightY) - GameMath.Min(centerY, topLeftY, topRightY, botLeftY, botRightY);
            if (diff != 0) return false;

            data.StartPos.Y = centerY + 1;

            // Ensure not floating on water
            tmpPos.Set(startPosX + wdthalf, data.StartPos.Y - 1, startPosZ + lenhalf);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX, data.StartPos.Y - 1, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y - 1, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX, data.StartPos.Y - 1, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y - 1, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            // Ensure not submerged in water
            tmpPos.Set(startPosX, data.StartPos.Y, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;


            tmpPos.Set(startPosX + wdt, data.StartPos.Y, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX, data.StartPos.Y, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;



            tmpPos.Set(startPosX, data.StartPos.Y + 1, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y + 1, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX, data.StartPos.Y + 1, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y + 1, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            // Add the offset after the liquid check or we check below the lake
            data.StartPos.Y += schematic.OffsetY;

            // if we have above ground blocks check if they are above the ground
            if (!TestAboveGroundCheckPositions(blockAccessor, data.StartPos, schematic.AbovegroundCheckPositions)) return false;
            if (!TestUndergroundCheckPositions(blockAccessor, data.StartPos, schematic.UndergroundCheckPositions)) return false;
            if (!SatisfiesMinDistance(data.StartPos, data.World)) return false;
            if (WouldOverlapAt(blockAccessor, data.StartPos, schematic, data.StoryStructureLocationCode)) return false;

            LastPlacedSchematicLocation.Set(startPosX, data.StartPos.Y, startPosZ, startPosX + schematic.SizeX, data.StartPos.Y + schematic.SizeY, startPosZ + schematic.SizeZ);
            LastPlacedSchematic = schematic;
            data.DisplaceWater = true;
            data.ReplaceWithBlockLayersBlockids = replacewithblocklayersBlockids;
            data.ResolvedRockTypeRemaps = resolvedRockTypeRemaps;
            schematic.PlaceRespectingBlockLayers(data);
            return true;
        }


        internal bool TryGenerateInShallowWater(SchematicPlacementData data)
        {
            int num = rand.NextInt(schematicDatas.Length);
            int orient = rand.NextInt(4);
            BlockSchematicStructure schematic = schematicDatas[num][orient];
            schematic.Unpack(data.World.Api, orient);

            data.StartPos.Add(0, schematic.OffsetY, 0);
            int startPosX = data.StartPos.X;
            int startPosZ = data.StartPos.Z;
            var blockAccessor = data.BlockAccessor;
            var tmpPos = this.tmpPos;

            if (schematic.EntranceRotation != -1)
            {
                orient = FindClearEntranceRotation(data, schematicDatas[num]);
                schematic = schematicDatas[num][orient];
                schematic.Unpack(data.World.Api, orient);
            }

            int wdthalf = (int)Math.Ceiling(schematic.SizeX / 2f);
            int lenhalf = (int)Math.Ceiling(schematic.SizeZ / 2f);
            int wdt = schematic.SizeX;
            int len = schematic.SizeZ;


            tmpPos.Set(startPosX + wdthalf, 0, startPosZ + lenhalf);
            int centerY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            // check if we are to deep underwater
            if (centerY < data.World.SeaLevel - MaxBelowSealevel)
            {
                return false;
            }

            // Probe all 4 corners + center if they are on the same height
            tmpPos.Set(startPosX, 0, startPosZ);
            int topLeftY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            tmpPos.Set(startPosX + wdt, 0, startPosZ);
            int topRightY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            tmpPos.Set(startPosX, 0, startPosZ + len);
            int botLeftY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            tmpPos.Set(startPosX + wdt, 0, startPosZ + len);
            int botRightY = blockAccessor.GetTerrainMapheightAt(tmpPos);

            // Is the ground flat?
            int maxy = GameMath.Max(centerY, topLeftY, topRightY, botLeftY, botRightY);
            int diff = maxy - GameMath.Min(centerY, topLeftY, topRightY, botLeftY, botRightY);
            if (diff != 1) return false;

            data.StartPos.Y = centerY + 1;

            // Ensure not floating on water
            tmpPos.Set(startPosX + wdthalf, data.StartPos.Y - 1, startPosZ + lenhalf);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX, data.StartPos.Y - 1, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y - 1, startPosZ);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX, data.StartPos.Y - 1, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            tmpPos.Set(startPosX + wdt, data.StartPos.Y - 1, startPosZ + len);
            if (blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Fluid).IsLiquid()) return false;

            // Ensure at least 1 block, but not more than 2 blocks underwater
            tmpPos.Set(startPosX, 0, startPosZ);
            int rainTopLeftY = blockAccessor.GetRainMapHeightAt(tmpPos);
            if (rainTopLeftY - topLeftY < 1 || rainTopLeftY - topLeftY > 2) return false;

            tmpPos.Set(startPosX + wdt, 0, startPosZ);
            int rainTopRightY = blockAccessor.GetRainMapHeightAt(tmpPos);
            if (rainTopRightY - topRightY < 1 || rainTopRightY - topRightY > 2) return false;

            tmpPos.Set(startPosX, 0, startPosZ + len);
            int rainBotLeftY = blockAccessor.GetRainMapHeightAt(tmpPos);
            if (rainBotLeftY - botLeftY < 1 || rainBotLeftY -  botLeftY > 2) return false;

            tmpPos.Set(startPosX + wdt, 0, startPosZ + len);
            int rainBotRightY = blockAccessor.GetRainMapHeightAt(tmpPos);
            if (rainBotRightY - botRightY < 1 || rainBotRightY - botRightY > 2) return false;

            // Add the offset after the liquid check or we check below the lake
            data.StartPos.Y = maxy + 1 + schematic.OffsetY;

            // if we have above ground blocks check if they are above the ground
            if (!TestAboveGroundCheckPositions(blockAccessor, data.StartPos, schematic.AbovegroundCheckPositions)) return false;
            if (!TestUndergroundCheckPositions(blockAccessor, data.StartPos, schematic.UndergroundCheckPositions)) return false;
            if (!SatisfiesMinDistance(data.StartPos, data.World)) return false;
            if (WouldOverlapAt(blockAccessor, data.StartPos, schematic, data.StoryStructureLocationCode)) return false;

            LastPlacedSchematicLocation.Set(startPosX, data.StartPos.Y, startPosZ, startPosX + schematic.SizeX, data.StartPos.Y + schematic.SizeY, startPosZ + schematic.SizeZ);
            LastPlacedSchematic = schematic;
            data.ReplaceWithBlockLayersBlockids = replacewithblocklayersBlockids;
            data.ResolvedRockTypeRemaps = resolvedRockTypeRemaps;
            schematic.PlaceRespectingBlockLayers(data);
            return true;
        }



        internal bool TryGenerateUnderwater(SchematicPlacementData data)
        {
            return false;
        }

        internal bool TryGenerateUnderground(SchematicPlacementData data)
        {
            int num = rand.NextInt(schematicDatas.Length);

            BlockSchematicStructure[] schematicStruc = schematicDatas[num];
            schematicStruc[0].Unpack(data.World.Api, 0);

            if (schematicStruc[0].PathwayStarts.Length > 0)
            {
                return tryGenerateAttachedToCave(data, schematicStruc);
            }

            int orient = rand.NextInt(4);
            BlockSchematicStructure schematic = schematicStruc[orient];
            schematic.Unpack(data.World.Api, orient);
            schematic.AdjustStartPos(data.StartPos, Origin);
            var blockAccessor = data.BlockAccessor;

            if (insideblockids.Count > 0 && !insideblockids.Contains(blockAccessor.GetBlock(data.StartPos).Id)) return false;
            if (!TestUndergroundCheckPositions(blockAccessor, data.StartPos, schematic.UndergroundCheckPositions)) return false;
            if (!SatisfiesMinDistance(data.StartPos, data.World)) return false;
            if (WouldOverlapAt(blockAccessor, data.StartPos, schematic, data.StoryStructureLocationCode)) return false;

            LastPlacedSchematicLocation.Set(data.StartPos.X, data.StartPos.Y, data.StartPos.Z, data.StartPos.X + schematic.SizeX, data.StartPos.Y + schematic.SizeY, data.StartPos.Z + schematic.SizeZ);
            LastPlacedSchematic = schematic;


            data.ReplaceWithBlockLayersBlockids = replacewithblocklayersBlockids;
            data.ResolvedRockTypeRemaps = resolvedRockTypeRemaps;
            data.ReplaceMode = schematic.ReplaceMode;
            if (resolvedRockTypeRemaps != null)
            {
                Block rockBlock = null;

                for (int i = 0; rockBlock == null && i < 10; i++)
                {
                    var block = blockAccessor.GetBlockRaw(
                        data.StartPos.X + rand.NextInt(schematic.SizeX),
                        data.StartPos.Y + rand.NextInt(schematic.SizeY),
                        data.StartPos.Z + rand.NextInt(schematic.SizeZ),
                        BlockLayersAccess.Solid
                    );

                    if (block.BlockMaterial == EnumBlockMaterial.Stone) rockBlock = block;
                }

                data.ResolvedRockTypeRemaps = resolvedRockTypeRemaps;
                data.RockBlock = rockBlock;
                data.ReplaceMeta = GlobalConfig.ReplaceMetaBlocks;
                schematic.PlaceReplacingBlocks(data);
            } else
            {
                schematic.Place(data);
            }

            return true;
        }

        private bool tryGenerateAttachedToCave(SchematicPlacementData data, BlockSchematicStructure[] schematicStruc)
        {
            // 1. Give up if non air block or mapheight is not at least 4 blocks higher
            // 2. Search up to 4 blocks downwards. Give up if no stone is found.
            // 3. Select one pathway randomly
            // 4. For every horizontal orientation
            //    - Get the correctly rotated version for this pathway
            //    - Starting at 2 blocks away, move one block closer each iteration
            //      - Check if
            //        - at every pathway block pos there is stone or air
            //        - at least one pathway block has an air block facing towards center?
            //      - If yes, remove the blocks that are in the way and place schematic

            var blockAccessor = data.BlockAccessor;
            Block rockBlock = null;
            Block block = blockAccessor.GetBlock(data.StartPos);
            if (block.Id != 0) return false;

            // 1./2. Search an underground position that has air and a stone floor below
            bool found = false;
            for (int dy = 0; dy <= 4; dy++)
            {
                data.StartPos.Down();
                block = blockAccessor.GetBlock(data.StartPos);

                if (block.BlockMaterial == EnumBlockMaterial.Stone)
                {
                    rockBlock = block;
                    data.StartPos.Up();
                    found = true;
                    break;
                }
            }

            if (!found) return false;

            // 3. Random pathway
            BlockSchematicStructure schematic = schematicStruc[0];
            schematic.Unpack(data.World.Api, 0);
            int pathwayNum = rand.NextInt(schematic.PathwayStarts.Length);
            int targetDistance = -1;
            BlockFacing targetFacing = null;
            BlockPos[] pathway = null;

            // 4. At that position search for a suitable stone wall in any direction
            for (int targetOrientation = 0; targetOrientation < 4; targetOrientation++)
            {
                schematic = schematicStruc[targetOrientation];
                schematic.Unpack(data.World.Api, targetOrientation);
                // Try every rotation
                pathway = schematic.PathwayOffsets[pathwayNum];
                // This is the facing we are currently checking
                targetFacing = schematic.PathwaySides[pathwayNum];

                targetDistance = CanPlacePathwayAt(blockAccessor, pathway, targetFacing, data.StartPos);
                if (targetDistance != -1) break;
            }

            if (targetDistance == -1) return false;

            BlockPos pathwayStart = schematic.PathwayStarts[pathwayNum];

            // Move back the structure so that the door aligns to the cave wall
            data.StartPos.Add(
                -pathwayStart.X - targetFacing.Normali.X * targetDistance,
                -pathwayStart.Y - targetFacing.Normali.Y * targetDistance + schematic.OffsetY,
                -pathwayStart.Z - targetFacing.Normali.Z * targetDistance
            );

            if (data.StartPos.Y <= 0) return false;
            if (!TestUndergroundCheckPositions(blockAccessor, data.StartPos, schematic.UndergroundCheckPositions)) return false;
            if (WouldOverlapAt(blockAccessor, data.StartPos, schematic, data.StoryStructureLocationCode)) return false;

            LastPlacedSchematicLocation.Set(data.StartPos.X, data.StartPos.Y, data.StartPos.Z, data.StartPos.X + schematic.SizeX, data.StartPos.Y + schematic.SizeY, data.StartPos.Z + schematic.SizeZ);
            LastPlacedSchematic = schematic;

            data.ReplaceMode = schematic.ReplaceMode;
            if (resolvedRockTypeRemaps != null)
            {
                data.ResolvedRockTypeRemaps = resolvedRockTypeRemaps;
                data.RockBlock = rockBlock;
                data.ReplaceMeta = GlobalConfig.ReplaceMetaBlocks;
                schematic.PlaceReplacingBlocks(data);
            }
            else
            {
                schematic.Place(data);
            }

            // Free up a layer of blocks in front of the door
            ushort blockId = 0; // blockAccessor.GetBlock(new AssetLocation("creativeblock-37")).BlockId;
            for (int i = 0; i < pathway.Length; i++)
            {
                for (int d = 0; d <= targetDistance; d++)
                {
                    tmpPos.Set(
                        data.StartPos.X + pathwayStart.X + pathway[i].X + (d + 1) * targetFacing.Normali.X,
                        data.StartPos.Y + pathwayStart.Y + pathway[i].Y + (d + 1) * targetFacing.Normali.Y,
                        data.StartPos.Z + pathwayStart.Z + pathway[i].Z + (d + 1) * targetFacing.Normali.Z
                    );

                    blockAccessor.SetBlock(blockId, tmpPos);
                }
            }

            return true;
        }


        BlockPos utestPos = new BlockPos(Dimensions.NormalWorld);
        private bool TestUndergroundCheckPositions(IBlockAccessor blockAccessor, BlockPos pos, BlockPos[] testPositionsDelta)
        {
            for (int i = 0; i < testPositionsDelta.Length; i++)
            {
                BlockPos deltapos = testPositionsDelta[i];

                utestPos.Set(pos.X + deltapos.X, pos.Y + deltapos.Y, pos.Z + deltapos.Z);

                Block block = blockAccessor.GetBlock(utestPos);
                if (block.BlockMaterial != EnumBlockMaterial.Stone && block.BlockMaterial != EnumBlockMaterial.Soil && block.BlockMaterial != EnumBlockMaterial.Sand && block.BlockMaterial != EnumBlockMaterial.Gravel)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TestAboveGroundCheckPositions(IBlockAccessor blockAccessor, BlockPos pos, BlockPos[] testPositionsDelta)
        {
            for (int i = 0; i < testPositionsDelta.Length; i++)
            {
                BlockPos deltapos = testPositionsDelta[i];

                utestPos.Set(pos.X + deltapos.X, pos.Y + deltapos.Y, pos.Z + deltapos.Z);

                var height = blockAccessor.GetTerrainMapheightAt(utestPos);
                if (utestPos.Y <= height) return false;
            }

            return true;
        }

        private int CanPlacePathwayAt(IBlockAccessor blockAccessor, BlockPos[] pathway, BlockFacing towardsFacing, BlockPos targetPos)
        {
            int quantityAir;
            BlockPos tmpPos = new BlockPos(Dimensions.NormalWorld);

            bool oppositeDir = rand.NextInt(2) > 0;

            for (int i = 3; i >= 1; i--)
            {
                int dist = oppositeDir ? 3 - i : i;
                int dx = dist * towardsFacing.Normali.X;
                int dz = dist * towardsFacing.Normali.Z;

                quantityAir = 0;

                for (int k = 0; k < pathway.Length; k++)
                {
                    tmpPos.Set(targetPos.X + pathway[k].X + dx, targetPos.Y + pathway[k].Y, targetPos.Z + pathway[k].Z + dz);

                    Block block = blockAccessor.GetBlock(tmpPos);
                    if (block.Id == 0) quantityAir++;
                    else if (block.BlockMaterial != EnumBlockMaterial.Stone) return -1;
                }

                if (quantityAir > 0 && quantityAir < pathway.Length) return dist;
            }

            return -1;
        }


        static Cuboidi tmpLoc = new Cuboidi();

        private bool WouldOverlapAt(IBlockAccessor blockAccessor, BlockPos pos, BlockSchematicStructure schematic, string locationCode)
        {
            int regSize = blockAccessor.RegionSize;

            int mapRegionSizeX = blockAccessor.MapSizeX / regSize;
            int mapRegionSizeZ = blockAccessor.MapSizeZ / regSize;

            int minrx = GameMath.Clamp(pos.X / regSize, 0, mapRegionSizeX);
            int minrz = GameMath.Clamp(pos.Z / regSize, 0, mapRegionSizeZ);

            int maxrx = GameMath.Clamp((pos.X + schematic.SizeX) / regSize, 0, mapRegionSizeX);
            int maxrz = GameMath.Clamp((pos.Z + schematic.SizeZ) / regSize, 0, mapRegionSizeZ);


            tmpLoc.Set(pos.X, pos.Y, pos.Z, pos.X + schematic.SizeX, pos.Y + schematic.SizeY, pos.Z + schematic.SizeZ);
            for (int rx = minrx; rx <= maxrx; rx++)
            {
                for (int rz = minrz; rz <= maxrz; rz++)
                {
                    IMapRegion mapregion = blockAccessor.GetMapRegion(rx, rz);
                    if (mapregion == null) continue;
                    foreach (var val in mapregion.GeneratedStructures)
                    {
                        if (val.Location.Intersects(tmpLoc))
                        {
                            return true;
                        }
                    }
                }
            }

            if (genStructuresSys.WouldSchematicOverlapAt(blockAccessor, pos, tmpLoc, locationCode)) return true;

            return false;
        }

        public bool SatisfiesMinDistance(BlockPos pos, IWorldAccessor world)
        {
            return SatisfiesMinDistance(pos, world, MinGroupDistance, Group);
        }

        public static bool SatisfiesMinDistance(BlockPos pos, IWorldAccessor world, int mingroupDistance, string group)
        {
            if (mingroupDistance < 1) return true;

            int regSize = world.BlockAccessor.RegionSize;

            int mapRegionSizeX = world.BlockAccessor.MapSizeX / regSize;
            int mapRegionSizeZ = world.BlockAccessor.MapSizeZ / regSize;

            int x1 = pos.X - mingroupDistance;
            int z1 = pos.Z - mingroupDistance;
            int x2 = pos.X + mingroupDistance;
            int z2 = pos.Z + mingroupDistance;

            long minDistSq = (long)mingroupDistance * mingroupDistance;

            int minrx = GameMath.Clamp(x1 / regSize, 0, mapRegionSizeX);
            int minrz = GameMath.Clamp(z1 / regSize, 0, mapRegionSizeZ);

            int maxrx = GameMath.Clamp(x2 / regSize, 0, mapRegionSizeX);
            int maxrz = GameMath.Clamp(z2 / regSize, 0, mapRegionSizeZ);

            for (int rx = minrx; rx <= maxrx; rx++)
            {
                for (int rz = minrz; rz <= maxrz; rz++)
                {
                    IMapRegion mapregion = world.BlockAccessor.GetMapRegion(rx, rz);
                    if (mapregion == null) continue;

                    foreach (var val in mapregion.GeneratedStructures)
                    {
                        if (val.Group == group && val.Location.Center.SquareDistanceTo(pos.X, pos.Y, pos.Z) < minDistSq)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }
    }
}
