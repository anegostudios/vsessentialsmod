using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods.NoObf;

#nullable disable

namespace Vintagestory.ServerMods
{
    /// <summary>
    /// A provider of story structures
    /// </summary>
    public interface IStructureProvider
    {
        /// <summary>
        /// Returns structure if should block generation of given category of blocks at this location (3D search)
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="category">Set to -1 to not check category</param>
        /// <returns></returns>
        //IStructureLocation GetBlockingStructureAt(BlockPos pos, int category);

        /// <summary>
        /// Returns structure if should deny generation of given category of blocks at this location. Ignores Y-Coordinate
        /// </summary>
        /// <param name="x"></param>
        /// <param name="z"></param>
        /// <param name="category">Set to -1 to not check category</param>
        /// <returns></returns>
        IStructureLocation GetBlockingStructureAt(int x, int z, int category);
    }


    public class ModSystemWorldGenStructures : ModSystem
    {
        /// <summary>
        /// Event triggered when structures are deleted from a region during regeneration.
        /// </summary>
        public event Action<List<GeneratedStructure>> OnStructuresDeleted;

        public void TriggerStructuresDeleted(List<GeneratedStructure> structures) => OnStructuresDeleted?.Invoke(structures);

        /// <summary>
        /// Event triggered when a chunk column regeneration is finalized.
        /// </summary>
        public event Action<int, int> OnRegenFinalized;

        public void TriggerRegenFinalized(int chunkX, int chunkZ) => OnRegenFinalized?.Invoke(chunkX, chunkZ);
        protected IStructureProvider[] providers;
        public override bool ShouldLoad(EnumAppSide forSide) => true;


        public override void StartPre(ICoreAPI api)
        {
            providers = Array.Empty<IStructureProvider>();
        }

        public void RegisterProvider(IStructureProvider provider)
        {
            providers = providers.Append(provider);
        }

        /// <summary>
        /// Checks weather the provided position is inside a story structures schematics for the specified skipCategory, or it's radius.
        /// If the Radius for the storystrcuture should be also checked is defined in the json as int as part of the skipGenerationCategories. Does only check 2D from story locations center.
        /// If the Radius at skipGenerationCategories is 0 then only the structures cuboid is checked.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="z"></param>
        /// <param name="category">The hashcode of the string category from storystructure.json skipGenerationCategories. The strings from storystructure.json are first converted to lowercase before getting the hash code.</param>
        /// <returns></returns>
        public IStructureLocation GetIntersectingStructure(int x, int z, int category)
        {
            for (int i = 0; i < providers.Length; i++)
            {
                var structure = providers[i].GetBlockingStructureAt(x, z, category);
                if (structure != null) return structure;
            }

            return null;
        }

    }

    public abstract class ModStdWorldGen : ModSystem
    {
        public GlobalConfig gcfg;
        protected const int chunksize = GlobalConstants.ChunkSize;

        public static int StructuresHashCode;
        public static int PatchesHashCode;
        public static int CavesHashCode;
        public static int TreesHashCode;
        public static int ShrubsHashCode;
        public static int StalagHashCode;
        public static int HotSpringsHashCode;
        public static int RivuletsHashCode;
        public static int PondsHashCode;
        public static int CreaturesHashCode;

        public static int[] AllHashCodes;

        static ModStdWorldGen()
        {
            StructuresHashCode = BitConverter.ToInt32(SHA256.HashData("structures"u8.ToArray()));
            PatchesHashCode = BitConverter.ToInt32(SHA256.HashData("patches"u8.ToArray()));
            CavesHashCode = BitConverter.ToInt32(SHA256.HashData("caves"u8.ToArray()));
            TreesHashCode = BitConverter.ToInt32(SHA256.HashData("trees"u8.ToArray()));
            ShrubsHashCode = BitConverter.ToInt32(SHA256.HashData("shrubs"u8.ToArray()));
            HotSpringsHashCode = BitConverter.ToInt32(SHA256.HashData("hotsprings"u8.ToArray()));
            RivuletsHashCode = BitConverter.ToInt32(SHA256.HashData("rivulets"u8.ToArray()));
            StalagHashCode = BitConverter.ToInt32(SHA256.HashData("stalag"u8.ToArray()));
            PondsHashCode = BitConverter.ToInt32(SHA256.HashData("pond"u8.ToArray()));
            CreaturesHashCode = BitConverter.ToInt32(SHA256.HashData("creatures"u8.ToArray()));

            AllHashCodes = new int[] { StructuresHashCode, PatchesHashCode, CavesHashCode, TreesHashCode, ShrubsHashCode, HotSpringsHashCode, RivuletsHashCode, StalagHashCode, PondsHashCode, CreaturesHashCode };
        }

        ModSystemWorldGenStructures provider;


        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            LoadGlobalConfig(api);
        }

        public void LoadGlobalConfig(ICoreServerAPI api)
        {
            gcfg = GlobalConfig.GetInstance(api);

            provider = api.ModLoader.GetModSystem<ModSystemWorldGenStructures>();
        }

        public IStructureLocation GetIntersectingStructure(int x, int z, int category)
        {
            return provider?.GetIntersectingStructure(x, z, category);
        }


        /// <summary>
        /// Checks weather the provided position is inside a story structures schematics for the specified skipCategory, or it's radius.
        /// If the Radius for the storystrcuture should be also checked is defined in the json as int as part of the skipGenerationCategories. Does only check 2D from story locations center.
        /// If the Radius at skipGenerationCategories is 0 then only the structures cuboid is checked.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public IStructureLocation GetIntersectingStructure(int x, int z)
        {
            return GetIntersectingStructure(x, z, -1);
        }

        /// <summary>
        /// Updates the heightmap on a chunk. Used for a postpass when we use PlacePartial for schematic which use raw access to set the blocks so they do not update the heightmap even with a heightmap updating WorldgenBlockAccessor. Used for Dungeons and story structures
        /// </summary>
        /// <param name="request"></param>
        /// <param name="worldGenBlockAccessor"></param>
        public static void UpdateHeightmap(IChunkColumnGenerateRequest request, IWorldGenBlockAccessor worldGenBlockAccessor)
        {
            var updatedPositionsT = 0;
            var updatedPositionsR = 0;

            var rainHeightMap = request.Chunks[0].MapChunk.RainHeightMap;
            var terrainHeightMap = request.Chunks[0].MapChunk.WorldGenTerrainHeightMap;
            for (int i = 0; i < rainHeightMap.Length; i++)
            {
                rainHeightMap[i] = 0;
                terrainHeightMap[i] = 0;
            }

            var mapSizeY = worldGenBlockAccessor.MapSizeY;
            var mapSize2D = chunksize * chunksize;
            for (int x = 0; x < chunksize; x++)
            {
                for (int z = 0; z < chunksize; z++)
                {
                    var mapIndex = z * chunksize + x;
                    bool rainSet = false;
                    bool heightSet = false;
                    for (int posY = mapSizeY - 1; posY >= 0; posY--)
                    {
                        var y = posY % chunksize;
                        var chunk = request.Chunks[posY / chunksize];
                        var chunkIndex = (y * chunksize + z) * chunksize + x;
                        var blockId = chunk.Data[chunkIndex];
                        if (blockId != 0)
                        {
                            var newBlock = worldGenBlockAccessor.GetBlock(blockId);
                            var newRainPermeable = newBlock.RainPermeable;
                            var newSolid = newBlock.SideSolid[BlockFacing.UP.Index];
                            if (!newRainPermeable && !rainSet)
                            {
                                rainSet = true;
                                rainHeightMap[mapIndex] = (ushort)posY;
                                updatedPositionsR++;
                            }

                            if (newSolid && !heightSet)
                            {
                                heightSet = true;
                                terrainHeightMap[mapIndex] = (ushort)posY;
                                updatedPositionsT++;
                            }

                            if (updatedPositionsR >= mapSize2D && updatedPositionsT >= mapSize2D)
                                return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Generates grass for the chunk column used to add grass to story locations like the tobias cave or dungeons
        /// </summary>
        /// <param name="api"></param>
        /// <param name="request"></param>
        /// <param name="grassRand"></param>
        /// <param name="genBlockLayers"></param>
        public void GenerateGrass(ICoreServerAPI api,IChunkColumnGenerateRequest request, LCGRandom grassRand, GenBlockLayers genBlockLayers)
        {
            var chunks = request.Chunks;
            int chunkX = request.ChunkX;
            int chunkZ = request.ChunkZ;

            grassRand.InitPositionSeed(chunkX, chunkZ);

            var forestMap = chunks[0].MapChunk.MapRegion.ForestMap;
            var climateMap = chunks[0].MapChunk.MapRegion.ClimateMap;

            ushort[] heightMap = chunks[0].MapChunk.RainHeightMap;

            int regionChunkSize = api.WorldManager.RegionSize / chunksize;
            int rdx = chunkX % regionChunkSize;
            int rdz = chunkZ % regionChunkSize;

            // Amount of data points per chunk
            float climateStep = (float)climateMap.InnerSize / regionChunkSize;
            float forestStep = (float)forestMap.InnerSize / regionChunkSize;

            // Retrieves the map data on the chunk edges
            int forestUpLeft = forestMap.GetUnpaddedInt((int)(rdx * forestStep), (int)(rdz * forestStep));
            int forestUpRight = forestMap.GetUnpaddedInt((int)(rdx * forestStep + forestStep), (int)(rdz * forestStep));
            int forestBotLeft = forestMap.GetUnpaddedInt((int)(rdx * forestStep), (int)(rdz * forestStep + forestStep));
            int forestBotRight = forestMap.GetUnpaddedInt((int)(rdx * forestStep + forestStep), (int)(rdz * forestStep + forestStep));

            var herePos = new BlockPos(Dimensions.NormalWorld);
            var mapheight = api.WorldManager.MapSizeY;

            for (int x = 0; x < chunksize; x++)
            {
                for (int z = 0; z < chunksize; z++)
                {
                    herePos.Set(chunkX * chunksize + x, 1, chunkZ * chunksize + z);
                    // Some weird randomnes stuff to hide fundamental bugs in the climate transition system :D T_T   (maybe not bugs but just fundamental shortcomings of using lerp on a very low resolution map)
                    int rnd = genBlockLayers.RandomlyAdjustPosition(herePos, out double distx, out double distz);

                    int posY = heightMap[z * chunksize + x];
                    if (posY >= mapheight) continue;

                    int climate = climateMap.GetUnpaddedColorLerped(
                        rdx * climateStep + climateStep * (x + (float)distx) / chunksize,
                        rdz * climateStep + climateStep * (z + (float)distz) / chunksize
                    );

                    int tempUnscaled = (climate >> 16) & 0xff;
                    float temp = Climate.GetScaledAdjustedTemperatureFloat(tempUnscaled, posY - TerraGenConfig.seaLevel + rnd);
                    float tempRel = Climate.GetAdjustedTemperature(tempUnscaled, posY - TerraGenConfig.seaLevel + rnd) / 255f;
                    float rainRel = Climate.GetRainFall((climate >> 8) & 0xff, posY + rnd) / 255f;
                    float forestRel = GameMath.BiLerp(forestUpLeft, forestUpRight, forestBotLeft, forestBotRight, (float)x / chunksize, (float)z / chunksize) / 255f;

                    int rocky = chunks[0].MapChunk.WorldGenTerrainHeightMap[z * chunksize + x];
                    int chunkY = rocky / chunksize;
                    int lY = rocky % chunksize;
                    int index3d = (chunksize * lY + z) * chunksize + x;

                    int rockblockID = chunks[chunkY].Data.GetBlockIdUnsafe(index3d);
                    var hereblock = api.World.Blocks[rockblockID];
                    if (hereblock.BlockMaterial != EnumBlockMaterial.Soil)
                    {
                        continue;
                    }
                    genBlockLayers.PlaceTallGrass(x, posY, z, chunks, rainRel, tempRel, temp, forestRel, 0);
                }
            }
        }
    }
}
