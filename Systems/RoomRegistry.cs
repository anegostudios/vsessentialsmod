using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class Room
    {
        public int ExitCount;

        public int SkylightCount;
        public int NonSkylightCount;

        public int CoolingWallCount;
        public int NonCoolingWallCount;

        public Cuboidi Location;

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

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.Event.BlockTexturesLoaded += init;
        }
        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Event.SaveGameLoaded += init;
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
                    for (int i = 0; i < chunkrooms.Rooms.Count; i++)
                    {
                        cuboid = chunkrooms.Rooms[i].Location;
                        set.Add(MapUtil.Index3dL(cuboid.Start.X / chunksize, cuboid.Start.Y / chunksize, cuboid.Start.Z / chunksize, chunkMapSizeX, chunkMapSizeZ));
                        set.Add(MapUtil.Index3dL(cuboid.Start.X / chunksize, cuboid.Start.Y / chunksize, cuboid.End.Z / chunksize, chunkMapSizeX, chunkMapSizeZ));
                        set.Add(MapUtil.Index3dL(cuboid.Start.X / chunksize, cuboid.End.Y / chunksize, cuboid.Start.Z / chunksize, chunkMapSizeX, chunkMapSizeZ));
                        set.Add(MapUtil.Index3dL(cuboid.Start.X / chunksize, cuboid.End.Y / chunksize, cuboid.End.Z / chunksize, chunkMapSizeX, chunkMapSizeZ));
                        set.Add(MapUtil.Index3dL(cuboid.End.X / chunksize, cuboid.Start.Y / chunksize, cuboid.Start.Z / chunksize, chunkMapSizeX, chunkMapSizeZ));
                        set.Add(MapUtil.Index3dL(cuboid.End.X / chunksize, cuboid.Start.Y / chunksize, cuboid.End.Z / chunksize, chunkMapSizeX, chunkMapSizeZ));
                        set.Add(MapUtil.Index3dL(cuboid.End.X / chunksize, cuboid.End.Y / chunksize, cuboid.Start.Z / chunksize, chunkMapSizeX, chunkMapSizeZ));
                        set.Add(MapUtil.Index3dL(cuboid.End.X / chunksize, cuboid.End.Y / chunksize, cuboid.End.Z / chunksize, chunkMapSizeX, chunkMapSizeZ));
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
                    if (room.Location.Contains(pos)) {
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


        private Room FindRoomForPosition(BlockPos pos, ChunkRooms otherRooms)
        {
            HashSet<BlockPos> visitedPositions = new HashSet<BlockPos>();
            return FindRoomForPosition(pos, otherRooms, visitedPositions);
        }

        private Room FindRoomForPosition(BlockPos pos, ChunkRooms otherRooms, HashSet<BlockPos> visitedPositions)
        {
            Queue<BlockPos> bfsQueue = new Queue<BlockPos>();

            bfsQueue.Enqueue(pos);
            visitedPositions.Add(pos);

            int maxHalfSize = 7;

            int coolingWallCount = 0;
            int nonCoolingWallCount = 0;

            int skyLightCount = 0;
            int nonSkyLightCount = 0;
            int exitCount = 0;

            blockAccess.Begin();

            HashSet<Vec2i> skyLightXZChecked = new HashSet<Vec2i>();

            bool allChunksLoaded = true;

            int minx=pos.X, miny = pos.Y, minz = pos.Z, maxx = pos.X + 1, maxy = pos.Y + 1, maxz = pos.Z + 1;

            while (bfsQueue.Count > 0)
            {
                BlockPos bpos = bfsQueue.Dequeue();
                
                foreach (BlockFacing facing in BlockFacing.ALLFACES)
                {
                    BlockPos npos = bpos.AddCopy(facing);

                    if (!api.World.BlockAccessor.IsValidPos(npos))
                    {
                        nonCoolingWallCount++;
                        continue;
                    }

                    Block nBlock = blockAccess.GetBlock(npos);
                    allChunksLoaded &= blockAccess.LastChunkLoaded;
                    int heatRetention = nBlock.GetHeatRetention(npos, facing);

                    // We hit a wall, no need to scan further
                    if (heatRetention != 0)
                    {
                        if (heatRetention < 0) coolingWallCount -=heatRetention;
                        else nonCoolingWallCount += heatRetention;

                        continue;
                    }

                    // Only traverse within maxHalfSize range
                    bool inCube = Math.Abs(npos.X - pos.X) <= maxHalfSize && Math.Abs(npos.Y - pos.Y) <= maxHalfSize && Math.Abs(npos.Z - pos.Z) <= maxHalfSize;

                    // Outside maxHalfSize range. Count as exit and don't continue searching in this direction
                    if (!inCube)
                    {
                        exitCount++;
                        continue;
                    }

                    minx = Math.Min(minx, bpos.X);
                    miny = Math.Min(miny, bpos.Y);
                    minz = Math.Min(minz, bpos.Z);

                    maxx = Math.Max(maxx, bpos.X);
                    maxy = Math.Max(maxy, bpos.Y);
                    maxz = Math.Max(maxz, bpos.Z);

                    Vec2i vec = new Vec2i(npos.X, npos.Z);
                    if (skyLightXZChecked.Add(vec))  //HashSet.Add returns true if the element is added to the HashSet<T> object; false if the element is already present.
                    {
                        int light = api.World.BlockAccessor.GetLightLevel(npos, EnumLightLevelType.OnlySunLight);

                        if (light >= api.World.SunBrightness - 1)
                        {
                            skyLightCount++;
                        } else
                        {
                            nonSkyLightCount++;
                        }
                    }
                    

                    if (visitedPositions.Add(npos))  //HashSet.Add returns true if the element is added to the HashSet<T> object; false if the element is already present.
                    {
                        bfsQueue.Enqueue(npos);
                    }
                }
            }

            // Find all rooms inside this boundary
            // Lame but what can we do salasinji
            /*BlockPos cpos = new BlockPos();
            for (int x = minx; x < maxx; x++)
            {
                for (int y = miny; y < maxy; y++)
                {
                    for (int z = minz; z < maxz; z++)
                    {
                        cpos.Set(x, y, z);
                        if (visitedPositions.Contains(cpos)) continue;

                        Block cBlock = api.World.BlockAccessor.GetBlock(cpos);
                        if (cBlock.Replaceable > 6000)
                        {
                            bool contains = false;
                            for (int i = 0; !contains && i < otherRooms.Rooms.Count; i++)
                            {
                                Room exroom = otherRooms.Rooms[i];
                                contains = exroom.Location.Contains(pos);
                            }

                            if (!contains)
                            {
                                Room room = FindRoomForPosition(cpos, otherRooms, visitedPositions);
                                otherRooms.AddRoom(room);
                            }
                        }
                    }
                }
            }*/

            return new Room()
            {
                CoolingWallCount = coolingWallCount,
                NonCoolingWallCount = nonCoolingWallCount,
                SkylightCount = skyLightCount,
                NonSkylightCount = nonSkyLightCount,
                ExitCount = exitCount,
                AnyChunkUnloaded = allChunksLoaded ? 0 : 1,
                Location = new Cuboidi(minx, miny, minz, maxx, maxy, maxz)
            };
        }
    }
}
