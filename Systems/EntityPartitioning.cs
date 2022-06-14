using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class EntityPartitionChunk
    {
        public List<Entity>[] Entities;

        public EntityPartitionChunk()
        {
            Entities = new List<Entity>[EntityPartitioning.partitionsLength * EntityPartitioning.partitionsLength];

            for (int x = 0; x < EntityPartitioning.partitionsLength; x++)
            {
                for (int z = 0; z < EntityPartitioning.partitionsLength; z++)
                {
                    Entities[z * EntityPartitioning.partitionsLength + x] = new List<Entity>();
                }
            }
        }
    }

    public struct GridAndChunkIndex {
        public int GridIndex;
        public long ChunkIndex;

        public GridAndChunkIndex(int gridIndex, long chunkIndex)
        {
            this.GridIndex = gridIndex;
            this.ChunkIndex = chunkIndex;
        }
    }


    public class EntityPartitioning : ModSystem
    {
        public static int partitionsLength = 8;
        int gridSizeInBlocks;

        ICoreAPI api;
        ICoreClientAPI capi;
        ICoreServerAPI sapi;

        public Dictionary<long, EntityPartitionChunk> Partitions = new Dictionary<long, EntityPartitionChunk>();

        int chunkSize;
        int chunkMapSizeX;
        int chunkMapSizeZ;


        /// <summary>
        /// Updated every frame. The largest hitbox length of all loaded entities.
        /// </summary>
        public double LargestTouchDistance;

        public override double ExecuteOrder()
        {
            return 0;
        }

        public override bool ShouldLoad(EnumAppSide side)
        {
            return true;
        }
        

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            chunkSize = api.World.BlockAccessor.ChunkSize;

            gridSizeInBlocks = chunkSize / partitionsLength;
        }



        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;
            api.Event.RegisterGameTickListener(OnClientTick, 32);
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            this.sapi = api;
            api.Event.RegisterGameTickListener(OnServerTick, 32);
        }

        private void OnClientTick(float dt)
        {
            partitionEntities(capi.World.LoadedEntities.Values);
        }

        private void OnServerTick(float dt)
        {
            partitionEntities(sapi.World.LoadedEntities.Values);
        }

        void partitionEntities(ICollection<Entity> entities)
        {
            chunkMapSizeX = api.World.BlockAccessor.MapSizeX / chunkSize;
            chunkMapSizeZ = api.World.BlockAccessor.MapSizeZ / chunkSize;
            LargestTouchDistance = 0;

            Partitions.Clear();

            foreach (var val in entities)
            {
                LargestTouchDistance = Math.Max(LargestTouchDistance, val.SelectionBox.XSize / 2);

                PartitionEntity(val);
            }
        }


        private void PartitionEntity(Entity entity)
        {
            EntityPos pos = entity.SidedPos;

            int lgx = ((int)pos.X / gridSizeInBlocks) % partitionsLength;
            int lgz = ((int)pos.Z / gridSizeInBlocks) % partitionsLength;
            int gridIndex = lgz * partitionsLength + lgx;
            
            long nowInChunkIndex3d = MapUtil.Index3dL((int)pos.X / chunkSize, (int)pos.Y / chunkSize, (int)pos.Z / chunkSize, chunkMapSizeX, chunkMapSizeZ);

            EntityPartitionChunk partition;
            if (!Partitions.TryGetValue(nowInChunkIndex3d, out partition))
            {
                Partitions[nowInChunkIndex3d] = partition = new EntityPartitionChunk();
            }

            if (gridIndex < 0) return;

            partition.Entities[gridIndex].Add(entity);
        }

        public Entity GetNearestEntity(Vec3d position, double radius, ActionConsumable<Entity> matches = null)
        {
            Entity nearestEntity = null;
            double radiusSq = radius * radius;
            double nearestDistanceSq = radiusSq;

            if (api.Side == EnumAppSide.Client)
            {
                WalkEntityPartitions(position, radius, (e) =>
                {
                    double distSq = e.Pos.SquareDistanceTo(position);

                    if (distSq < nearestDistanceSq && matches(e))
                    {
                        nearestDistanceSq = distSq;
                        nearestEntity = e;
                    }

                    return true;
                });
            } else
            {
                WalkEntityPartitions(position, radius, (e) =>
                {
                    double distSq = e.ServerPos.SquareDistanceTo(position);

                    if (distSq < nearestDistanceSq && matches(e))
                    {
                        nearestDistanceSq = distSq;
                        nearestEntity = e;
                    }

                    return true;
                });
            }

            

            return nearestEntity;
        }



        public delegate bool RangeTestDelegate(Entity e, Vec3d pos, double radiuSq);

        private bool onIsInRangeServer(Entity e, Vec3d pos, double radiusSq)
        {
            double dx = e.ServerPos.X - pos.X;
            double dy = e.ServerPos.Y - pos.Y;
            double dz = e.ServerPos.Z - pos.Z;

            return (dx * dx + dy * dy + dz * dz) < radiusSq;
        }

        private bool onIsInRangeClient(Entity e, Vec3d pos, double radiusSq)
        {
            double dx = e.Pos.X - pos.X;
            double dy = e.Pos.Y - pos.Y;
            double dz = e.Pos.Z - pos.Z;

            return (dx * dx + dy * dy + dz * dz) < radiusSq;
        }

        private bool onIsInRangePartition(Entity e, Vec3d pos, double radiusSq)
        {
            return true;
        }


        /// <summary>
        /// This performs a entity search inside a spacially partioned search grid thats refreshed every 16ms.
        /// This can be a lot faster for when there are thousands of entities on a small space. It is used by EntityBehaviorRepulseAgents to improve performance, because otherwise when spawning 1000 creatures nearby, it has to do 1000x1000 = 1mil search operations every frame
        /// A small search grid allows us to ignore most of those during the search.  Return false to stop the walk.
        /// </summary>
        /// <param name="centerPos"></param>
        /// <param name="radius"></param>
        /// <param name="callback">Return false to stop the walk</param>
        public void WalkEntities(Vec3d centerPos, double radius, ActionConsumable<Entity> callback)
        {
            if (api.Side == EnumAppSide.Client)
            {
                WalkEntities(centerPos, radius, callback, onIsInRangeClient);
            } else
            {
                WalkEntities(centerPos, radius, callback, onIsInRangeServer);
            }
        }

        /// <summary>
        /// Same as <see cref="WalkEntities(Vec3d, double, Action{Entity})"/> but does no exact radius distance check, walks all entities that it finds in the grid
        /// </summary>
        /// <param name="centerPos"></param>
        /// <param name="radius"></param>
        /// <param name="callback"></param>
        public void WalkEntityPartitions(Vec3d centerPos, double radius, ActionConsumable<Entity> callback)
        {
            WalkEntities(centerPos, radius, callback, onIsInRangePartition);
        }


        private void WalkEntities(Vec3d centerPos, double radius, ActionConsumable<Entity> callback, RangeTestDelegate onRangeTest)
        {
            int gridXMax = api.World.BlockAccessor.MapSizeX / gridSizeInBlocks - 1;
            int cyTop = api.World.BlockAccessor.MapSizeY / chunkSize - 1;
            int gridZMax = api.World.BlockAccessor.MapSizeZ / gridSizeInBlocks - 1;

            int mingx = (int)GameMath.Clamp((centerPos.X - radius) / gridSizeInBlocks, 0, gridXMax);
            int maxgx = (int)GameMath.Clamp((centerPos.X + radius) / gridSizeInBlocks, 0, gridXMax);

            int mincy = (int)GameMath.Clamp((centerPos.Y - radius) / chunkSize, 0, cyTop);
            int maxcy = (int)GameMath.Clamp((centerPos.Y + radius) / chunkSize, 0, cyTop);

            int mingz = (int)GameMath.Clamp((centerPos.Z - radius) / gridSizeInBlocks, 0, gridZMax);
            int maxgz = (int)GameMath.Clamp((centerPos.Z + radius) / gridSizeInBlocks, 0, gridZMax);

            double radiusSq = radius * radius;

            long indexBefore = -1;
            IWorldChunk chunk = null;
            EntityPartitionChunk partitionChunk = null;

            for (int gridX = mingx; gridX <= maxgx; gridX++)
            {
                int cx = gridX * gridSizeInBlocks / chunkSize;
                int lgx = gridX % partitionsLength;

                for (int gridZ = mingz; gridZ <= maxgz; gridZ++)
                {
                    int cz = gridZ * gridSizeInBlocks / chunkSize;
                    int lgz = gridZ % partitionsLength;
                    lgz = lgz * partitionsLength + lgx;

                    for (int cy = mincy; cy <= maxcy; cy++)
                    {
                        long index3d = MapUtil.Index3dL(cx, cy, cz, chunkMapSizeX, chunkMapSizeZ);

                        if (index3d != indexBefore)
                        {
                            indexBefore = index3d;
                            chunk = api.World.BlockAccessor.GetChunk(cx, cy, cz);
                            if (chunk == null || chunk.Entities == null) continue;
                            Partitions.TryGetValue(index3d, out partitionChunk);
                        }
                        else if (chunk == null || chunk.Entities == null) continue;

                        if (partitionChunk == null) continue;

                        List<Entity> entities = partitionChunk.Entities[lgz];

                        for (int i = 0; i < entities.Count; i++)
                        {
                            if (onRangeTest(entities[i], centerPos, radiusSq) && !callback(entities[i]))
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }

    }
}
