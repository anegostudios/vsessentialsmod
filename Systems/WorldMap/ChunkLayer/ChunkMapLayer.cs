using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    // We probably want to just transmit these maps as int[] blockids through the mapchunks (maybe rainheightmap suffices already?)
    // make a property block.BlockColor for the blocks color
    // and have the chunk intmap cached client side



    public class ChunkMapLayer : RGBMapLayer
    {
        int[] texDataTmp;
        int chunksize;
        IWorldChunk[] chunksTmp;

        object chunksToGenLock = new object();
        UniqueQueue<Vec2i> chunksToGen = new UniqueQueue<Vec2i>();
        Dictionary<Vec2i, MultiChunkMapComponent> loadedMapData = new Dictionary<Vec2i, MultiChunkMapComponent>();
        HashSet<Vec2i> curVisibleChunks = new HashSet<Vec2i>();

        public override MapLegendItem[] LegendItems => throw new NotImplementedException();
        public override EnumMinMagFilter MinFilter => EnumMinMagFilter.Linear;
        public override EnumMinMagFilter MagFilter => EnumMinMagFilter.Nearest;
        public override string Title => "Terrain";
        public override EnumMapAppSide DataSide => EnumMapAppSide.Client;


        MapDB mapdb;
        

        public string getMapDbFilePath()
        {
            string path = Path.Combine(GamePaths.DataPath, "Maps");
            GamePaths.EnsurePathExists(path);

            return Path.Combine(path, api.World.SavegameIdentifier + ".db");
        }


        public ChunkMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {
            api.Event.ChunkDirty += Event_OnChunkDirty;
            
            if (api.Side == EnumAppSide.Server)
            {
                (api as ICoreServerAPI).Event.DidPlaceBlock += Event_DidPlaceBlock;
            }

            if (api.Side == EnumAppSide.Client)
            {
                mapdb = new MapDB(api.World.Logger);
                string errorMessage = null;
                string mapdbfilepath = getMapDbFilePath();
                mapdb.OpenOrCreate(mapdbfilepath, ref errorMessage, true, true);
                if (errorMessage != null)
                {
                    throw new Exception(string.Format("Cannot open {0}, possibly corrupted. Please fix manually or delete this file to continue playing", mapdbfilepath));
                }
            }
        }

        private void Event_DidPlaceBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel, ItemStack withItemStack)
        {
            IMapChunk mapchunk = api.World.BlockAccessor.GetMapChunkAtBlockPos(blockSel.Position);
            if (mapchunk == null) return;

            int lx = blockSel.Position.X % chunksize;
            int lz = blockSel.Position.Z % chunksize;

            int y = mapchunk.RainHeightMap[lz * chunksize + lx];
            int ly = y % chunksize;


            IWorldChunk chunk = api.World.BlockAccessor.GetChunkAtBlockPos(blockSel.Position.X, y, blockSel.Position.Z);
            if (chunk == null) return;

            chunk.Unpack();
            int blockId = chunk.Blocks[(ly * chunksize + lz) * chunksize + lx];

            if (blockId == 0)
            {
                int cx = blockSel.Position.X / chunksize;
                int cz = blockSel.Position.Z / chunksize;
                api.World.Logger.Notification("Huh. Found air block in rain map at chunk pos {0}/{1}. That seems invalid, will regenerate rain map", cx, cz);
                rebuildRainmap(cx, cz);
            }
        }

        Vec2i tmpMccoord = new Vec2i();
        Vec2i tmpCoord = new Vec2i();

        private void Event_OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
        {
            if (reason == EnumChunkDirtyReason.NewlyCreated || !mapSink.IsOpened) return;

            tmpMccoord.Set(chunkCoord.X / MultiChunkMapComponent.ChunkLen, chunkCoord.Z / MultiChunkMapComponent.ChunkLen);
            tmpCoord.Set(chunkCoord.X, chunkCoord.Z);
            if (!loadedMapData.ContainsKey(tmpCoord) && !curVisibleChunks.Contains(tmpMccoord)) return;

            lock (chunksToGenLock)
            {
                chunksToGen.Enqueue(new Vec2i(chunkCoord.X, chunkCoord.Z));
            }
        }

        public override void OnLoaded()
        {
            chunksize = api.World.BlockAccessor.ChunkSize;
            texDataTmp = new int[chunksize * chunksize];
            chunksTmp = new IWorldChunk[api.World.BlockAccessor.MapSizeY / chunksize];
        }

        public override void OnMapClosedClient()
        {
            foreach (MapComponent cmp in loadedMapData.Values)
            {
                cmp.Dispose();
            }

            loadedMapData.Clear();
            lock (chunksToGenLock)
            {
                chunksToGen.Clear();
            }

            curVisibleChunks.Clear();
        }

        public override void Dispose()
        {
            if (loadedMapData != null)
            {
                foreach (MapComponent cmp in loadedMapData.Values)
                {
                    cmp?.Dispose();
                }
            }

            base.Dispose();
        }

        public override void OnShutDown()
        {
            MultiChunkMapComponent.tmpTexture?.Dispose();
            mapdb?.Dispose();
        }

        float accum = 0f;


        public override void OnOffThreadTick(float dt)
        {
            int quantityToGen = chunksToGen.Count;
            while (quantityToGen > 0)
            {
                quantityToGen--;
                Vec2i cord;

                lock (chunksToGenLock)
                {
                    if (chunksToGen.Count == 0) break;
                    cord = chunksToGen.Dequeue();
                }

                if (!api.World.BlockAccessor.IsValidPos(cord.X * chunksize, 1, cord.Y * chunksize)) continue;

                IMapChunk mc = api.World.BlockAccessor.GetMapChunk(cord);
                if (mc == null)
                {
                    MapPieceDB piece = mapdb.GetMapPiece(cord);
                    if (piece?.Pixels != null)
                    {
                        loadFromChunkPixels(cord, piece.Pixels);
                    }

                    continue;
                }

                int[] pixels = (int[])GenerateChunkImage(cord, mc)?.Clone();

                if (pixels == null)
                {
                    lock (chunksToGenLock)
                    {
                        chunksToGen.Enqueue(cord);
                    }
                    continue;
                }

                mapdb.SetMapPiece(cord, new MapPieceDB() { Pixels = pixels });

                loadFromChunkPixels(cord, pixels);
            }


            accum += dt;
            if (accum > 1)
            {
                foreach (var val in loadedMapData)
                {
                    MultiChunkMapComponent mcmp = val.Value;

                    if (!mcmp.AnyChunkSet)
                    {
                        mcmp.TTL -= 1;

                        if (mcmp.TTL <= 0)
                        {
                            Vec2i mccord = val.Key;

                            api.Event.EnqueueMainThreadTask(() =>
                            {
                                if (!mcmp.AnyChunkSet)
                                {
                                    mapSink.RemoveMapData(mcmp);
                                    loadedMapData.Remove(mccord);
                                    mcmp.Dispose();
                                }
                            }, "disposemcmcp");
                        }
                    } else
                    {
                        mcmp.TTL = MultiChunkMapComponent.MaxTTL;
                    }
                }

                accum = 0;
            }
        }


        void loadFromChunkPixels(Vec2i cord, int[] pixels)
        {
            api.Event.EnqueueMainThreadTask(() =>
            {
                Vec2i mcord = new Vec2i(cord.X / MultiChunkMapComponent.ChunkLen, cord.Y / MultiChunkMapComponent.ChunkLen);
                Vec2i baseCord = new Vec2i(mcord.X * MultiChunkMapComponent.ChunkLen, mcord.Y * MultiChunkMapComponent.ChunkLen);

                MultiChunkMapComponent mccomp;
                if (!loadedMapData.TryGetValue(mcord, out mccomp))
                {
                    loadedMapData[mcord] = mccomp = new MultiChunkMapComponent(api as ICoreClientAPI, baseCord);
                    mapSink.AddMapData(mccomp);
                }

                mccomp.setChunk(cord.X - baseCord.X, cord.Y - baseCord.Y, pixels);

            }, "chunkmaplayerready");
        }



        public override void OnViewChangedClient(List<Vec2i> nowVisible, List<Vec2i> nowHidden)
        {
            foreach (var val in nowVisible)
            {
                curVisibleChunks.Add(val);
            }

            foreach (var val in nowHidden)
            {
                curVisibleChunks.Remove(val);
            }

            lock (chunksToGenLock)
            {
                foreach (Vec2i cord in nowVisible)
                {
                    tmpMccoord.Set(cord.X / MultiChunkMapComponent.ChunkLen, cord.Y / MultiChunkMapComponent.ChunkLen);
                    MultiChunkMapComponent mcomp;

                    if (loadedMapData.TryGetValue(tmpMccoord, out mcomp))
                    {
                        int dx = cord.X % MultiChunkMapComponent.ChunkLen;
                        int dz = cord.Y % MultiChunkMapComponent.ChunkLen;
                        if (mcomp.IsChunkSet(dx, dz)) continue;
                    }

                    chunksToGen.Enqueue(cord.Copy());
                }
            }

            Vec2i mcord = new Vec2i();
            foreach (Vec2i cord in nowHidden)
            {
                MultiChunkMapComponent mc;
                
                mcord.Set(cord.X / MultiChunkMapComponent.ChunkLen, cord.Y / MultiChunkMapComponent.ChunkLen);

                if (loadedMapData.TryGetValue(mcord, out mc))
                {
                    mc.unsetChunk(cord.X % MultiChunkMapComponent.ChunkLen, cord.Y % MultiChunkMapComponent.ChunkLen);
                }
            }
        }

        


        public int[] GenerateChunkImage(Vec2i chunkPos, IMapChunk mc)
        {
            ICoreClientAPI capi = api as ICoreClientAPI;

            BlockPos tmpPos = new BlockPos();
            Vec2i localpos = new Vec2i();

            // Prefetch chunks
            for (int cy = 0; cy < chunksTmp.Length; cy++)
            {
                chunksTmp[cy] = capi.World.BlockAccessor.GetChunk(chunkPos.X, cy, chunkPos.Y);
                if (chunksTmp[cy] == null) return null;
            }

            // Prefetch map chunks
            IMapChunk[] mapChunks = new IMapChunk[]
            {
                capi.World.BlockAccessor.GetMapChunk(chunkPos.X - 1, chunkPos.Y - 1),
                capi.World.BlockAccessor.GetMapChunk(chunkPos.X - 1, chunkPos.Y),
                capi.World.BlockAccessor.GetMapChunk(chunkPos.X, chunkPos.Y - 1)            
            };

            //  bool didRegen = false;

            for (int i = 0; i < texDataTmp.Length; i++)
            {
                int y = mc.RainHeightMap[i];
                int cy = y / chunksize;
                if (cy >= chunksTmp.Length) continue;

                MapUtil.PosInt2d(i, chunksize, localpos);
                int lx = localpos.X;
                int lz = localpos.Y;

                float b = 1;
                int leftTop, rightTop, leftBot;

                IMapChunk leftTopMapChunk = mc;
                IMapChunk rightTopMapChunk = mc;
                IMapChunk leftBotMapChunk = mc;

                int topX = lx - 1;
                int botX = lx;
                int leftZ = lz - 1;
                int rightZ = lz;

                if (topX < 0 && leftZ < 0)
                {     
                    leftTopMapChunk = mapChunks[0];
                    rightTopMapChunk = mapChunks[1];
                    leftBotMapChunk = mapChunks[2];
                } else
                {
                    if (topX < 0)
                    {
                        leftTopMapChunk = mapChunks[1];
                        rightTopMapChunk = mapChunks[1];
                    }
                    if (leftZ < 0)
                    {
                        leftTopMapChunk = mapChunks[2];
                        leftBotMapChunk = mapChunks[2];
                    }
                }

                topX = GameMath.Mod(topX, chunksize);
                leftZ = GameMath.Mod(leftZ, chunksize);

                leftTop = leftTopMapChunk == null ? 0 : Math.Sign(y - leftTopMapChunk.RainHeightMap[leftZ * chunksize + topX]);
                rightTop = rightTopMapChunk == null ? 0 : Math.Sign(y - rightTopMapChunk.RainHeightMap[rightZ * chunksize + topX]);
                leftBot = leftBotMapChunk == null ? 0 : Math.Sign(y - leftBotMapChunk.RainHeightMap[leftZ * chunksize + botX]);

                float slopeness = (leftTop + rightTop + leftBot);

                if (slopeness > 0) b = 1.18f;
                if (slopeness < 0) b = 0.82f;

                chunksTmp[cy].Unpack();
                int blockId = chunksTmp[cy].Blocks[MapUtil.Index3d(localpos.X, y % chunksize, localpos.Y, chunksize, chunksize)];
                Block block = api.World.Blocks[blockId];

                tmpPos.Set(chunksize * chunkPos.X + localpos.X, y, chunksize * chunkPos.Y + localpos.Y);

                int avgCol = block.GetColor(capi, tmpPos);
                int rndCol = block.GetRandomColor(capi, tmpPos, BlockFacing.UP);

                // Add a bit of randomness to each pixel
                int col = ColorUtil.ColorOverlay(avgCol, rndCol, 0.2f);

                texDataTmp[i] = ColorUtil.ColorMultiply3Clamped(col, b) | 255 << 24;
            }

            for (int cy = 0; cy < chunksTmp.Length; cy++) chunksTmp[cy] = null;

            return texDataTmp;
        }


        
        

        void rebuildRainmap(int cx, int cz)
        {
            ICoreServerAPI sapi = api as ICoreServerAPI;

            int ymax = sapi.WorldManager.MapSizeY / sapi.WorldManager.ChunkSize;

            IServerChunk[] column = new IServerChunk[ymax];
            int chunksize = sapi.WorldManager.ChunkSize;

            IMapChunk mapchunk = null;

            for (int cy = 0; cy < ymax; cy++)
            {
                column[cy] = sapi.WorldManager.GetChunk(cx, cy, cz);
                column[cy]?.Unpack();

                mapchunk = column[cy]?.MapChunk;
            }

            if (mapchunk == null) return;

            for (int dx = 0; dx < chunksize; dx++)
            {
                for (int dz = 0; dz < chunksize; dz++)
                {
                    for (int dy = sapi.WorldManager.MapSizeY - 1; dy >= 0; dy--)
                    {
                        IServerChunk chunk = column[dy / chunksize];
                        if (chunk == null) continue;

                        int index = ((dy % chunksize) * chunksize + dz) * chunksize + dx;
                        Block block = sapi.World.Blocks[chunk.Blocks[index]];

                        if (!block.RainPermeable || dy == 0)
                        {
                            mapchunk.RainHeightMap[dz * chunksize + dx] = (ushort)dy;
                            break;
                        }
                    }
                }
            }


            sapi.WorldManager.ResendMapChunk(cx, cz, true);
            mapchunk.MarkDirty();
        }



    }
}
