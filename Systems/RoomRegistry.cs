using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class Room
    {
        public int ExitCount;
        /// <summary>
        /// If true, indicates room dimensions do not exceed recommended cellar dimensions of 7x7x7  (soft limit: slightly longer shapes with low overall volume also permitted)
        /// </summary>
        public bool IsSmallRoom;

        public int SkylightCount;
        public int NonSkylightCount;

        public int CoolingWallCount;
        public int NonCoolingWallCount;

        /// <summary>
        /// A bounding box of the found room volume, but that doesn't mean this volumne is 100% room. You can check if a block inside inside is volume is part of the room with the PosInRoom byte array
        /// </summary>
        public Cuboidi Location;

        public byte[] PosInRoom;


        /// <summary>
        /// If greater than 0, a chunk is unloaded.  Counts upwards and when it reaches a certain value, this room will be removed from the registry and re-checked: this allows valid fully loaded rooms to be detected quite quickly in the normal world loading process
        /// The potential issue is a room with a container, on the very edge of the server's loaded world, with neighbouring chunks remaining unloaded for potentially a long time.  This will never be loaded, so we don't want to recheck its status fully too often: not every tick, that would be too costly
        /// </summary>
        public int AnyChunkUnloaded;

        public bool IsFullyLoaded(ChunkRooms roomsList)
        {
            if (AnyChunkUnloaded == 0) return true;

            if (++AnyChunkUnloaded > 10)
            {
                roomsList.RemoveRoom(this);
            }
            return false;
        }

        public bool Contains(BlockPos pos)
        {
            if (!Location.ContainsOrTouches(pos)) return false;

            int sizez = Location.Z2 - Location.Z1 + 1;
            int sizex = Location.X2 - Location.X1 + 1;

            int dx = pos.X - Location.X1;
            int dy = pos.Y - Location.Y1;
            int dz = pos.Z - Location.Z1;

            int index = (dy * sizez + dz) * sizex + dx;

            return (PosInRoom[index / 8] & (1 << (index % 8))) > 0;
        }
    }

    public class ChunkRooms {
        public List<Room> Rooms = new List<Room>();

        public object roomsLock = new object();
        public void AddRoom(Room room)
        {
            lock (roomsLock)
            {
                Rooms.Add(room);
            }
        }
        public void RemoveRoom(Room room)
        {
            lock (roomsLock)
            {
                Rooms.Remove(room);
            }
        }

    }

    public class RoomRegistry : ModSystem
    {
        protected Dictionary<long, ChunkRooms> roomsByChunkIndex = new Dictionary<long, ChunkRooms>();
        protected object roomsByChunkIndexLock = new object(); 

        int chunksize;
        int chunkMapSizeX;
        int chunkMapSizeZ;

        ICoreAPI api;
        ICachingBlockAccessor blockAccess;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;

            api.Event.ChunkDirty += Event_ChunkDirty;

            blockAccess = api.World.GetCachingBlockAccessor(false, false);
        }

        public override void Dispose()
        {
            blockAccess?.Dispose();
            blockAccess = null;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.Event.BlockTexturesLoaded += init;
        }
        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Event.SaveGameLoaded += init;

            api.ChatCommands.GetOrCreate("debug")
                .BeginSubCommand("roomregdebug")
                    .RequiresPrivilege(Privilege.controlserver)

                    .BeginSubCommand("list")
                        .HandleWith(onRoomRegDbgCmdList)
                    .EndSubCommand()

                    .BeginSubCommand("hi")
                        .WithArgs(api.ChatCommands.Parsers.OptionalInt("rindex"))
                        .RequiresPlayer()
                        .HandleWith(onRoomRegDbgCmdHi)
                    .EndSubCommand()

                    .BeginSubCommand("unhi")
                        .RequiresPlayer()
                        .HandleWith(onRoomRegDbgCmdUnhi)
                    .EndSubCommand()
                .EndSubCommand()
                ;
        }

        private TextCommandResult onRoomRegDbgCmdHi(TextCommandCallingArgs args)
        {
            int rindex = (int)args.Parsers[0].GetValue();
            var player = args.Caller.Player as IServerPlayer;
            BlockPos pos = player.Entity.Pos.XYZ.AsBlockPos;
            long index3d = MapUtil.Index3dL(pos.X / chunksize, pos.Y / chunksize, pos.Z / chunksize, chunkMapSizeX, chunkMapSizeZ);
            ChunkRooms chunkrooms;
            lock (roomsByChunkIndexLock)
            {
                roomsByChunkIndex.TryGetValue(index3d, out chunkrooms);
            }

            if (chunkrooms == null || chunkrooms.Rooms.Count == 0)
            {
                return TextCommandResult.Success("No rooms here");
            }

            if (chunkrooms.Rooms.Count - 1 < rindex || rindex < 0)
            {
                if (rindex == 0)
                {
                    TextCommandResult.Success("No room here");
                }
                else
                {
                    TextCommandResult.Success("Wrong index, select a number between 0 and " + (chunkrooms.Rooms.Count - 1));
                }
            }
            else
            {
                Room room = chunkrooms.Rooms[rindex];

                // Debug visualization
                List<BlockPos> poses = new List<BlockPos>();
                List<int> colors = new List<int>();

                int sizex = room.Location.X2 - room.Location.X1 + 1;
                int sizey = room.Location.Y2 - room.Location.Y1 + 1;
                int sizez = room.Location.Z2 - room.Location.Z1 + 1;
                
                for (int dx = 0; dx < sizex; dx++)
                {
                    for (int dy = 0; dy < sizey; dy++)
                    {
                        for (int dz = 0; dz < sizez; dz++)
                        {
                            int pindex = (dy * sizez + dz) * sizex + dx;

                            if ((room.PosInRoom[pindex / 8] & (1 << (pindex % 8))) > 0)
                            {
                                poses.Add(new BlockPos(room.Location.X1 + dx, room.Location.Y1 + dy, room.Location.Z1 + dz));
                                colors.Add(ColorUtil.ColorFromRgba(room.ExitCount == 0 ? 0 : 100, room.ExitCount == 0 ? 100 : 0, Math.Min(255, rindex * 30), 150));
                            }
                        }
                    }
                }

                api.World.HighlightBlocks(player, 50, poses, colors);
            }   
            return TextCommandResult.Success();
        }

        private TextCommandResult onRoomRegDbgCmdUnhi(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            api.World.HighlightBlocks(player, 50, new List<BlockPos>(), new List<int>());

            return TextCommandResult.Success();
        }

        private TextCommandResult onRoomRegDbgCmdList(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            BlockPos pos = player.Entity.Pos.XYZ.AsBlockPos;
            long index3d = MapUtil.Index3dL(pos.X / chunksize, pos.Y / chunksize, pos.Z / chunksize, chunkMapSizeX, chunkMapSizeZ);
            ChunkRooms chunkrooms;
            lock (roomsByChunkIndexLock)
            {
                roomsByChunkIndex.TryGetValue(index3d, out chunkrooms);
            }

            if (chunkrooms == null || chunkrooms.Rooms.Count == 0)
            {
                return TextCommandResult.Success("No rooms here");
            }
            string response = chunkrooms.Rooms.Count + " Rooms here \n";

            lock (chunkrooms.roomsLock)
            {
                for (int i = 0; i < chunkrooms.Rooms.Count; i++)
                {
                    Room room = chunkrooms.Rooms[i];
                    int sizex = room.Location.X2 - room.Location.X1 + 1;
                    int sizey = room.Location.Y2 - room.Location.Y1 + 1;
                    int sizez = room.Location.Z2 - room.Location.Z1 + 1;
                    response += string.Format("{0} - bbox dim: {1}/{2}/{3}, mid: {4}/{5}/{6}\n", i, sizex, sizey,
                        sizez, room.Location.X1 + sizex / 2f, room.Location.Y1 + sizey / 2f,
                        room.Location.Z1 + sizez / 2f);

                }
            }

            return TextCommandResult.Success(response);
        }

        private void init()
        {
            chunksize = this.api.World.BlockAccessor.ChunkSize;
            chunkMapSizeX = api.World.BlockAccessor.MapSizeX / chunksize;
            chunkMapSizeZ = api.World.BlockAccessor.MapSizeZ / chunksize;
        }


        private void Event_ChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
        {
            long index3d = MapUtil.Index3dL(chunkCoord.X, chunkCoord.Y, chunkCoord.Z, chunkMapSizeX, chunkMapSizeZ);
            ChunkRooms chunkrooms;
            Cuboidi cuboid;
            FastSetOfLongs set = new FastSetOfLongs();
            set.Add(index3d);
            lock (roomsByChunkIndexLock)
            {
                roomsByChunkIndex.TryGetValue(index3d, out chunkrooms);
                if (chunkrooms != null)
                {
                    set.Add(index3d);
                    for (int i = 0; i < chunkrooms.Rooms.Count; i++)
                    {
                        cuboid = chunkrooms.Rooms[i].Location;
                        int x1 = cuboid.Start.X / chunksize;
                        int x2 = cuboid.End.X / chunksize;
                        int y1 = cuboid.Start.Y / chunksize;
                        int y2 = cuboid.End.Y / chunksize;
                        int z1 = cuboid.Start.Z / chunksize;
                        int z2 = cuboid.End.Z / chunksize;
                        set.Add(MapUtil.Index3dL(x1, y1, z1, chunkMapSizeX, chunkMapSizeZ));
                        if (z2 != z1) set.Add(MapUtil.Index3dL(x1, y1, z2, chunkMapSizeX, chunkMapSizeZ));
                        if (y2 != y1)
                        {
                            set.Add(MapUtil.Index3dL(x1, y2, z1, chunkMapSizeX, chunkMapSizeZ));
                            if (z2 != z1) set.Add(MapUtil.Index3dL(x1, y2, z2, chunkMapSizeX, chunkMapSizeZ));
                        }
                        if (x2 != x1)
                        {
                            set.Add(MapUtil.Index3dL(x2, y1, z1, chunkMapSizeX, chunkMapSizeZ));
                            if (z2 != z1) set.Add(MapUtil.Index3dL(x2, y1, z2, chunkMapSizeX, chunkMapSizeZ));
                            if (y2 != y1)
                            {
                                set.Add(MapUtil.Index3dL(x2, y2, z1, chunkMapSizeX, chunkMapSizeZ));
                                if (z2 != z1) set.Add(MapUtil.Index3dL(x2, y2, z2, chunkMapSizeX, chunkMapSizeZ));
                            }
                        }
                    }
                }
                foreach (long index in set) roomsByChunkIndex.Remove(index);
            }
        }

        public Room GetRoomForPosition(BlockPos pos)
        {
            long index3d = MapUtil.Index3dL(pos.X / chunksize, pos.Y / chunksize, pos.Z / chunksize, chunkMapSizeX, chunkMapSizeZ);
            
            ChunkRooms chunkrooms;
            Room room;

            lock (roomsByChunkIndexLock)
            {
                roomsByChunkIndex.TryGetValue(index3d, out chunkrooms);
            }
            
            if (chunkrooms != null)
            {
                Room firstEnclosedRoom=null;
                Room firstOpenedRoom=null;

                for (int i = 0; i < chunkrooms.Rooms.Count; i++)
                {
                    room = chunkrooms.Rooms[i];
                    if (room.Contains(pos))
                    {
                        if (firstEnclosedRoom == null && room.ExitCount == 0)
                        {
                            firstEnclosedRoom = room;
                        }
                        if (firstOpenedRoom == null && room.ExitCount > 0)
                        {
                            firstOpenedRoom = room;
                        }
                    }
                }

                if (firstEnclosedRoom != null && firstEnclosedRoom.IsFullyLoaded(chunkrooms)) return firstEnclosedRoom;
                if (firstOpenedRoom != null && firstOpenedRoom.IsFullyLoaded(chunkrooms)) return firstOpenedRoom;

                room = FindRoomForPosition(pos, chunkrooms);
                chunkrooms.AddRoom(room);

                return room;
            }



            ChunkRooms rooms = new ChunkRooms();
            room = FindRoomForPosition(pos, rooms);
            rooms.AddRoom(room);

            lock (roomsByChunkIndexLock)
            {
                roomsByChunkIndex[index3d] = rooms;
            }

            return room;
        }


        const int ARRAYSIZE = 29;  // Note if this constant is increased beyond 32, the bitshifts for compressedPos in the bfsQueue.Enqueue() and .Dequeue() calls may need updating
        readonly int[] currentVisited = new int[ARRAYSIZE * ARRAYSIZE * ARRAYSIZE];
        readonly int[] skyLightXZChecked = new int[ARRAYSIZE * ARRAYSIZE];
        const int MAXROOMSIZE = 14;
        const int MAXCELLARSIZE = 7;
        const int ALTMAXCELLARSIZE = 9;
        const int ALTMAXCELLARVOLUME = 150;
        int iteration = 0;


        private Room FindRoomForPosition(BlockPos pos, ChunkRooms otherRooms)
        {
            QueueOfInt bfsQueue = new QueueOfInt();

            int halfSize = (ARRAYSIZE - 1) / 2;
            int maxSize = halfSize + halfSize;
            bfsQueue.Enqueue(halfSize << 10 | halfSize << 5 | halfSize);

            int visitedIndex = (halfSize * ARRAYSIZE + halfSize) * ARRAYSIZE + halfSize; // Center node
            int iteration = ++this.iteration;
            currentVisited[visitedIndex] = iteration;

            int coolingWallCount = 0;
            int nonCoolingWallCount = 0;

            int skyLightCount = 0;
            int nonSkyLightCount = 0;
            int exitCount = 0;

            blockAccess.Begin();

            bool allChunksLoaded = true;

            int minx = halfSize, miny = halfSize, minz = halfSize, maxx = halfSize, maxy = halfSize, maxz = halfSize;
            int posX = pos.X - halfSize;
            int posY = pos.Y - halfSize;
            int posZ = pos.Z - halfSize;
            BlockPos npos = new BlockPos();
            BlockPos bpos = new BlockPos();
            int dx, dy, dz;

            while (bfsQueue.Count > 0)
            {
                int compressedPos = bfsQueue.Dequeue();
                dx = compressedPos >> 10;
                dy = (compressedPos >> 5) & 0x1f;
                dz = compressedPos & 0x1f;
                npos.Set(posX + dx, posY + dy, posZ + dz);
                bpos.Set(npos);

                if (dx < minx) minx = dx;
                else if (dx > maxx) maxx = dx;

                if (dy < miny) miny = dy;
                else if (dy > maxy) maxy = dy;

                if (dz < minz) minz = dz;
                else if (dz > maxz) maxz = dz;

                Block bBlock = blockAccess.GetBlock(bpos);

                foreach (BlockFacing facing in BlockFacing.ALLFACES)
                {
                    facing.IterateThruFacingOffsets(npos);  // This must be the first command in the loop, to ensure all facings will be properly looped through regardless of any 'continue;' statements

                    // We cannot exit current block, if the facing is heat retaining (e.g. chiselled block with solid side)
                    if (bBlock.Id != 0 && bBlock.GetHeatRetention(bpos, facing) != 0)
                    {
                        continue;
                    }

                    if (!blockAccess.IsValidPos(npos))
                    {
                        nonCoolingWallCount++;
                        continue;
                    }

                    Block nBlock = blockAccess.GetBlock(npos);
                    allChunksLoaded &= blockAccess.LastChunkLoaded;
                    int heatRetention = nBlock.GetHeatRetention(npos, facing.Opposite);

                    // We hit a wall, no need to scan further
                    if (heatRetention != 0)
                    {
                        if (heatRetention < 0) coolingWallCount -=heatRetention;
                        else nonCoolingWallCount += heatRetention;

                        continue;
                    }

                    // Compute the new dx, dy, dz offsets for npos
                    dx = npos.X - posX;
                    dy = npos.Y - posY;
                    dz = npos.Z - posZ;

                    // Only traverse within maxSize range, and overall room size must not exceed MAXROOMSIZE
                    //   If outside that, count as an exit and don't continue searching in this direction
                    //   Note: for performance, this switch statement ensures only one conditional check in each case on the dimension which has actually changed, instead of 6 conditionals or more
                    bool outsideCube = false;
                    switch (facing.Index)
                    {
                        case 0: // North
                            if (dz < minz) outsideCube = dz < 0 || maxz - minz >= MAXROOMSIZE;
                            break;
                        case 1: // East
                            if (dx > maxx) outsideCube = dx > maxSize || maxx - minx >= MAXROOMSIZE;
                            break;
                        case 2: // South
                            if (dz > maxz) outsideCube = dz > maxSize || maxz - minz >= MAXROOMSIZE;
                            break;
                        case 3: // West
                            if (dx < minx) outsideCube = dx < 0 || maxx - minx >= MAXROOMSIZE;
                            break;
                        case 4: // Up
                            if (dy > maxy) outsideCube = dy > maxSize || maxy - miny >= MAXROOMSIZE;
                            break;
                        case 5: // Down
                            if (dy < miny) outsideCube = dy < 0 || maxy - miny >= MAXROOMSIZE;
                            break;
                    }
                    if (outsideCube)
                    {
                        exitCount++;
                        continue;
                    }


                    visitedIndex = (dx * ARRAYSIZE + dy) * ARRAYSIZE + dz;
                    if (currentVisited[visitedIndex] == iteration) continue;   // continue if block position was already visited
                    currentVisited[visitedIndex] = iteration;

                    // We only need to check the skylight if it's a block position not already visited ...
                    int skyLightIndex = dx * ARRAYSIZE + dz;
                    if (skyLightXZChecked[skyLightIndex] < iteration)
                    {
                        skyLightXZChecked[skyLightIndex] = iteration;
                        int light = blockAccess.GetLightLevel(npos, EnumLightLevelType.OnlySunLight);

                        if (light >= api.World.SunBrightness - 1)
                        {
                            skyLightCount++;
                        }
                        else
                        {
                            nonSkyLightCount++;
                        }
                    }

                    bfsQueue.Enqueue(dx << 10 | dy << 5 | dz);
                }
            }



            int sizex = maxx - minx + 1;
            int sizey = maxy - miny + 1;
            int sizez = maxz - minz + 1;

            byte[] posInRoom = new byte[(sizex * sizey * sizez + 7) / 8];

            int volumeCount = 0;
            for (dx = 0; dx < sizex; dx++)
            {
                for (dy = 0; dy < sizey; dy++)
                {
                    visitedIndex = ((dx + minx) * ARRAYSIZE + (dy + miny)) * ARRAYSIZE + minz;
                    for (dz = 0; dz < sizez; dz++)
                    {
                        if (currentVisited[visitedIndex + dz] == iteration)
                        {
                            int index = (dy * sizez + dz) * sizex + dx;

                            posInRoom[index / 8] = (byte)(posInRoom[index / 8] | (1 << (index % 8)));
                            volumeCount++;
                        }
                    }
                }
            }

            bool isCellar = sizex <= MAXCELLARSIZE && sizey <= MAXCELLARSIZE && sizez <= MAXCELLARSIZE;
            if (!isCellar && volumeCount <= ALTMAXCELLARVOLUME)
            {
                isCellar = sizex <= ALTMAXCELLARSIZE && sizey <= MAXCELLARSIZE && sizez <= MAXCELLARSIZE
                    || sizex <= MAXCELLARSIZE && sizey <= ALTMAXCELLARSIZE && sizez <= MAXCELLARSIZE
                    || sizex <= MAXCELLARSIZE && sizey <= MAXCELLARSIZE && sizez <= ALTMAXCELLARSIZE;
            }


            return new Room()
            {
                CoolingWallCount = coolingWallCount,
                NonCoolingWallCount = nonCoolingWallCount,
                SkylightCount = skyLightCount,
                NonSkylightCount = nonSkyLightCount,
                ExitCount = exitCount,
                AnyChunkUnloaded = allChunksLoaded ? 0 : 1,
                Location = new Cuboidi(posX + minx, posY + miny, posZ + minz, posX + maxx, posY + maxy, posZ + maxz),
                PosInRoom = posInRoom,
                IsSmallRoom = isCellar && exitCount == 0
            };
        }
    }
}
