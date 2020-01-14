using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

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
    }

    public class RoomRegistry : ModSystem
    {
        protected Dictionary<long, ChunkRooms> roomsByChunkIndex = new Dictionary<long, ChunkRooms>();
        protected object roomsByChunkIndexLock = new object(); 

        int chunksize;
        int chunkMapSizeX;
        int chunkMapSizeZ;

        ICoreAPI api;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;

            api.Event.ChunkDirty += Event_ChunkDirty;
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
            lock (roomsByChunkIndexLock)
            {

                roomsByChunkIndex.Remove(index3d);
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

                if (firstEnclosedRoom != null) return firstEnclosedRoom;
                if (firstOpenedRoom != null) return firstOpenedRoom;

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

            int maxHalfSize = 6;

            int coolingWallCount = 0;
            int nonCoolingWallCount = 0;

            int skyLightCount = 0;
            int nonSkyLightCount = 0;
            int exitCount = 0;

            HashSet<Vec2i> skyLightXZChecked = new HashSet<Vec2i>();

            int minx=pos.X, miny = pos.Y, minz = pos.Z, maxx = pos.X, maxy = pos.Y, maxz = pos.Z;

            while (bfsQueue.Count > 0)
            {
                BlockPos bpos = bfsQueue.Dequeue();
                
                foreach (BlockFacing facing in BlockFacing.ALLFACES)
                {
                    BlockPos npos = bpos.AddCopy(facing);
                    Block nBlock = api.World.BlockAccessor.GetBlock(npos);

                    // We hit a wall, no need to scan further
                    if (nBlock.SideSolid[facing.GetOpposite().Index] || nBlock.SideSolid[facing.Index])
                    {
                        if (nBlock.BlockMaterial == EnumBlockMaterial.Stone || nBlock.BlockMaterial == EnumBlockMaterial.Soil || nBlock.BlockMaterial == EnumBlockMaterial.Ceramic) coolingWallCount++;
                        else nonCoolingWallCount++;
                        
                        continue;
                    }

                    // We hit a door or trapdoor - stop, but penalty!
                    if (nBlock.Code.Path.Contains("door"))
                    {
                        nonCoolingWallCount+=3;
                        continue;
                    }
                    
                    // Only traverse within an 12x12x12 block cube
                    bool inCube = Math.Abs(npos.X - pos.X) <= maxHalfSize && Math.Abs(npos.Y - pos.Y) <= maxHalfSize && Math.Abs(npos.Z - pos.Z) <= maxHalfSize;

                    // Outside the 12x12x12. Count as exit and don't continue searching in this direction
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
                    if (!skyLightXZChecked.Contains(vec))
                    {
                        skyLightXZChecked.Add(vec);

                        int rainY = api.World.BlockAccessor.GetRainMapHeightAt(npos);
                        if (rainY <= npos.Y)
                        {
                            skyLightCount++;
                        } else
                        {
                            nonSkyLightCount++;
                        }
                    }
                    

                    if (!visitedPositions.Contains(npos))
                    {
                        bfsQueue.Enqueue(npos);
                        visitedPositions.Add(npos);
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
                Location = new Cuboidi(minx, miny, minz, maxx, maxy, maxz)
            };
        }
    }
}
