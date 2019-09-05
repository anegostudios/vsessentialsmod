using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
        Dictionary<Vec2i, MapComponent> loadedMapData = new Dictionary<Vec2i, MapComponent>();


        public override MapLegendItem[] LegendItems => throw new NotImplementedException();
        public override EnumMinMagFilter MinFilter => EnumMinMagFilter.Linear;
        public override EnumMinMagFilter MagFilter => EnumMinMagFilter.Nearest;
        public override string Title => "Terrain";
        public override EnumMapAppSide DataSide => EnumMapAppSide.Client;

        public ChunkMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {
            api.Event.ChunkDirty += Event_OnChunkDirty;
            if (api.Side == EnumAppSide.Server)
            {
                (api as ICoreServerAPI).Event.DidPlaceBlock += Event_DidPlaceBlock;
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

            
            int cy = y / chunksize;

            IWorldChunk chunk = api.World.BlockAccessor.GetChunkAtBlockPos(blockSel.Position.X, y, blockSel.Position.Z);
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

        private void Event_OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, bool isNewChunk)
        {
            if (isNewChunk || !mapSink.IsOpened) return;

            if (!loadedMapData.ContainsKey(new Vec2i(chunkCoord.X, chunkCoord.Z))) return;

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

        public override void OnOffThreadTick()
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

                IMapChunk mc = api.World.BlockAccessor.GetMapChunk(cord);
                if (mc == null)
                {
                    lock (chunksToGenLock)
                    {
                        chunksToGen.Enqueue(cord);
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

                api.Event.EnqueueMainThreadTask(() =>
                {
                    if (loadedMapData.ContainsKey(cord))
                    {
                        mapSink.RemoveMapData(loadedMapData[cord]);
                        loadedMapData[cord].Dispose();

                        //Console.WriteLine("disposed " + cord);
                    }

                    mapSink.AddMapData(loadedMapData[cord] = LoadMapData(cord, pixels));

                    //Console.WriteLine("generated " + cord);
                }, "chunkmaplayerready");
            }
        }

        public override void OnViewChangedClient(List<Vec2i> nowVisible, List<Vec2i> nowHidden)
        {
            lock (chunksToGenLock)
            {
                foreach (Vec2i cord in nowVisible)
                {
                    if (loadedMapData.ContainsKey(cord))
                    {
                        if (mapSink.RemoveMapData(loadedMapData[cord]))
                        {
                            // In case it got added twice due to race condition (which tends to happen)
                        }

                        mapSink.AddMapData(loadedMapData[cord]);
                        continue;
                    }

                    chunksToGen.Enqueue(cord.Copy());
                }
            }

            foreach (Vec2i cord in nowHidden)
            {
                MapComponent mc = null;
                if (loadedMapData.TryGetValue(cord, out mc))
                {
                    mapSink.RemoveMapData(mc);
                    loadedMapData.Remove(cord);
                    mc.Dispose();
                }

                //Console.WriteLine("removed " + cord);
            }
        }

        

        public MapComponent LoadMapData(Vec2i chunkCoord, int[] pixels)
        {
            ICoreClientAPI capi = api as ICoreClientAPI;
            int chunksize = api.World.BlockAccessor.ChunkSize;
            LoadedTexture tex = new LoadedTexture(capi, 0, chunksize, chunksize);

            capi.Render.LoadOrUpdateTextureFromRgba(pixels, false, 0, ref tex);
            
            ChunkMapComponent cmp = new ChunkMapComponent(capi, chunkCoord.Copy());
            cmp.Texture = tex;

            return cmp;
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

          //  bool didRegen = false;


            for (int i = 0; i < texDataTmp.Length; i++)
            {
                int y = mc.RainHeightMap[i];
                int cy = y / chunksize;
                if (cy >= chunksTmp.Length) continue;

                MapUtil.PosInt2d(i, chunksize, localpos);

                chunksTmp[cy].Unpack();
                int blockId = chunksTmp[cy].Blocks[MapUtil.Index3d(localpos.X, y % chunksize, localpos.Y, chunksize, chunksize)];
                Block block = api.World.Blocks[blockId];

                tmpPos.Set(chunksize * chunkPos.X + localpos.X, y, chunksize * chunkPos.Y + localpos.Y);
                
                texDataTmp[i] = block.GetColor(capi, tmpPos) | 255 << 24;
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
