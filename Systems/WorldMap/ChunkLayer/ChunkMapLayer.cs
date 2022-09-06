using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
        
        int chunksize;
        IWorldChunk[] chunksTmp;

        object chunksToGenLock = new object();
        UniqueQueue<Vec2i> chunksToGen = new UniqueQueue<Vec2i>();
        ConcurrentDictionary<Vec2i, MultiChunkMapComponent> loadedMapData = new ConcurrentDictionary<Vec2i, MultiChunkMapComponent>();
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
            } else
            {
                (api as ICoreClientAPI).RegisterCommand("map", "world map stuff", "", onMapCmd);
            }

            if (api.Side == EnumAppSide.Client)
            {
                api.World.Logger.Notification("Loading world map cache db...");
                mapdb = new MapDB(api.World.Logger);
                string errorMessage = null;
                string mapdbfilepath = getMapDbFilePath();
                mapdb.OpenOrCreate(mapdbfilepath, ref errorMessage, true, true, false);
                if (errorMessage != null)
                {
                    throw new Exception(string.Format("Cannot open {0}, possibly corrupted. Please fix manually or delete this file to continue playing", mapdbfilepath));
                }
            }
        }

        private void onMapCmd(int groupId, CmdArgs args)
        {
            string cmd = args.PopWord();
            ICoreClientAPI capi = api as ICoreClientAPI;

            if (cmd == "purgedb")
            {
                mapdb.Purge();
                capi.ShowChatMessage("Ok, db purged");
            }

            if (cmd == "redraw")
            {
                foreach (MultiChunkMapComponent cmp in loadedMapData.Values)
                {
                    cmp.ActuallyDispose();
                }
                loadedMapData.Clear();

                lock (chunksToGenLock)
                {
                    foreach (Vec2i cord in curVisibleChunks)
                    {
                        chunksToGen.Enqueue(cord.Copy());
                    }
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

            int blockId = chunk.UnpackAndReadBlock((ly * chunksize + lz) * chunksize + lx, BlockLayersAccess.FluidOrSolid);

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
            //api.Logger.Notification("NowDirty @{0}/{1}", chunkCoord.X, chunkCoord.Z);

            lock (chunksToGenLock)
            {
                if (!mapSink.IsOpened) return;

                tmpMccoord.Set(chunkCoord.X / MultiChunkMapComponent.ChunkLen, chunkCoord.Z / MultiChunkMapComponent.ChunkLen);
                tmpCoord.Set(chunkCoord.X, chunkCoord.Z);

                if (!loadedMapData.ContainsKey(tmpMccoord) && !curVisibleChunks.Contains(tmpCoord)) return;

                chunksToGen.Enqueue(new Vec2i(chunkCoord.X, chunkCoord.Z));
                chunksToGen.Enqueue(new Vec2i(chunkCoord.X, chunkCoord.Z - 1));
                chunksToGen.Enqueue(new Vec2i(chunkCoord.X - 1, chunkCoord.Z));
                chunksToGen.Enqueue(new Vec2i(chunkCoord.X, chunkCoord.Z + 1));
                chunksToGen.Enqueue(new Vec2i(chunkCoord.X + 1, chunkCoord.Z + 1));
            }
        }

        public override void OnLoaded()
        {
            chunksize = api.World.BlockAccessor.ChunkSize;
            
            chunksTmp = new IWorldChunk[api.World.BlockAccessor.MapSizeY / chunksize];
        }

        public override void OnMapOpenedClient()
        {

        }

        public override void OnMapClosedClient()
        {

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
                foreach (MultiChunkMapComponent cmp in loadedMapData.Values)
                {
                    cmp?.ActuallyDispose();
                }
            }

            MultiChunkMapComponent.DisposeStatic();

            base.Dispose();
        }

        public override void OnShutDown()
        {
            MultiChunkMapComponent.tmpTexture?.Dispose();
            mapdb?.Dispose();
        }

        float mtThread1secAccum = 0f;

        float genAccum = 0f;
        float diskSaveAccum = 0f;
        Dictionary<Vec2i, MapPieceDB> toSaveList = new Dictionary<Vec2i, MapPieceDB>();

        public override void OnOffThreadTick(float dt)
        {
            genAccum += dt;
            if (genAccum < 0.1) return;
            genAccum = 0;

            int quantityToGen = chunksToGen.Count;
            while (quantityToGen > 0)
            {
                if (mapSink.IsShuttingDown) break;

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
                    try
                    {
                        MapPieceDB piece = mapdb.GetMapPiece(cord);
                        if (piece?.Pixels != null)
                        {
                            loadFromChunkPixels(cord, piece.Pixels);
                        }
                    } catch (ProtoBuf.ProtoException)
                    {
                        api.Logger.Warning("Failed loading map db section {0}/{1}, a protobuf exception was thrown. Will ignore.", cord.X, cord.Y);
                    }
                    catch (OverflowException)
                    {
                        api.Logger.Warning("Failed loading map db section {0}/{1}, a overflow exception was thrown. Will ignore.", cord.X, cord.Y);
                    }

                    continue;
                }

                //api.Logger.Notification("Genpixels @{0}/{1}", cord.X, cord.Y);

                int[] pixels = (int[])GenerateChunkImage(cord, mc)?.Clone();

                if (pixels == null)
                {
                    lock (chunksToGenLock)
                    {
                        chunksToGen.Enqueue(cord);
                    }

                    continue;
                }

                toSaveList[cord.Copy()] = new MapPieceDB() { Pixels = pixels };


                /// North: Negative Z
                /// East: Positive X
                /// South: Positive Z
                /// West: Negative X
                /*bool north = !api.World.LoadedMapChunkIndices.Contains(MapUtil.Index2dL(cord.X, cord.Y - 1, chunkmapSizeX));
                bool east = !api.World.LoadedMapChunkIndices.Contains(MapUtil.Index2dL(cord.X + 1, cord.Y, chunkmapSizeX));
                bool south = !api.World.LoadedMapChunkIndices.Contains(MapUtil.Index2dL(cord.X, cord.Y + 1, chunkmapSizeX));
                bool west = !api.World.LoadedMapChunkIndices.Contains(MapUtil.Index2dL(cord.X - 1, cord.Y, chunkmapSizeX));*/

                loadFromChunkPixels(cord, pixels/*, (north ? 1 : 0 ) | (east ? 2 : 0) | (south ? 4 : 0) | (west ? 8 : 0)*/);
            }

            if (toSaveList.Count > 100 || diskSaveAccum > 4f)
            {
                diskSaveAccum = 0;
                mapdb.SetMapPieces(toSaveList);
                toSaveList.Clear();
            }
        }



        public override void OnTick(float dt)
        {
            mtThread1secAccum += dt;
            if (mtThread1secAccum > 1)
            {
                List<Vec2i> toRemove = new List<Vec2i>();

                foreach (var val in loadedMapData)
                {
                    MultiChunkMapComponent mcmp = val.Value;

                    if (!mcmp.AnyChunkSet || !mcmp.IsVisible(curVisibleChunks))
                    {
                        mcmp.TTL -= 1;

                        if (mcmp.TTL <= 0)
                        {
                            Vec2i mccord = val.Key;
                            toRemove.Add(mccord);
                            mcmp.ActuallyDispose();
                        }
                    }
                    else
                    {
                        mcmp.TTL = MultiChunkMapComponent.MaxTTL;
                    }
                }

                foreach (var val in toRemove)
                {
                    loadedMapData.TryRemove(val, out _);
                }

                mtThread1secAccum = 0;
            }
        }

        public override void Render(GuiElementMap mapElem, float dt)
        {
            foreach (var val in loadedMapData)
            {
                val.Value.Render(mapElem, dt);
            }
        }

        public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {
            foreach (var val in loadedMapData)
            {
                val.Value.OnMouseMove(args, mapElem, hoverText);
            }
        }

        public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
        {
            foreach (var val in loadedMapData)
            {
                val.Value.OnMouseUpOnElement(args, mapElem);
            }
        }

        void loadFromChunkPixels(Vec2i cord, int[] pixels/*, int sideSepia*/)
        {
            /*if (sideSepia == 255)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    int pixel = pixels[i];
                    int r = pixel & 0xff;
                    int g = (pixel >> 8) & 0xff;
                    int b = (pixel >> 16) & 0xff;

                    // Sepia
                    byte rs = (byte)Math.Min(255, (r * 0.393f) + (g * 0.769f) + (b * 0.189f));
                    byte gs = (byte)Math.Min(255, (r * 0.349f) + (g * 0.686f) + (b * 0.168f));
                    byte bs = (byte)Math.Min(255, (r * 0.272f) + (g * 0.534f) + (b * 0.131f));

                    pixels[i] = (rs) | (gs << 8) | (bs << 16) | (255 << 24);
                }
            }
            else if (sideSepia != 0)
            {
                bool northSepia = (sideSepia & 1) > 0;
                bool eastSepia = (sideSepia & 2) > 0;
                bool southSepia = (sideSepia & 4) > 0;
                bool westSepia = (sideSepia & 8) > 0;

                for (int i = 0; i < pixels.Length; i++)
                {
                    int pixel = pixels[i];
                    int r = pixel & 0xff;
                    int g = (pixel >> 8) & 0xff;
                    int b = (pixel >> 16) & 0xff;

                    int x = i % chunksize;
                    int z = i / chunksize;

                    int rnd = GameMath.MurmurHash3Mod(x, 0, z, chunksize / 2);

                    /// North: Negative Z
                    /// East: Positive X
                    /// South: Positive Z
                    /// West: Negative X
                    bool sepia =
                        northSepia && rnd > z ||
                        eastSepia && rnd > chunksize - x ||
                        southSepia && rnd > chunksize - z ||
                        westSepia && rnd > x
                    ;

                    if (!sepia) continue;

                    // Sepia
                    byte rs = (byte)Math.Min(255, (r * 0.393f) + (g * 0.769f) + (b * 0.189f));
                    byte gs = (byte)Math.Min(255, (r * 0.349f) + (g * 0.686f) + (b * 0.168f));
                    byte bs = (byte)Math.Min(255, (r * 0.272f) + (g * 0.534f) + (b * 0.131f));

                    pixels[i] = (rs) | (gs << 8) | (bs << 16) | (255 << 24);
                }
            }*/

            Vec2i mcord = new Vec2i(cord.X / MultiChunkMapComponent.ChunkLen, cord.Y / MultiChunkMapComponent.ChunkLen);
            Vec2i baseCord = new Vec2i(mcord.X * MultiChunkMapComponent.ChunkLen, mcord.Y * MultiChunkMapComponent.ChunkLen);

            api.Event.EnqueueMainThreadTask(() =>
            {
                MultiChunkMapComponent mccomp;
                if (!loadedMapData.TryGetValue(mcord, out mccomp))
                {
                    loadedMapData[mcord] = mccomp = new MultiChunkMapComponent(api as ICoreClientAPI, baseCord);
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

                    int dx = cord.X % MultiChunkMapComponent.ChunkLen;
                    int dz = cord.Y % MultiChunkMapComponent.ChunkLen;
                    if (dx < 0 || dz < 0) continue;

                    if (loadedMapData.TryGetValue(tmpMccoord, out mcomp))
                    {
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

                if (cord.X < 0 || cord.Y < 0) continue;

                if (loadedMapData.TryGetValue(mcord, out mc))
                {
                    mc.unsetChunk(cord.X % MultiChunkMapComponent.ChunkLen, cord.Y % MultiChunkMapComponent.ChunkLen);
                }
            }
        }

        


        public int[] GenerateChunkImage(Vec2i chunkPos, IMapChunk mc, bool withSnow = true)
        {
            ICoreClientAPI capi = api as ICoreClientAPI;

            BlockPos tmpPos = new BlockPos();
            Vec2i localpos = new Vec2i();

            // Prefetch chunks
            for (int cy = 0; cy < chunksTmp.Length; cy++)
            {
                chunksTmp[cy] = capi.World.BlockAccessor.GetChunk(chunkPos.X, cy, chunkPos.Y);
                if (chunksTmp[cy] == null || !(chunksTmp[cy] as IClientChunk).LoadedFromServer) return null;
            }

            // Prefetch map chunks
            IMapChunk[] mapChunks = new IMapChunk[]
            {
                capi.World.BlockAccessor.GetMapChunk(chunkPos.X - 1, chunkPos.Y - 1),
                capi.World.BlockAccessor.GetMapChunk(chunkPos.X - 1, chunkPos.Y),
                capi.World.BlockAccessor.GetMapChunk(chunkPos.X, chunkPos.Y - 1)            
            };

            int[] texDataTmp = new int[chunksize * chunksize];
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

                int blockId = chunksTmp[cy].UnpackAndReadBlock(MapUtil.Index3d(localpos.X, y % chunksize, localpos.Y, chunksize, chunksize), BlockLayersAccess.FluidOrSolid);
                Block block = api.World.Blocks[blockId];

                if (!withSnow && block.BlockMaterial == EnumBlockMaterial.Snow)
                {
                    y--;
                    cy = y / chunksize;
                    blockId = chunksTmp[cy].UnpackAndReadBlock(MapUtil.Index3d(localpos.X, y % chunksize, localpos.Y, chunksize, chunksize), BlockLayersAccess.FluidOrSolid);
                    block = api.World.Blocks[blockId];
                }

                tmpPos.Set(chunksize * chunkPos.X + localpos.X, y, chunksize * chunkPos.Y + localpos.Y);


                int avgCol = block.GetColor(capi, tmpPos);
                int rndCol = block.GetRandomColor(capi, tmpPos, BlockFacing.UP, GameMath.MurmurHash3Mod(tmpPos.X, tmpPos.Y, tmpPos.Z, 30));
                // Why the eff is r and b flipped
                rndCol = ((rndCol & 0xff) << 16) | (((rndCol >> 8) & 0xff) << 8) | (((rndCol>>16) & 0xff) << 0);

                // Add a bit of randomness to each pixel
                int col = ColorUtil.ColorOverlay(avgCol, rndCol, 0.6f);

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
                column[cy]?.Unpack_ReadOnly();

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
                        Block block = sapi.World.Blocks[chunk.Data.GetBlockId(index, BlockLayersAccess.FluidOrSolid)];

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
