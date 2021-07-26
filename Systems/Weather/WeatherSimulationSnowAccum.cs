using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public struct BlockIdAndSnowLevel
    {
        public Block Block;
        public float SnowLevel;

        public BlockIdAndSnowLevel(Block block, float snowLevel)
        {
            Block = block;
            SnowLevel = snowLevel;
        }
    }

    public class UpdateSnowLayerChunk : IEquatable<UpdateSnowLayerChunk>
    {
        public Vec2i Coords;
        public double LastSnowAccumUpdateTotalHours;
        public Dictionary<BlockPos, BlockIdAndSnowLevel> SetBlocks = new Dictionary<BlockPos, BlockIdAndSnowLevel>();

        public bool Equals(UpdateSnowLayerChunk other)
        {
            return other.Coords.Equals(Coords);
        }

        public override bool Equals(object obj)
        {
            Vec2i pos;
            if (obj is UpdateSnowLayerChunk uplc)
            {
                pos = uplc.Coords;
            }
            else
            {
                return false;
            }

            return Coords.X == pos.X && Coords.Y == pos.Y;
        }

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 23 + Coords.X.GetHashCode();
            hash = hash * 23 + Coords.Y.GetHashCode();
            return hash;
        }
    }

    public class WeatherSimulationSnowAccum
    {
        int[][] randomShuffles;

        ICoreServerAPI sapi;
        WeatherSystemBase ws;
        Thread snowLayerScannerThread;
        bool isShuttingDown = false;


        UniqueQueue<Vec2i> chunkColsstoCheckQueue = new UniqueQueue<Vec2i>();

        UniqueQueue<UpdateSnowLayerChunk> updateSnowLayerQueue = new UniqueQueue<UpdateSnowLayerChunk>();

        int chunksize;
        int regionsize;
        internal float accum;

        public bool ProcessChunks = true;
        public bool enabled;

        IBulkBlockAccessor ba;
        IBulkBlockAccessor cuba;

        bool shouldPauseThread;
        bool isThreadPaused;

        public WeatherSimulationSnowAccum(ICoreServerAPI sapi, WeatherSystemBase ws)
        {
            this.sapi = sapi;
            this.ws = ws;

            // Get a non-relighting block accessor, because snowlayers do not block light and do not remove light blocking blocks.
            ba = sapi.World.GetBlockAccessorBulkMinimalUpdate(true, false);
            ba.UpdateSnowAccumMap = false;
            cuba = sapi.World.GetBlockAccessorMapChunkLoading(false, false);
            cuba.UpdateSnowAccumMap = false;

            initRandomShuffles();

            sapi.Event.ChunkColumnSnowUpdate += Event_ChunkColumnLoading;
            sapi.Event.ChunkColumnLoaded += Event_ChunkColumnLoaded;
            sapi.Event.SaveGameLoaded += Event_SaveGameLoaded;
            sapi.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, () => isShuttingDown = true);
            sapi.Event.RegisterGameTickListener(OnServerTick3s, 3000);
            sapi.Event.RegisterGameTickListener(OnServerTick100ms, 100);
            sapi.Event.ServerSuspend += Event_ServerSuspend;
            sapi.Event.ServerResume += Event_ServerResume;

            snowLayerScannerThread = new Thread(new ThreadStart(onThreadStart));
            snowLayerScannerThread.IsBackground = true;
        }

        private void Event_ServerResume()
        {
            shouldPauseThread = false;
        }

        private EnumSuspendState Event_ServerSuspend()
        {
            shouldPauseThread = true;
            if (isThreadPaused) return EnumSuspendState.Ready;
            else return EnumSuspendState.Wait;
        }

        private void Event_SaveGameLoaded()
        {
            chunksize = sapi.World.BlockAccessor.ChunkSize;
            regionsize = sapi.WorldManager.RegionSize;
            if (regionsize == 0)
            {
                sapi.Logger.Notification("Warning: region size was 0 for Snow Accum system");
                regionsize = 16;
            }

            enabled = sapi.World.Config.GetBool("snowAccum", true);
            GlobalConstants.MeltingFreezingEnabled = enabled;

            if (!enabled) return;

            snowLayerScannerThread.Start();
        }


        private void OnServerTick3s(float dt)
        {
            if (ProcessChunks && enabled)
            {
                foreach (var val in sapi.WorldManager.AllLoadedMapchunks)
                {
                    Vec2i chunkCoord = sapi.WorldManager.MapChunkPosFromChunkIndex2D(val.Key);

                    lock (chunkColsstoCheckQueue)
                    {
                        chunkColsstoCheckQueue.Enqueue(chunkCoord);
                    }
                }
            }
        }

        public void AddToCheckQueue(Vec2i chunkCoord)
        {
            lock (chunkColsstoCheckQueue)
            {
                chunkColsstoCheckQueue.Enqueue(chunkCoord);
            }
        }

        private void OnServerTick100ms(float dt)
        {
            accum += dt;

            if (updateSnowLayerQueue.Count > 5 || (accum > 1 && updateSnowLayerQueue.Count > 0))
            {
                accum = 0;
                int cnt = 0;
                int max = 10;
                UpdateSnowLayerChunk[] q = new UpdateSnowLayerChunk[max];

                lock (updateSnowLayerQueue)
                {
                    while (updateSnowLayerQueue.Count > 0)
                    {
                        q[cnt] = updateSnowLayerQueue.Dequeue();
                        cnt++;
                        if (cnt >= max) break;
                    }
                }

                for (int i = 0; i < cnt; i++)
                {
                    IMapChunk mc = sapi.WorldManager.GetMapChunk(q[i].Coords.X, q[i].Coords.Y);
                    if (mc == null) continue; // No longer loaded, we can just ditch it and re-do the thing again next time it gets loaded again
                    processBlockUpdates(mc, q[i], ba);
                }

                ba.Commit();
            }
        }


        internal void processBlockUpdates(IMapChunk mc, UpdateSnowLayerChunk updateChunk, IBulkBlockAccessor ba)
        {
            var setblocks = updateChunk.SetBlocks;
            double lastSnowAccumUpdateTotalHours = updateChunk.LastSnowAccumUpdateTotalHours;

            Vec2i tmpVec = new Vec2i();

            foreach (var sval in setblocks)
            {
                Block newblock = sval.Value.Block;
                float snowLevel = sval.Value.SnowLevel;

                Block hereblock = ba.GetBlock(sval.Key);

                tmpVec.Set(sval.Key.X, sval.Key.Z);
                if (snowLevel > 0 && !mc.SnowAccum.ContainsKey(tmpVec)) continue; // Must have gotten removed since we last checked in our seperate thread

                hereblock.PerformSnowLevelUpdate(ba, sval.Key, newblock, snowLevel);
            }

            mc.SetData("lastSnowAccumUpdateTotalHours", SerializerUtil.Serialize<double>(lastSnowAccumUpdateTotalHours));
            mc.MarkDirty();
        }

        private void Event_ChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
        {
            if (!ProcessChunks) return;

            int regionX = (chunkCoord.X * chunksize) / regionsize;
            int regionZ = (chunkCoord.Y * chunksize) / regionsize;

            WeatherSimulationRegion simregion = ws.getOrCreateWeatherSimForRegion(regionX, regionZ);

            IServerMapChunk mc = sapi.WorldManager.GetMapChunk(chunkCoord.X, chunkCoord.Y);

            if (mc != null && simregion != null)
            {
                lock (chunkColsstoCheckQueue)
                {
                    chunkColsstoCheckQueue.Enqueue(chunkCoord);
                }
            }
        }

        private void Event_ChunkColumnLoading(IServerMapChunk mc, int chunkX, int chunkZ, IWorldChunk[] chunks)
        {
            if (!ProcessChunks) return;

            int regionX = (chunkX * chunksize) / regionsize;
            int regionZ = (chunkZ * chunksize) / regionsize;

            WeatherSimulationRegion simregion = ws.getOrCreateWeatherSimForRegion(regionX, regionZ);

            if (simregion != null)
            {
                if (TryImmediateSnowUpdate(simregion, mc, new Vec2i(chunkX, chunkZ), chunks)) return;
            }
        }

        private bool TryImmediateSnowUpdate(WeatherSimulationRegion simregion, IServerMapChunk mc, Vec2i chunkCoord, IWorldChunk[] chunksCol)
        {
            UpdateSnowLayerChunk dummy = new UpdateSnowLayerChunk()
            {
                Coords = chunkCoord
            };
            lock (updateSnowLayerQueue)
            {
                if (updateSnowLayerQueue.Contains(dummy)) return false;
            }

            double nowTotalHours = ws.api.World.Calendar.TotalHours;
            if (nowTotalHours - simregion.LastUpdateTotalHours > 1) // Lets wait until WeatherSimulationRegion is done updating
            {
                return false;
            }

            UpdateSnowLayerChunk ch = GetSnowUpdate(simregion, mc, chunkCoord, chunksCol);
            if (ch == null) return true;  // The only path returning null, if we provided a full set of chunksCol, will be where newCount == 0
            if (ch.SetBlocks.Count == 0)
            {
                return true;
            }

            cuba.SetChunks(chunkCoord, chunksCol);
            processBlockUpdates(mc, ch, cuba);
            cuba.Commit();

            lock (updateSnowLayerQueue)
            {
                updateSnowLayerQueue.Enqueue(dummy);
            }
            return true;
        }

        private void onThreadStart()
        {
            while (!isShuttingDown)
            {
                Thread.Sleep(5);

                if (shouldPauseThread)
                {
                    isThreadPaused = true;
                    continue;
                }
                isThreadPaused = false;

                int i = 0;

                while (chunkColsstoCheckQueue.Count > 0 && i++ < 10)
                {
                    Vec2i chunkCoord;
                    lock (chunkColsstoCheckQueue)
                    {
                        chunkCoord = chunkColsstoCheckQueue.Dequeue();
                    }

                    int regionX = (chunkCoord.X * chunksize) / regionsize;
                    int regionZ = (chunkCoord.Y * chunksize) / regionsize;

                    WeatherSimulationRegion sim = ws.getOrCreateWeatherSimForRegion(regionX, regionZ);

                    IServerMapChunk mc = sapi.WorldManager.GetMapChunk(chunkCoord.X, chunkCoord.Y);

                    if (mc != null && sim != null)
                    {
                        UpdateSnowLayerOffThread(sim, mc, chunkCoord);
                    }
                }
            }
        }




        void initRandomShuffles()
        {
            randomShuffles = new int[50][];
            for (int i = 0; i < randomShuffles.Length; i++)
            {
                int[] coords = randomShuffles[i] = new int[sapi.World.BlockAccessor.ChunkSize * sapi.World.BlockAccessor.ChunkSize];

                for (int j = 0; j < coords.Length; j++)
                {
                    coords[j] = j;
                }

                GameMath.Shuffle(sapi.World.Rand, coords);
            }
        }



        public void UpdateSnowLayerOffThread(WeatherSimulationRegion simregion, IServerMapChunk mc, Vec2i chunkPos)
        {
            #region Tyrons brain cloud
            // Trick 1: Each x/z coordinate gets a "snow accum" threshold by using a locational random (murmurhash3). Once that threshold is reached, spawn snow. If its doubled, spawn 2nd layer of snow. => Patchy "fade in" of snow \o/
            // Trick 2: We store a region wide snow accum value for the ground level and the map ceiling level. We can now interpolate between those values for each Y-Coordinate \o/
            // Trick 3: We loop through each x/z block in a separate thread, then hand over "place snow" tasks to the main thread
            // Trick 4: Lets pre-gen 50 random shuffles for every x/z coordinate of a chunk. Loop through the region chunks, check which one is loaded and select one random shuffle from the list, then iterate over every x/z coord

            // Trick 5: Snowed over blocks:
            // - New VSMC util: "Automatically Try to add a snow cover to all horizontal faces"
            // - New Block property: SnowCoverableShape. 
            // - Block.OnJsonTesselation adds snow adds cover shape to the sourceMesh!!


            // Trick 6: Turn Cloud Patterns into a "dumb slave system". They are visual information only, so lets make them follow internal mechanisms.
            // - Create a precipitation perlin noise generator. If the precipitation value goes above or below a certain value, we force the cloud pattern system to adapt to a fitting pattern
            // => We gain easy to probe, deterministic precipitation values!! 
            // => We gain the ability to do unloaded chunk snow accumulation and unloaded chunk farmland rain-wetness accum 

            // Trick 6 v2.0: 
            // Rain clouds are simply overlaid onto the normal clouds.


            // Questions:
            // - Q1: When should it hail now?
            // - Q2: How is particle size determined?
            // - Q3: When should there be thunder?
            // - Q4: How to control the precipitation by command?

            // A1/A3: What if we read the slope of precipitation change. If there is a drastic increase of rain fall launch a
            // a. wind + thunder event 
            // b. thunder event
            // c. rarely a hail event
            // d. extra rarely thunder + hail event

            // A2: Particle size is determiend by precipitation intensity


            // Trick 7 v2.0
            // - Hail and Thunder are also triggered by a perlin noise generator. That way I don't need to care about event range.

            // A4: /weather setprecip [auto or 0..1]

            // - Q5: How do we overlay rain clouds onto the normal clouds? 
            //         Q5a: Will they be hardcoded? Or configurable?                         
            //         Q5b: How does the overlay work? Lerp?                                 
            //         Q5c: Rain cloud intensity should relate to precip level. 
            //         How? Lerp from zero to max rain clouds? Multiple cloud configs and lerp between them? 

            // - A5a: Configurable
            //   A5b: Lerp. 
            //   A5c: Single max rain cloud config seems sufficient


            // TODO:
            // 1. Rain cloud overlay
            // 2. Snow accum
            // 3. Hail, Thunder perlin noise
            // 4. Done?


            // Idea 8:
            // - FUCK the region based weather sim. 
            // - Generate clouds patterns like you generate terrain from landforms
            // - Which is grid based indices, neatly abstracted with LerpedIndex2DMap and nicely shaped with domain warping
            // - Give it enough padding to ensure domain warping does not go out of bounds
            // - Every 2-3 minutes regenerate this map in a seperate thread, cloud renderer lerps between old and new map. 
            // - Since the basic indices input is grid based, we can cycle those individually through time



            // for a future version
            // Hm. Maybe one noise generator for cloud coverage?
            // => Gain the ability to affect local temperature based on cloud coverage

            // Hm. Or maybe one noise generator for each cloud pattern?
            // => Gain the abillity for small scale and very large scale cloud patterns

            // Maybe even completely ditch per-region simulation?
            // => Gain the ability for migrating weather patterns

            // but then what will determine the cloud pattern?

            // Region-less Concept:
            // Take an LCGRandom. Use xpos and zpos+((int)totalDays) / 5 for coords
            // Iterate over every player
            //  - iterate over a 20x20 chunk area around it (or max view dist + 5 chunks)
            //    - domain warp x/z coords. use those coords to init position seed on lcgrand. get random value
            //    - store in an LerpedWeightedIndex2DMap
            // Iterate over every cloud tile
            //  - read cloud pattern data from the map  








            // Snow accum needs to take the existing world information into account, i.e. current snow level
            // We should probably
            // - Store snow accumulation as a float value in mapchunkdata as Dictionary<BlockPos, float>
            // - Every 3 seconds or so, "commit" that snow accum into actual snow layer blocks, i.e. if accum >= 1 then add one snow layer and do accum-=1




            #endregion

            UpdateSnowLayerChunk ch = new UpdateSnowLayerChunk()
            {
                Coords = chunkPos
            };

            // Lets wait until we're done with the current job for this chunk
            lock (updateSnowLayerQueue)
            {
                if (updateSnowLayerQueue.Contains(ch)) return;
            }

            if (simregion == null) return;

            double nowTotalHours = ws.api.World.Calendar.TotalHours;
            if (nowTotalHours - simregion.LastUpdateTotalHours > 1) // Lets wait until WeatherSimulationRegion is done updating
            {
                return;
            }

            ch = GetSnowUpdate(simregion, mc, chunkPos, null);

            if (ch != null)
            {
                lock (updateSnowLayerQueue)
                {
                    updateSnowLayerQueue.Enqueue(ch);
                }
            }
        }

        private UpdateSnowLayerChunk GetSnowUpdate(WeatherSimulationRegion simregion, IServerMapChunk mc, Vec2i chunkPos, IWorldChunk[] chunksCol)
        {

            byte[] data = mc.GetData("lastSnowAccumUpdateTotalHours");
            double lastSnowAccumUpdateTotalHours = data == null ? 0 : SerializerUtil.Deserialize<double>(data);
            double startTotalHours = lastSnowAccumUpdateTotalHours;

            int reso = WeatherSimulationRegion.snowAccumResolution;

            SnowAccumSnapshot sumsnapshot = new SnowAccumSnapshot()
            {
                //SumTemperatureByRegionCorner = new API.FloatDataMap3D(reso, reso, reso),
                SnowAccumulationByRegionCorner = new FloatDataMap3D(reso, reso, reso)
            };
            float[] sumdata = sumsnapshot.SnowAccumulationByRegionCorner.Data;

            // Can't grow bigger than one full snow block
            float max = ws.GeneralConfig.SnowLayerBlocks.Count + 0.5f;

            int len = simregion.SnowAccumSnapshots.Length;
            int i = simregion.SnowAccumSnapshots.Start;
            int newCount = 0;

            lock (WeatherSimulationRegion.lockTest)
            {

                while (len-- > 0)
                {
                    SnowAccumSnapshot hoursnapshot = simregion.SnowAccumSnapshots[i];
                    i = (i + 1) % simregion.SnowAccumSnapshots.Length;

                    if (hoursnapshot == null || lastSnowAccumUpdateTotalHours >= hoursnapshot.TotalHours) continue;

                    float[] snowaccumdata = hoursnapshot.SnowAccumulationByRegionCorner.Data;
                    for (int j = 0; j < snowaccumdata.Length; j++)
                    {
                        sumdata[j] = GameMath.Clamp(sumdata[j] + snowaccumdata[j], -max, max);
                    }

                    lastSnowAccumUpdateTotalHours = Math.Max(lastSnowAccumUpdateTotalHours, hoursnapshot.TotalHours);
                    newCount++;
                }

            }

            if (newCount == 0) return null;

            bool ignoreOldAccum = false;
            if (lastSnowAccumUpdateTotalHours - startTotalHours >= sapi.World.Calendar.DaysPerYear * sapi.World.Calendar.HoursPerDay)
            {
                ignoreOldAccum = true;
            }

            UpdateSnowLayerChunk ch = UpdateSnowLayer(sumsnapshot, ignoreOldAccum, mc, chunkPos, chunksCol);

            if (ch != null)
            {
                //Console.WriteLine("{0} snaps used for {1}/{2}", newCount, chunkPos.X, chunkPos.Y);

                ch.LastSnowAccumUpdateTotalHours = lastSnowAccumUpdateTotalHours;
                ch.Coords = chunkPos.Copy();
            }
            return ch;
        }


        public UpdateSnowLayerChunk UpdateSnowLayer(SnowAccumSnapshot sumsnapshot, bool ignoreOldAccum, IServerMapChunk mc, Vec2i chunkPos, IWorldChunk[] chunksCol)
        {
            UpdateSnowLayerChunk updateChunk = new UpdateSnowLayerChunk();
            var layers = ws.GeneralConfig.SnowLayerBlocks;

            int chunkX = chunkPos.X;
            int chunkZ = chunkPos.Y;

            int regionX = (chunkX * chunksize) / regionsize;
            int regionZ = (chunkZ * chunksize) / regionsize;

            int regionBasePosX = regionX * regionsize;
            int regionBasePosZ = regionZ * regionsize;

            BlockPos pos = new BlockPos();
            BlockPos placePos = new BlockPos();
            float aboveSeaLevelHeight = sapi.World.BlockAccessor.MapSizeY - sapi.World.SeaLevel;

            int[] posIndices = randomShuffles[sapi.World.Rand.Next(randomShuffles.Length)];

            int prevChunkY = -99999;
            IWorldChunk chunk = null;

            int maxY = sapi.World.BlockAccessor.MapSizeY - 1;

            for (int i = 0; i < posIndices.Length; i++)
            {
                int posIndex = posIndices[i];
                int posY = GameMath.Clamp(mc.RainHeightMap[posIndex], 0, maxY);
                int chunkY = posY / chunksize;

                pos.Set(
                    chunkX * chunksize + posIndex % chunksize,
                    posY,
                    chunkZ * chunksize + posIndex / chunksize
                );

                if (prevChunkY != chunkY)
                {
                    chunk = chunksCol?[chunkY] ?? sapi.WorldManager.GetChunk(chunkX, chunkY, chunkZ);
                    prevChunkY = chunkY;
                }
                if (chunk == null) return null;

                float relx = (pos.X - regionBasePosX) / (float)regionsize;
                float rely = GameMath.Clamp((pos.Y - sapi.World.SeaLevel) / aboveSeaLevelHeight, 0, 1);
                float relz = (pos.Z - regionBasePosZ) / (float)regionsize;


                // What needs to be done here?
                // 1. Get desired snow cover level

                // 2. Get current snow cover level
                //    - Get topmmost block. Is it snow?
                //      - Yes. Use it as reference pos and stuff
                //      - No. Must have no snow, increment pos.Y by 1

                // 3. Compare and place block accordingly
                // Idea: New method Block.UpdateSnowLayer() returns a new block instance if a block change is needed


                // What needs to be done here, take 2
                // We have 3 possible cases per-block
                // 1: We find upside solid block. That means it has no snow on top
                // 2: We find snow. That means below is a solid block. 
                // 3: We find some other block: That means we should try to find its snow-covered variant

                // We have the following input data
                // 1. Snow accumulation changes since the last update (usually an in-game hour or 2)
                // 2. A precise snow level value from the position (if not set, load from snowlayer block type) (set to zero if the snowlayer is removed)
                // 3. The current block at position, which is either
                //    - A snow layer: Override with internal level + accum changes
                //    - A solid block: Plase snow on top based on internal level + accum changes
                //    - A snow variantable block: Call the method with the new level


                Block block = chunk.GetLocalBlockAtBlockPos(sapi.World, pos);

                float hereAccum = 0;

                Vec2i vec = new Vec2i(pos.X, pos.Z);
                if (!ignoreOldAccum && !mc.SnowAccum.TryGetValue(vec, out hereAccum))
                {
                    hereAccum = block.GetSnowLevel(pos);
                }

                float nowAccum = hereAccum + sumsnapshot.GetAvgSnowAccumByRegionCorner(relx, rely, relz);
                
                mc.SnowAccum[vec] = GameMath.Clamp(nowAccum, -1, ws.GeneralConfig.SnowLayerBlocks.Count + 0.5f);

                float hereShouldLevel = nowAccum - GameMath.MurmurHash3Mod(pos.X, 0, pos.Z, 100) / 300f;
                float shouldIndexf = GameMath.Clamp((hereShouldLevel - 1.1f), -1, ws.GeneralConfig.SnowLayerBlocks.Count - 1);
                int shouldIndex = shouldIndexf < 0 ? -1 : (int)shouldIndexf;

                placePos.Set(pos.X, Math.Min(pos.Y + 1, sapi.World.BlockAccessor.MapSizeY - 1), pos.Z);
                chunkY = placePos.Y / chunksize;

                if (prevChunkY != chunkY)
                {
                    chunk = chunksCol?[chunkY] ?? sapi.WorldManager.GetChunk(chunkX, chunkY, chunkZ);
                    prevChunkY = chunkY;
                }
                if (chunk == null) return null;

                Block upBlock = chunk.GetLocalBlockAtBlockPos(sapi.World, placePos);



                // Case 1: We have a block that can become snow covered (or more snow covered)
                placePos.Set(pos);
                Block newblock = block.GetSnowCoveredVariant(placePos, hereShouldLevel);
                if (newblock != null)
                {
                    if (block.Id != newblock.Id && upBlock.Replaceable > 6000)
                    {
                        updateChunk.SetBlocks[placePos.Copy()] = new BlockIdAndSnowLevel(newblock, hereShouldLevel);
                    }
                }
                // Case 2: We have a solid block that can have snow on top
                else if ((block.SnowCoverage == null && block.SideSolid[BlockFacing.UP.Index]) || block.SnowCoverage == true)
                {
                    placePos.Set(pos.X, pos.Y + 1, pos.Z);

                    if (upBlock.Id != 0)
                    {
                        newblock = upBlock.GetSnowCoveredVariant(placePos, hereShouldLevel);
                        if (newblock != null && upBlock.Id != newblock.Id)
                        {
                            updateChunk.SetBlocks[placePos.Copy()] = new BlockIdAndSnowLevel(newblock, hereShouldLevel);
                        }

                        continue;
                    }

                    if (shouldIndex >= 0)
                    {
                        Block toPlaceBlock = layers.GetKeyAtIndex(shouldIndex);
                        updateChunk.SetBlocks[placePos.Copy()] = new BlockIdAndSnowLevel(toPlaceBlock, hereShouldLevel);
                    }
                }
            }


            return updateChunk;
        }

        
    }
}
