using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Datastructures;

#nullable disable

namespace Vintagestory.ServerMods
{

    public class BlockSchematicPartial : BlockSchematicStructure
    {
        public List<Entity> EntitiesDecoded;

        static BlockPos Zero = new BlockPos(0, 0, 0);

        public virtual int PlacePartial(SchematicPlacementData data)
        {
            Unpack(data.World.Api);
            const int chunksize = GlobalConstants.ChunkSize;
            var rect = new Rectanglei(data.ChunkX * chunksize, data.ChunkZ * chunksize, chunksize, chunksize);

            if (!rect.IntersectsOrTouches(data.StartPos.X, data.StartPos.Z, data.StartPos.X + SizeX, data.StartPos.Z + SizeZ)) return 0;
            var placed = 0;

            var curPos = new BlockPos(data.StartPos.dimension);

            int climateUpLeft = 0, climateUpRight = 0, climateBotLeft = 0, climateBotRight = 0;
            if(data.ReplaceWithBlockLayersBlockids != null)
            {
                var regionChunkSize = data.BlockAccessor.RegionSize / chunksize;
                var region = data.Chunks[0].MapChunk.MapRegion;
                var climateMap = region.ClimateMap;
                var rlX = data.ChunkX % regionChunkSize;
                var rlZ = data.ChunkZ % regionChunkSize;
                var facC = (float)climateMap.InnerSize / regionChunkSize;
                climateUpLeft = climateMap.GetUnpaddedInt((int)(rlX * facC), (int)(rlZ * facC));
                climateUpRight = climateMap.GetUnpaddedInt((int)(rlX * facC + facC), (int)(rlZ * facC));
                climateBotLeft = climateMap.GetUnpaddedInt((int)(rlX * facC), (int)(rlZ * facC + facC));
                climateBotRight = climateMap.GetUnpaddedInt((int)(rlX * facC + facC), (int)(rlZ * facC + facC));
            }

            if (genBlockLayers == null) genBlockLayers = data.World.Api.ModLoader.GetModSystem<GenBlockLayers>();

            var rockblockid = data.RockBlock?.BlockId ?? data.Chunks[0].MapChunk.TopRockIdMap[15 * chunksize + 15];


            int i = -1;
            int dy, dx, dz;
            foreach (uint index in Indices)
            {
                i++;   // increment i first, because we have various continue statements

                dx = (int)(index & PosBitMask);
                int posX = data.StartPos.X + dx;
                dz = (int)((index >> 10) & PosBitMask);
                int posZ = data.StartPos.Z + dz;

                if (!rect.Contains(posX, posZ)) continue;
                dy = (int)((index >> 20) & PosBitMask);
                int posY = data.StartPos.Y + dy;
                int storedBlockid = BlockIds[i];
                AssetLocation blockCode = BlockCodes[storedBlockid];

                Block newBlock = data.BlockAccessor.GetBlock(blockCode);
                if (newBlock == null || (data.ReplaceMeta && (newBlock.Id == UndergroundBlockId || newBlock.Id == AbovegroundBlockId))) continue;

                int blockId = data.ReplaceMeta && IsFillerOrPath(newBlock) ? empty : newBlock.BlockId;

                IChunkBlocks chunkData = data.Chunks[posY / chunksize].Data;
                int posIndex = ((posY % chunksize) * chunksize + (posZ % chunksize)) * chunksize + (posX % chunksize);

                // update blocks that will be exposed to air to their top surface variant ex soil with grass
                if (data.StructurePlacement is EnumStructurePlacement.SurfaceRuin && newBlock.Id == FillerBlockId)
                {
                    var belowIndex = (uint)(((dy-1) << 20) | (dz << 10) | dx);
                    if (!Indices.Contains(belowIndex))
                    {
                        var belowPos = (((posY - 1) % chunksize) * chunksize + (posZ % chunksize)) * chunksize + (posX % chunksize);
                        var belowBlock = data.BlockAccessor.GetBlock(chunkData[belowPos]);

                        if (belowBlock.BlockMaterial == EnumBlockMaterial.Soil)
                        {
                            var belowBlockId = GetReplaceLayerBlockId(data.BlockAccessor, data.World, data.ReplaceWithBlockLayersBlockids, belowBlock.Id, curPos, posX, posY, posZ, chunksize, climateUpLeft, climateUpRight, climateBotLeft, climateBotRight, dy, dx, dz, rockblockid, ref belowBlock, true);

                            chunkData[belowPos] = belowBlockId;
                        }
                    }
                }

                blockId = GetReplaceLayerBlockId(data.BlockAccessor, data.World, data.ReplaceWithBlockLayersBlockids, blockId, curPos, posX, posY, posZ, chunksize, climateUpLeft, climateUpRight, climateBotLeft, climateBotRight, dy, dx, dz, rockblockid, ref newBlock);

                blockId = GetRocktypeBlockId(data.BlockAccessor, data.ResolvedRockTypeRemaps, blockId, rockblockid, ref newBlock);

                // surface sits on top of terrain and so needs to blend with existing one better
                // do not place grass or loose rocks below terrain if there was a solid blocks
                if (!data.DisableSurfaceTerrainBlending && data.StructurePlacement is EnumStructurePlacement.Surface)
                {
                    var oldBlock = data.BlockAccessor.GetBlock(chunkData[posIndex]);
                    // if we have solid blocks at the same pos keep the old one to blend in better with the terrain
                    if((newBlock.Replaceable >= 5500 || newBlock.BlockMaterial == EnumBlockMaterial.Plant) && oldBlock.Replaceable < newBlock.Replaceable && !newBlock.IsLiquid()) continue;
                }

                // if we only have a fluid block we need to clear the previous block so we can place fluids
                // schematics have solid block first and second fluid in the Indices array and the index (pos) is the same
                if (newBlock.ForFluidsLayer && index != Indices[i-1])
                {
                    chunkData[posIndex] = 0;
                }
                if (newBlock.ForFluidsLayer)
                {
                    chunkData.SetFluid(posIndex, blockId);
                }
                else
                {
                    chunkData.SetFluid(posIndex, 0);
                    chunkData[posIndex] = blockId;
                }

                if (newBlock.LightHsv[2] > 0 && data.BlockAccessor is IWorldGenBlockAccessor wgen)
                {
                    curPos.Set(posX, posY, posZ);
                    wgen.ScheduleBlockLightUpdate(curPos, 0, newBlock.BlockId);
                }

                placed++;
            }

            PlaceDecors(data, rect);

            int schematicSeed = data.World.Rand.Next();

            foreach (var val in BlockEntities)
            {
                uint index = val.Key;
                int posX = data.StartPos.X + (int)(index & PosBitMask);
                int posZ = data.StartPos.Z + (int)((index >> 10) & PosBitMask);

                if (!rect.Contains(posX, posZ)) continue;
                int posY = data.StartPos.Y + (int)((index >> 20) & PosBitMask);

                curPos.Set(posX, posY, posZ);
                BlockEntity be = data.BlockAccessor.GetBlockEntity(curPos);

                // Block entities need to be manually initialized for world gen block access
                if (be == null && data.BlockAccessor != null)
                {
                    Block block = data.BlockAccessor.GetBlock(curPos, BlockLayersAccess.Solid);

                    if (block.EntityClass != null)
                    {
                        data.BlockAccessor.SpawnBlockEntity(block.EntityClass, curPos);
                        be = data.BlockAccessor.GetBlockEntity(curPos);
                    }
                }

                if (be != null)
                {
                    Block block = data.BlockAccessor.GetBlock(curPos, BlockLayersAccess.Solid);
                    if (block.EntityClass != data.World.ClassRegistry.GetBlockEntityClass(be.GetType()))
                    {
                        data.World.Logger.Warning("Could not import block entity data for schematic at {0}. There is already {1}, expected {2}. Probably overlapping ruins.", curPos, be.GetType(), block.EntityClass);
                        continue;
                    }

                    ITreeAttribute tree = DecodeBlockEntityData(val.Value);
                    tree.SetInt("posx", curPos.X);
                    tree.SetInt("posy", curPos.Y);
                    tree.SetInt("posz", curPos.Z);

                    var climate = GameMath.BiLerpRgbColor(
                        GameMath.Clamp((posX % chunksize) / (float)chunksize, 0, 1),
                        GameMath.Clamp((posZ % chunksize) / (float)chunksize, 0, 1),
                        climateUpLeft, climateUpRight, climateBotLeft, climateBotRight
                    );
                    var layerBlock = GetBlockLayerBlock((climate >> 8) & 0xff, (climate >> 16) & 0xff, curPos.Y, rockblockid, 0, null,
                        data.World.Blocks, curPos, -1);

                    be.FromTreeAttributes(tree, data.World);
                    be.OnLoadCollectibleMappings(data.World, BlockCodes, ItemCodes, schematicSeed, data.ReplaceMeta);
                    be.OnPlacementBySchematic(data.World.Api as ICoreServerAPI, data.BlockAccessor, curPos, data.ResolvedRockTypeRemaps, rockblockid, layerBlock, data.ReplaceMeta);
                    if (data.Attributes != null)
                    {
                        data.AttributesDirty |= be.OnPlacementBySchematicCustom(data.World.Api as ICoreServerAPI, data.BlockAccessor, data.Attributes);
                    }
                }
            }

            // We need to decode entity each time since IWorldGenBlockAccessor does AddEntity and would add the same entity again. So the positon would be wrong for those "new" ones
            // if (EntitiesDecoded == null)
            DecodeEntities(data.World, data.StartPos, data.World);

            foreach (Entity entity in EntitiesDecoded)
            {
                if (rect.Contains((int)entity.Pos.X, (int)entity.Pos.Z))
                {
                    if (OriginalPos != null)
                    {
                        var prevOffset = entity.WatchedAttributes.GetBlockPos("importOffset", Zero);
                        entity.WatchedAttributes.SetBlockPos("importOffset", data.StartPos - OriginalPos + prevOffset);
                    }
                    // Not ideal but whatever
                    if (data.BlockAccessor is IWorldGenBlockAccessor wgen)
                    {
                        wgen.AddEntity(entity);
                        if (!entity.TryEarlyLoadCollectibleMappings(data.World, BlockCodes, ItemCodes, schematicSeed, data.ReplaceMeta))
                        {
                            entity.OnInitialized += () => entity.OnLoadCollectibleMappings(data.World, BlockCodes, ItemCodes, schematicSeed, data.ReplaceMeta);
                        }
                    }
                    else
                    {
                        entity.Attributes.SetBool("onFirstSpawn", false);
                        data.World.SpawnEntity(entity);
                        entity.OnLoadCollectibleMappings(data.World, BlockCodes, ItemCodes, schematicSeed, data.ReplaceMeta);
                    }
                }
            }

            if (ModData.Count > 0)
            {
                ((IServerEventAPI)data.World.Api.Event).TriggerSchematicImport(data.BlockAccessor, data.World, data.StartPos, ModData, rect);
            }

            return placed;
        }

        private int GetReplaceLayerBlockId(IBlockAccessor blockAccessor, IWorldAccessor worldForResolve, int[] replaceWithBlockLayersBlockids,
            int blockId, BlockPos curPos, int posX, int posY, int posZ, int chunksize, int climateUpLeft, int climateUpRight, int climateBotLeft,
            int climateBotRight, int dy, int dx, int dz, int rockblockid, ref Block newBlock, bool topBlockOnly = false)
        {
            if (replaceWithBlockLayersBlockids != null && replaceWithBlockLayersBlockids.Contains(blockId))
            {
                curPos.Set(posX, posY, posZ);
                var climate = GameMath.BiLerpRgbColor(
                    GameMath.Clamp((posX % chunksize) / (float)chunksize, 0, 1),
                    GameMath.Clamp((posZ % chunksize) / (float)chunksize, 0, 1),
                    climateUpLeft, climateUpRight, climateBotLeft, climateBotRight
                );
                var depth = 0;

                // if we are not the top block we need to get the layerblock for depth 1 -> soil without grass
                if (dy + 1 < SizeY && !topBlockOnly)
                {
                    var aboveBlock = blocksByPos[dx, dy + 1, dz];
                    if (aboveBlock != null && aboveBlock.SideSolid[BlockFacing.DOWN.Index] &&
                        aboveBlock.BlockMaterial != EnumBlockMaterial.Wood && aboveBlock.BlockMaterial != EnumBlockMaterial.Snow &&
                        aboveBlock.BlockMaterial != EnumBlockMaterial.Ice)
                    {
                        depth = 1;
                    }
                }

                var layerBlock = GetBlockLayerBlock((climate >> 8) & 0xff, (climate >> 16) & 0xff, curPos.Y - 1, rockblockid, depth, null,
                    worldForResolve.Blocks, curPos, -1);
                blockId = layerBlock?.Id ?? rockblockid;
                newBlock = blockAccessor.GetBlock(blockId);
            }

            return blockId;
        }

        private static int GetRocktypeBlockId(IBlockAccessor blockAccessor, Dictionary<int, Dictionary<int, int>> resolvedRockTypeRemaps, int blockId, int rockblockid,
            ref Block newBlock)
        {
            if (resolvedRockTypeRemaps != null)
            {
                if (resolvedRockTypeRemaps.TryGetValue(blockId, out Dictionary<int, int> replaceByBlock))
                {
                    if (replaceByBlock.TryGetValue(rockblockid, out var newBlockId))
                    {
                        blockId = newBlockId;
                        newBlock = blockAccessor.GetBlock(blockId);
                    }
                }
            }

            return blockId;
        }

        private void DecodeEntities(IWorldAccessor worldForResolve, BlockPos startPos, IServerWorldAccessor serverWorldAccessor)
        {
            EntitiesDecoded = new List<Entity>(Entities.Count);
            foreach (string entityData in Entities)
            {
                var reader = new BinarySpanReader(Ascii85.Decode(entityData));

                string className = reader.ReadString();
                Entity entity = worldForResolve.ClassRegistry.CreateEntity(className);
                entity.Api = worldForResolve.Api;   // FromBytes works better for some entities (e.g. EntityArmorStand) if the Api is already set
                entity.FromBytes(ref reader, false, serverWorldAccessor.RemappedEntities);
                entity.DidImportOrExport(startPos);

                EntitiesDecoded.Add(entity);
            }
        }

        public override BlockSchematic ClonePacked()
        {
            BlockSchematicPartial cloned = new BlockSchematicPartial();

            cloned.SizeX = SizeX;
            cloned.SizeY = SizeY;
            cloned.SizeZ = SizeZ;
            cloned.OffsetY = OffsetY;

            cloned.GameVersion = GameVersion;
            cloned.FromFile = FromFile;

            cloned.BlockCodes = new Dictionary<int, AssetLocation>(BlockCodes);
            cloned.ItemCodes = new Dictionary<int, AssetLocation>(ItemCodes);
            cloned.Indices = new List<uint>(Indices);
            cloned.BlockIds = new List<int>(BlockIds);

            cloned.BlockEntities = new Dictionary<uint, string>(BlockEntities);
            cloned.Entities = new List<string>(Entities);

            cloned.DecorIndices = new List<uint>(DecorIndices);
            cloned.DecorIds = new List<long>(DecorIds);

            cloned.ReplaceMode = ReplaceMode;
            cloned.EntranceRotation = EntranceRotation;
            cloned.OriginalPos = OriginalPos;

            cloned.ModData = ModData.ToDictionary(s => s.Key, s => new Dictionary<string, byte[]>(s.Value));

            return cloned;
        }
    }
}
