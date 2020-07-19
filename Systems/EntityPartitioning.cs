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

        Dictionary<long, GridAndChunkIndex> GridIndexByEntityId = new Dictionary<long, GridAndChunkIndex>();

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
            //api.Event.OnEntityDespawn += Event_OnEntityDespawn;
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
            GridIndexByEntityId.Clear();

            foreach (var val in entities)
            {
                LargestTouchDistance = Math.Max(LargestTouchDistance, (val.CollisionBox.X2 - val.CollisionBox.X1) / 2);

                PartitionEntity(val);
            }
        }


        private void PartitionEntity(Entity entity)
        {
            EntityPos pos = entity.SidedPos;

            int lgx = (int)(pos.X / gridSizeInBlocks) % partitionsLength;
            int lgz = (int)(pos.Z / gridSizeInBlocks) % partitionsLength;
            int gridIndex = lgz * partitionsLength + lgx;
            
            long nowInChunkIndex3d = MapUtil.Index3dL((int)pos.X / chunkSize, (int)pos.Y / chunkSize, (int)pos.Z / chunkSize, chunkMapSizeX, chunkMapSizeZ);

            EntityPartitionChunk partition;
            if (!Partitions.TryGetValue(nowInChunkIndex3d, out partition))
            {
                Partitions[nowInChunkIndex3d] = partition = new EntityPartitionChunk();
            }

            bool didChange = false;

            GridAndChunkIndex indexes;
            if (GridIndexByEntityId.TryGetValue(entity.EntityId, out indexes))
            {
                if (indexes.ChunkIndex != nowInChunkIndex3d)
                {
                    EntityPartitionChunk oldpartition = null;
                    Partitions.TryGetValue(indexes.ChunkIndex, out oldpartition);
                    oldpartition.Entities[indexes.GridIndex].Remove(entity);
                    didChange = true;
                }
                if (indexes.GridIndex != gridIndex)
                {
                    partition.Entities[indexes.GridIndex].Remove(entity);
                    didChange = true;
                }
            }
            else didChange = true;


            if (didChange && gridIndex >= 0) // Gridindex can be negative when a entity is outside world bounds 
            {
                partition.Entities[gridIndex].Add(entity);
                GridIndexByEntityId[entity.EntityId] = new GridAndChunkIndex(lgz * partitionsLength + lgx, nowInChunkIndex3d);
            }
        }



        /*private void Event_OnEntityDespawn(Entity entity, EntityDespawnReason reason)
        {
            GridAndChunkIndex indexes;
            if (GridIndexByEntityId.TryGetValue(entity.EntityId, out indexes))
            {
                GridIndexByEntityId.Remove(entity.EntityId);
            } else return;

            EntityPartitionChunk partition;
            if (Partitions.TryGetValue(indexes.ChunkIndex, out partition))
            {
                partition.Entities[indexes.GridIndex].Remove(entity); 
            }            
        }*/


        public Entity GetNearestEntity(Vec3d position, double radius, ActionConsumable<Entity> matches = null)
        {
            Entity nearestEntity = null;
            double nearestDistance = 999999;

            WalkEntities(position, radius, (e) =>
            {
                double dist = e.SidedPos.SquareDistanceTo(position);

                if (dist < nearestDistance && matches(e))
                {
                    nearestDistance = dist;
                    nearestEntity = e;
                }

                return true;
            });

            return nearestEntity;
        }


        /// <summary>
        /// This performs a entity search inside a spacially partioned search grid thats refreshed every 16ms.
        /// This can be a lot faster for when there are thousands of entities on a small space. It is used by EntityBehaviorRepulseAgents to improve performance, because otherwise when spawning 1000 creatures nearby, it has to do 1000x1000 = 1mil search operations every frame
        /// A small search grid allows us to ignore most of those during the search. 
        /// </summary>
        /// <param name="centerPos"></param>
        /// <param name="radius"></param>
        /// <param name="callback">Return false to stop the walk</param>
        public void WalkEntities(Vec3d centerPos, double radius, API.Common.ActionConsumable<Entity> callback)
        {
            int mingx = (int)((centerPos.X - radius) / gridSizeInBlocks);
            int maxgx = (int)((centerPos.X + radius) / gridSizeInBlocks);
            int mingy = (int)((centerPos.Y - radius) / gridSizeInBlocks);
            int maxgy = (int)((centerPos.Y + radius) / gridSizeInBlocks);
            int mingz = (int)((centerPos.Z - radius) / gridSizeInBlocks);
            int maxgz = (int)((centerPos.Z + radius) / gridSizeInBlocks);

            double radiusSq = radius * radius;

            long indexBefore = -1;
            IWorldChunk chunk = null;
            EntityPartitionChunk partitionChunk = null;

            int gridXMax = api.World.BlockAccessor.MapSizeX / gridSizeInBlocks;
            int gridYMax = api.World.BlockAccessor.MapSizeY / gridSizeInBlocks;
            int gridZMax = api.World.BlockAccessor.MapSizeZ / gridSizeInBlocks;

            for (int gridX = mingx; gridX <= maxgx; gridX++)
            {
                for (int gridY = mingy; gridY <= maxgy; gridY++)
                {
                    for (int gridZ = mingz; gridZ <= maxgz; gridZ++)
                    {
                        if (gridX < 0 || gridY < 0 || gridZ < 0 || gridX >= gridXMax || gridY >= gridYMax || gridZ >= gridZMax) continue;

                        int cx = gridX * gridSizeInBlocks / chunkSize;
                        int cy = gridY * gridSizeInBlocks / chunkSize;
                        int cz = gridZ * gridSizeInBlocks / chunkSize;

                        long index3d = MapUtil.Index3dL(cx, cy, cz, chunkMapSizeX, chunkMapSizeZ);

                        if (index3d != indexBefore)
                        {
                            chunk = api.World.BlockAccessor.GetChunk(cx, cy, cz);
                            Partitions.TryGetValue(index3d, out partitionChunk);
                            indexBefore = index3d;
                        }

                        if (chunk == null || chunk.Entities == null || partitionChunk == null) continue;

                        int lgx = gridX % partitionsLength;
                        int lgz = gridZ % partitionsLength;


                        List<Entity> entities = partitionChunk.Entities[lgz * partitionsLength + lgx];
                        for (int i = 0; i < entities.Count; i++)
                        {
                            double distSq = entities[i].SidedPos.SquareDistanceTo(centerPos);
                            if (distSq <= radiusSq)
                            {
                                if (!callback(entities[i]))
                                {
                                    return;
                                }
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Same as <see cref="WalkEntities(Vec3d, double, API.Common.Action{Entity})"/> but does no exact radius distance check, walks all entities that it finds in the grid
        /// </summary>
        /// <param name="centerPos"></param>
        /// <param name="radius"></param>
        /// <param name="callback"></param>
        public void WalkEntityPartitions(Vec3d centerPos, double radius, API.Common.Action<Entity> callback)
        {
            int mingx = (int)((centerPos.X - radius) / gridSizeInBlocks);
            int maxgx = (int)((centerPos.X + radius) / gridSizeInBlocks);
            int mingy = (int)((centerPos.Y - radius) / gridSizeInBlocks);
            int maxgy = (int)((centerPos.Y + radius) / gridSizeInBlocks);
            int mingz = (int)((centerPos.Z - radius) / gridSizeInBlocks);
            int maxgz = (int)((centerPos.Z + radius) / gridSizeInBlocks);
            

            int cxBefore = -99, cyBefore = -99, czBefore = -99;
            IWorldChunk chunk = null;
            EntityPartitionChunk partitionChunk = null;

            int gridXMax = api.World.BlockAccessor.MapSizeX / gridSizeInBlocks;
            int gridYMax = api.World.BlockAccessor.MapSizeX / gridSizeInBlocks;
            int gridZMax = api.World.BlockAccessor.MapSizeX / gridSizeInBlocks;

            for (int gridX = mingx; gridX <= maxgx; gridX++)
            {
                for (int gridY = mingy; gridY <= maxgy; gridY++)
                {
                    for (int gridZ = mingz; gridZ <= maxgz; gridZ++)
                    {
                        if (gridX < 0 || gridY < 0 || gridZ < 0 || gridX >= gridXMax || gridY >= gridYMax || gridZ >= gridZMax) continue;

                        int cx = gridX * gridSizeInBlocks / chunkSize;
                        int cy = gridY * gridSizeInBlocks / chunkSize;
                        int cz = gridZ * gridSizeInBlocks / chunkSize;

                        long index3d = MapUtil.Index3dL(cx, cy, cz, chunkMapSizeX, chunkMapSizeZ);

                        if (cx != cxBefore || cy != cyBefore || cz != czBefore)
                        {
                            chunk = api.World.BlockAccessor.GetChunk(cx, cy, cz);
                            Partitions.TryGetValue(index3d, out partitionChunk);
                        }
                        if (chunk == null || chunk.Entities == null || partitionChunk == null) continue;

                        cxBefore = cx;
                        cyBefore = cy;
                        czBefore = cz;

                        int lgx = gridX % partitionsLength;
                        int lgz = gridZ % partitionsLength;
                        
                        List<Entity> entities = partitionChunk.Entities[lgz * partitionsLength + lgx];
                        for (int i = 0; i < entities.Count; i++)
                        {
                            callback(entities[i]);
                        }
                    }
                }
            }
        }

    }
}
