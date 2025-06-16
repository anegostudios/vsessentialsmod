using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{

    public class ReadyMapPiece {
        public int[] Pixels;
        public FastVec2i Cord;
    }

    // We probably want to just transmit these maps as int[] blockids through the mapchunks (maybe rainheightmap suffices already?)
    // make a property block.BlockColor for the blocks color
    // and have the chunk intmap cached client side
    public class ChunkMapLayer : RGBMapLayer
    {
        public static Dictionary<EnumBlockMaterial, string> defaultMapColorCodes = new Dictionary<EnumBlockMaterial, string>()
        {
            { EnumBlockMaterial.Soil, "land" },
            { EnumBlockMaterial.Sand, "desert" },
            { EnumBlockMaterial.Ore, "land" },
            { EnumBlockMaterial.Gravel, "desert" },
            { EnumBlockMaterial.Stone, "land" },
            { EnumBlockMaterial.Leaves, "forest" },
            { EnumBlockMaterial.Plant, "plant" },
            { EnumBlockMaterial.Wood, "forest" },
            { EnumBlockMaterial.Snow, "glacier" },
            { EnumBlockMaterial.Liquid, "lake" },
            { EnumBlockMaterial.Ice, "glacier" },
            { EnumBlockMaterial.Lava, "lava" }
        };

        public static API.Datastructures.OrderedDictionary<string, string> hexColorsByCode = new ()
        {
            { "ink", "#483018" },
            { "settlement", "#856844" },
            { "wateredge", "#483018" },
            { "land", "#AC8858" },
            { "desert", "#C4A468" },
            { "forest", "#98844C" },
            { "road", "#805030" },
            { "plant", "#808650" },
            { "lake", "#CCC890" },
            { "ocean", "#CCC890" },
            { "glacier", "#E0E0C0" },
            { "devastation", "#755c3c" }
        };

        public API.Datastructures.OrderedDictionary<string, int> colorsByCode = new () {};
        int[] colors;

        public byte[] block2Color;

        const int chunksize = GlobalConstants.ChunkSize;
        IWorldChunk[] chunksTmp;

        object chunksToGenLock = new object();
        UniqueQueue<FastVec2i> chunksToGen = new ();
        ConcurrentDictionary<FastVec2i, MultiChunkMapComponent> loadedMapData = new ();
        HashSet<FastVec2i> curVisibleChunks = new ();

        ConcurrentQueue<ReadyMapPiece> readyMapPieces = new ConcurrentQueue<ReadyMapPiece>();

        public override MapLegendItem[] LegendItems => throw new NotImplementedException();
        public override EnumMinMagFilter MinFilter => EnumMinMagFilter.Linear;
        public override EnumMinMagFilter MagFilter => EnumMinMagFilter.Nearest;
        public override string Title => "Terrain";
        public override EnumMapAppSide DataSide => EnumMapAppSide.Client;

        public override string LayerGroupCode => "terrain";

        MapDB mapdb;
        ICoreClientAPI capi;

        bool colorAccurate;

        public string getMapDbFilePath()
        {
            string path = Path.Combine(GamePaths.DataPath, "Maps");
            GamePaths.EnsurePathExists(path);

            return Path.Combine(path, api.World.SavegameIdentifier + ".db");
        }

        

        public ChunkMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {
            foreach (var val in hexColorsByCode)
            {
                colorsByCode[val.Key] = ColorUtil.ReverseColorBytes(ColorUtil.Hex2Int(val.Value));
            }

            api.Event.ChunkDirty += Event_OnChunkDirty;
            capi = api as ICoreClientAPI;

            if (api.Side == EnumAppSide.Server)
            {
                (api as ICoreServerAPI).Event.DidPlaceBlock += Event_DidPlaceBlock;
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
                
                api.ChatCommands.GetOrCreate("map")
                    .BeginSubCommand("purgedb")
                        .WithDescription("purge the map db")
                        .HandleWith(_ =>
                        {
                            mapdb.Purge();
                            return TextCommandResult.Success("Ok, db purged");
                        })
                    .EndSubCommand()
                    .BeginSubCommand("redraw")
                        .WithDescription("Redraw the map")
                        .HandleWith(OnMapCmdRedraw)
                    .EndSubCommand();
            }
        }

        private TextCommandResult OnMapCmdRedraw(TextCommandCallingArgs args)
        {
            foreach (MultiChunkMapComponent cmp in loadedMapData.Values)
            {
                cmp.ActuallyDispose();
            }
            loadedMapData.Clear();

            lock (chunksToGenLock)
            {
                foreach (FastVec2i cord in curVisibleChunks)
                {
                    chunksToGen.Enqueue(cord.Copy());
                }
            }
            return TextCommandResult.Success("Redrawing map...");
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

        private void Event_OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
        {
            lock (chunksToGenLock)
            {
                if (!mapSink.IsOpened) return;

                FastVec2i tmpMccoord = new FastVec2i(chunkCoord.X / MultiChunkMapComponent.ChunkLen, chunkCoord.Z / MultiChunkMapComponent.ChunkLen);
                FastVec2i tmpCoord = new FastVec2i(chunkCoord.X, chunkCoord.Z);

                if (!loadedMapData.ContainsKey(tmpMccoord) && !curVisibleChunks.Contains(tmpCoord)) return;

                chunksToGen.Enqueue(new FastVec2i(chunkCoord.X, chunkCoord.Z));
                chunksToGen.Enqueue(new FastVec2i(chunkCoord.X, chunkCoord.Z - 1));
                chunksToGen.Enqueue(new FastVec2i(chunkCoord.X - 1, chunkCoord.Z));
                chunksToGen.Enqueue(new FastVec2i(chunkCoord.X, chunkCoord.Z + 1));
                chunksToGen.Enqueue(new FastVec2i(chunkCoord.X + 1, chunkCoord.Z + 1));
            }
        }


        public override void OnLoaded()
        {
            if (api.Side == EnumAppSide.Server) return;
            chunksTmp = new IWorldChunk[api.World.BlockAccessor.MapSizeY / chunksize];

            colors = new int[colorsByCode.Count];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = colorsByCode.GetValueAtIndex(i);
            }

            var blocks = api.World.Blocks;
            block2Color = new byte[blocks.Count];
            for (int i = 0; i < block2Color.Length; i++)
            {
                var block = blocks[i];
                string colorcode = "land";
                if (block?.Attributes != null)
                {
                    colorcode = block.Attributes["mapColorCode"].AsString();
                    if (colorcode == null)
                    {
                        if (!defaultMapColorCodes.TryGetValue(block.BlockMaterial, out colorcode))
                        {
                            colorcode = "land";
                        }
                    }
                }

                block2Color[i] = (byte)colorsByCode.IndexOfKey(colorcode);
                if (colorsByCode.IndexOfKey(colorcode) < 0)
                {
                    throw new Exception("No color exists for color code " + colorcode);
                }
            }
        }

        public override void OnMapOpenedClient()
        {
            colorAccurate = api.World.Config.GetAsBool("colorAccurateWorldmap", false) || (capi.World.Player.Privileges.IndexOf("colorAccurateWorldmap") != -1);
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
        Dictionary<FastVec2i, MapPieceDB> toSaveList = new Dictionary<FastVec2i, MapPieceDB>();

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
                FastVec2i cord;

                lock (chunksToGenLock)
                {
                    if (chunksToGen.Count == 0) break;
                    cord = chunksToGen.Dequeue();
                }

                if (!api.World.BlockAccessor.IsValidPos(cord.X * chunksize, 1, cord.Y * chunksize)) continue;

                IMapChunk mc = api.World.BlockAccessor.GetMapChunk(cord.X, cord.Y);
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

                int[] tintedPixels = GenerateChunkImage(cord, mc, colorAccurate);
                if (tintedPixels == null)
                {
                    lock (chunksToGenLock)
                    {
                        chunksToGen.Enqueue(cord);
                    }

                    continue;
                }

                toSaveList[cord.Copy()] = new MapPieceDB() { Pixels = tintedPixels };

                loadFromChunkPixels(cord, tintedPixels);
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
            if (!readyMapPieces.IsEmpty)
            {
                int q = Math.Min(readyMapPieces.Count, 200);
                List<MultiChunkMapComponent> modified = new();
                while (q-- > 0)
                {
                    if (readyMapPieces.TryDequeue(out var mappiece))
                    {
                        FastVec2i mcord = new FastVec2i(mappiece.Cord.X / MultiChunkMapComponent.ChunkLen, mappiece.Cord.Y / MultiChunkMapComponent.ChunkLen);
                        FastVec2i baseCord = new FastVec2i(mcord.X * MultiChunkMapComponent.ChunkLen, mcord.Y * MultiChunkMapComponent.ChunkLen);

                        if (!loadedMapData.TryGetValue(mcord, out MultiChunkMapComponent mccomp))
                        {
                            loadedMapData[mcord] = mccomp = new MultiChunkMapComponent(api as ICoreClientAPI, baseCord);
                        }

                        mccomp.setChunk(mappiece.Cord.X - baseCord.X, mappiece.Cord.Y - baseCord.Y, mappiece.Pixels);
                        modified.Add(mccomp);
                    }
                }

                foreach (var mccomp in modified) mccomp.FinishSetChunks();
            }

            mtThread1secAccum += dt;
            if (mtThread1secAccum > 1)
            {
                List<FastVec2i> toRemove = new List<FastVec2i>();

                foreach (var val in loadedMapData)
                {
                    MultiChunkMapComponent mcmp = val.Value;

                    if (!mcmp.AnyChunkSet || !mcmp.IsVisible(curVisibleChunks))
                    {
                        mcmp.TTL -= 1;

                        if (mcmp.TTL <= 0)
                        {
                            FastVec2i mccord = val.Key;
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
            if (!Active) return;

            foreach (var val in loadedMapData)
            {
                val.Value.Render(mapElem, dt);
            }
        }

        public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {
            if (!Active) return;

            foreach (var val in loadedMapData)
            {
                val.Value.OnMouseMove(args, mapElem, hoverText);
            }
        }

        public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
        {
            if (!Active) return;

            foreach (var val in loadedMapData)
            {
                val.Value.OnMouseUpOnElement(args, mapElem);
            }
        }

        void loadFromChunkPixels(FastVec2i cord, int[] pixels)
        {
            readyMapPieces.Enqueue(new ReadyMapPiece() { Pixels = pixels, Cord = cord });            
        }



        public override void OnViewChangedClient(List<FastVec2i> nowVisible, List<FastVec2i> nowHidden)
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
                foreach (FastVec2i cord in nowVisible)
                {
                    FastVec2i tmpMccoord = new FastVec2i(cord.X / MultiChunkMapComponent.ChunkLen, cord.Y / MultiChunkMapComponent.ChunkLen);

                    int dx = cord.X % MultiChunkMapComponent.ChunkLen;
                    int dz = cord.Y % MultiChunkMapComponent.ChunkLen;
                    if (dx < 0 || dz < 0) continue;

                    if (loadedMapData.TryGetValue(tmpMccoord, out MultiChunkMapComponent mcomp))
                    {
                        if (mcomp.IsChunkSet(dx, dz)) continue;
                    }

                    chunksToGen.Enqueue(cord.Copy());
                }
            }

            foreach (FastVec2i cord in nowHidden)
            {
                if (cord.X < 0 || cord.Y < 0) continue;

                FastVec2i mcord = new FastVec2i(cord.X / MultiChunkMapComponent.ChunkLen, cord.Y / MultiChunkMapComponent.ChunkLen);

                if (loadedMapData.TryGetValue(mcord, out MultiChunkMapComponent mc))
                {
                    mc.unsetChunk(cord.X % MultiChunkMapComponent.ChunkLen, cord.Y % MultiChunkMapComponent.ChunkLen);
                }
            }
        }



        private static bool isLake(Block block)
        {
            return block.BlockMaterial == EnumBlockMaterial.Liquid || (block.BlockMaterial == EnumBlockMaterial.Ice && block.Code.Path != "glacierice");
        }

        [ThreadStatic]
        static byte[] shadowMapReusable;
        [ThreadStatic]
        static byte[] shadowMapBlurReusable;
        public int[] GenerateChunkImage(FastVec2i chunkPos, IMapChunk mc, bool colorAccurate = false)
        {
            BlockPos tmpPos = new BlockPos();
            Vec2i localpos = new Vec2i();

            // Prefetch chunks
            for (int cy = 0; cy < chunksTmp.Length; cy++)
            {
                chunksTmp[cy] = capi.World.BlockAccessor.GetChunk(chunkPos.X, cy, chunkPos.Y);
                if (chunksTmp[cy] == null || !(chunksTmp[cy] as IClientChunk).LoadedFromServer) return null;
            }

            int[] tintedImage = new int[chunksize * chunksize];

            // Prefetch map chunks
            IMapChunk chunkNeibNW = capi.World.BlockAccessor.GetMapChunk(chunkPos.X - 1, chunkPos.Y - 1);
            IMapChunk chunkNeibW = capi.World.BlockAccessor.GetMapChunk(chunkPos.X - 1, chunkPos.Y);
            IMapChunk chunkNeibN = capi.World.BlockAccessor.GetMapChunk(chunkPos.X, chunkPos.Y - 1);

            shadowMapReusable ??= new byte[tintedImage.Length];
            byte[] shadowMap = shadowMapReusable;
            for (int i = 0; i < shadowMap.Length; i += 4)
            {
                shadowMap[i + 0] = 128;
                shadowMap[i + 1] = 128;
                shadowMap[i + 2] = 128;
                shadowMap[i + 3] = 128;
            }

            for (int i = 0; i < tintedImage.Length; i++)
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
                    leftTopMapChunk = chunkNeibNW;
                    rightTopMapChunk = chunkNeibW;
                    leftBotMapChunk = chunkNeibN;
                } else
                {
                    if (topX < 0)
                    {
                        leftTopMapChunk = chunkNeibW;
                        rightTopMapChunk = chunkNeibW;
                    }
                    if (leftZ < 0)
                    {
                        leftTopMapChunk = chunkNeibN;
                        leftBotMapChunk = chunkNeibN;
                    }
                }

                topX = GameMath.Mod(topX, chunksize);
                leftZ = GameMath.Mod(leftZ, chunksize);

                leftTop = leftTopMapChunk == null ? 0 : (y - leftTopMapChunk.RainHeightMap[leftZ * chunksize + topX]);
                rightTop = rightTopMapChunk == null ? 0 : (y - rightTopMapChunk.RainHeightMap[rightZ * chunksize + topX]);
                leftBot = leftBotMapChunk == null ? 0 : (y - leftBotMapChunk.RainHeightMap[leftZ * chunksize + botX]);

                float slopedir = (Math.Sign(leftTop) + Math.Sign(rightTop) + Math.Sign(leftBot));
                float steepness = Math.Max(Math.Max(Math.Abs(leftTop), Math.Abs(rightTop)), Math.Abs(leftBot));

                int blockId = chunksTmp[cy].UnpackAndReadBlock(MapUtil.Index3d(lx, y % chunksize, lz, chunksize, chunksize), BlockLayersAccess.FluidOrSolid);
                Block block = api.World.Blocks[blockId];

                if (slopedir > 0) b = 1.08f + Math.Min(0.5f, steepness / 10f) / 1.25f;
                if (slopedir < 0) b = 0.92f - Math.Min(0.5f, steepness / 10f) / 1.25f;

                if (block.BlockMaterial == EnumBlockMaterial.Snow && !colorAccurate)
                {
                    y--;
                    cy = y / chunksize;
                    blockId = chunksTmp[cy].UnpackAndReadBlock(MapUtil.Index3d(localpos.X, y % chunksize, localpos.Y, chunksize, chunksize), BlockLayersAccess.FluidOrSolid);
                    block = api.World.Blocks[blockId];
                }
                tmpPos.Set(chunksize * chunkPos.X + localpos.X, y, chunksize * chunkPos.Y + localpos.Y);

                if (colorAccurate)
                {
                    int avgCol = block.GetColor(capi, tmpPos);
                    int rndCol = block.GetRandomColor(capi, tmpPos, BlockFacing.UP, GameMath.MurmurHash3Mod(tmpPos.X, tmpPos.Y, tmpPos.Z, 30));
                    // Why the eff is r and b flipped
                    rndCol = ((rndCol & 0xff) << 16) | (((rndCol >> 8) & 0xff) << 8) | (((rndCol >> 16) & 0xff) << 0);

                    // Add a bit of randomness to each pixel
                    int col = ColorUtil.ColorOverlay(avgCol, rndCol, 0.6f);

                    tintedImage[i] = col;
                    shadowMap[i] = (byte)(shadowMap[i] * b);
                }
                else
                {

                    if (isLake(block))
                    {
                        // Water
                        IWorldChunk lChunk = chunksTmp[cy];
                        IWorldChunk rChunk = lChunk;
                        IWorldChunk tChunk = lChunk;
                        IWorldChunk bChunk = lChunk;

                        int leftX = localpos.X - 1;
                        int rightX = localpos.X + 1;
                        int topY = localpos.Y - 1;
                        int bottomY = localpos.Y + 1;

                        if (leftX < 0)
                        {
                            lChunk = capi.World.BlockAccessor.GetChunk(chunkPos.X - 1, cy, chunkPos.Y);
                        }
                        if (rightX >= chunksize)
                        {
                            rChunk = capi.World.BlockAccessor.GetChunk(chunkPos.X + 1, cy, chunkPos.Y);
                        }
                        if (topY < 0)
                        {
                            tChunk = capi.World.BlockAccessor.GetChunk(chunkPos.X, cy, chunkPos.Y - 1);
                        }
                        if (bottomY >= chunksize)
                        {
                            bChunk = capi.World.BlockAccessor.GetChunk(chunkPos.X, cy, chunkPos.Y + 1);
                        }

                        if (lChunk != null && rChunk != null && tChunk != null && bChunk != null)
                        {
                            leftX = GameMath.Mod(leftX, chunksize);
                            rightX = GameMath.Mod(rightX, chunksize);
                            topY = GameMath.Mod(topY, chunksize);
                            bottomY = GameMath.Mod(bottomY, chunksize);

                            Block lBlock = api.World.Blocks[lChunk.UnpackAndReadBlock(MapUtil.Index3d(leftX, y % chunksize, localpos.Y, chunksize, chunksize), BlockLayersAccess.FluidOrSolid)];
                            Block rBlock = api.World.Blocks[rChunk.UnpackAndReadBlock(MapUtil.Index3d(rightX, y % chunksize, localpos.Y, chunksize, chunksize), BlockLayersAccess.FluidOrSolid)];
                            Block tBlock = api.World.Blocks[tChunk.UnpackAndReadBlock(MapUtil.Index3d(localpos.X, y % chunksize, topY, chunksize, chunksize), BlockLayersAccess.FluidOrSolid)];
                            Block bBlock = api.World.Blocks[bChunk.UnpackAndReadBlock(MapUtil.Index3d(localpos.X, y % chunksize, bottomY, chunksize, chunksize), BlockLayersAccess.FluidOrSolid)];

                            if (isLake(lBlock) && isLake(rBlock) && isLake(tBlock) && isLake(bBlock))
                            {
                                tintedImage[i] = getColor(block, localpos.X, y, localpos.Y);
                            }
                            else
                            {
                                tintedImage[i] = colorsByCode["wateredge"];
                            }
                        }
                        else
                        {
                            // Default to water until chunks are loaded.
                            tintedImage[i] = getColor(block, localpos.X, y, localpos.Y);
                        }
                    }
                    else
                    {
                        shadowMap[i] = (byte)(shadowMap[i] * b);
                        tintedImage[i] = getColor(block, localpos.X, y, localpos.Y);
                    }
                }



            }

            shadowMapBlurReusable ??= new byte[shadowMap.Length];
            byte[] bla = shadowMapBlurReusable;
            for (int i = 0; i < bla.Length; i+=4)
            {
                bla[i + 0] = shadowMap[i + 0];
                bla[i + 1] = shadowMap[i + 1];
                bla[i + 2] = shadowMap[i + 2];
                bla[i + 3] = shadowMap[i + 3];
            }

            BlurTool.Blur(shadowMap, 32, 32, 2);
            float sharpen = 1.0f;

            for (int i = 0; i < shadowMap.Length; i++)
            {
                float b = ((int)((shadowMap[i]/128f - 1f)*5))/5f;
                b += (((bla[i] / 128f - 1f)*5) % 1) / 5f;

                tintedImage[i] = ColorUtil.ColorMultiply3Clamped(tintedImage[i], b * sharpen + 1f) | 255 << 24;
            }

            for (int cy = 0; cy < chunksTmp.Length; cy++) chunksTmp[cy] = null;

            return tintedImage;
        }

        private int getColor(Block block, int x, int y1, int y2)
        {
            var colorIndex = block2Color[block.Id];
            int color = colors[colorIndex];
            return color;
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
