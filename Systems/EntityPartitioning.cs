using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class EntityPartitionChunk
    {
        public List<Entity>[] Entities;
        public List<Entity>[] InanimateEntities;

        public EntityPartitionChunk()
        {
            Entities = new List<Entity>[EntityPartitioning.partitionsLength * EntityPartitioning.partitionsLength];
        }

        public List<Entity> Add(Entity e, int gridIndex)
        {
            List<Entity> list = e.IsCreature ?
                FetchOrCreateList(ref Entities[gridIndex]) :
                FetchOrCreateList(ref (InanimateEntities ??= new List<Entity>[EntityPartitioning.partitionsLength * EntityPartitioning.partitionsLength])[gridIndex]);
            list.Add(e);
            return list;
        }

        private List<Entity> FetchOrCreateList(ref List<Entity> list)
        {
            return list ??= new List<Entity>(4);
        }
    }

    public struct GridAndChunkIndex
    {
        public int GridIndex;
        public long ChunkIndex;

        public GridAndChunkIndex(int gridIndex, long chunkIndex)
        {
            this.GridIndex = gridIndex;
            this.ChunkIndex = chunkIndex;
        }
    }


    public enum EnumEntitySearchType
    {
        Creatures = 0,
        Inanimate = 1,
    }


    public class EntityPartitioning : ModSystem, IEntityPartitioning
    {
        public const int partitionsLength = 4;
        private const int gridSizeInBlocks = chunkSize / partitionsLength;

        ICoreAPI api;
        ICoreClientAPI capi;
        ICoreServerAPI sapi;

        public Dictionary<long, EntityPartitionChunk> Partitions = new Dictionary<long, EntityPartitionChunk>();

        const int chunkSize = GlobalConstants.ChunkSize;


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

            api.Event.PlayerDimensionChanged += Event_PlayerDimensionChanged;
        }

        private void Event_PlayerDimensionChanged(IPlayer byPlayer)
        {
            RePartitionPlayer(byPlayer.Entity);
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
            api.Event.PlayerSwitchGameMode += OnSwitchedGameMode;
        }

        private void OnClientTick(float dt)
        {
            PartitionEntities(capi.World.LoadedEntities.Values);
        }

        private void OnServerTick(float dt)
        {
            PartitionEntities(((CachingConcurrentDictionary<long, Entity>)sapi.World.LoadedEntities).Values);
        }

        void PartitionEntities(ICollection<Entity> entities)
        {
            long chunkMapSizeX = api.World.BlockAccessor.MapSizeX / chunkSize;
            long chunkMapSizeZ = api.World.BlockAccessor.MapSizeZ / chunkSize;
            double largestTouchDistance = 0;

            var Partitions = this.Partitions;
            Partitions.Clear();

            foreach (var entity in entities)
            {
                if (entity.IsCreature)
                {
                    if (entity.touchDistance > largestTouchDistance) largestTouchDistance = entity.touchDistance;
                }

                EntityPos pos = entity.SidedPos;

                int x = (int)pos.X;
                int z = (int)pos.Z;
                int gridIndex = (z / gridSizeInBlocks) % partitionsLength * partitionsLength + (x / gridSizeInBlocks) % partitionsLength;
                if (gridIndex < 0) continue;    // entities could be outside the map edge

                long nowInChunkIndex3d = MapUtil.Index3dL(x / chunkSize, (int)pos.Y / chunkSize, z / chunkSize, chunkMapSizeX, chunkMapSizeZ);

                if (!Partitions.TryGetValue(nowInChunkIndex3d, out EntityPartitionChunk partition))
                {
                    Partitions[nowInChunkIndex3d] = partition = new EntityPartitionChunk();
                }

                var list = partition.Add(entity, gridIndex);
                if (entity is EntityPlayer ep) ep.entityListForPartitioning = list;
            }

            this.LargestTouchDistance = largestTouchDistance;   // Only write to the field when we finished the operation, there could be 10k entities
        }


        public void RePartitionPlayer(EntityPlayer entity)
        {
            entity.entityListForPartitioning?.Remove(entity);
            PartitionEntities(new Entity[] { entity });
        }

        private void OnSwitchedGameMode(IServerPlayer player)
        {
            RePartitionPlayer(player.Entity);
        }

        [Obsolete("In version 1.19.2 and later, this searches only entities which are Creatures, which is probably what the caller wants but you should specify EnumEntitySearchType explicitly")]
        public Entity GetNearestEntity(Vec3d position, double radius, ActionConsumable<Entity> matches = null)
        {
            return GetNearestEntity(position, radius, matches, EnumEntitySearchType.Creatures);
        }

        /// <summary>
        /// Search all nearby creatures to find the nearest one which is Interactable
        /// </summary>
        public Entity GetNearestInteractableEntity(Vec3d position, double radius, ActionConsumable<Entity> matches = null)
        {
            if (matches == null)
            {
                return GetNearestEntity(position, radius, (e) => e.IsInteractable, EnumEntitySearchType.Creatures);
            }
            return GetNearestEntity(position, radius, (e) => matches(e) && e.IsInteractable, EnumEntitySearchType.Creatures);
        }

        /// <summary>
        /// Search all nearby entities (either Creatures or Inanimate, according to searchType) to find the nearest one meeting the "matches" condition
        /// </summary>
        public Entity GetNearestEntity(Vec3d position, double radius, ActionConsumable<Entity> matches, EnumEntitySearchType searchType)
        {
            Entity nearestEntity = null;
            double radiusSq = radius * radius;
            double nearestDistanceSq = radiusSq;

            if (api.Side == EnumAppSide.Client)
            {
                WalkEntities(position.X, position.Y, position.Z, radius, (e) =>
                {
                    double distSq = e.Pos.SquareDistanceTo(position);

                    if (distSq < nearestDistanceSq && matches(e))
                    {
                        nearestDistanceSq = distSq;
                        nearestEntity = e;
                    }

                    return true;
                }, onIsInRangePartition, searchType);
            } else
            {
                WalkEntities(position.X, position.Y, position.Z, radius, (e) =>
                {
                    double distSq = e.ServerPos.SquareDistanceTo(position);

                    if (distSq < nearestDistanceSq && matches(e))
                    {
                        nearestDistanceSq = distSq;
                        nearestEntity = e;
                    }

                    return true;
                }, onIsInRangePartition, searchType);
            }

            return nearestEntity;
        }



        public delegate bool RangeTestDelegate(Entity e, double posX, double posY, double posZ, double radiuSq);

        private bool onIsInRangeServer(Entity e, double posX, double posY, double posZ, double radiusSq)
        {
            var ePos = e.ServerPos;
            double dx = ePos.X - posX;
            double dy = ePos.Y - posY;
            double dz = ePos.Z - posZ;

            return (dx * dx + dy * dy + dz * dz) < radiusSq;
        }

        private bool onIsInRangeClient(Entity e, double posX, double posY, double posZ, double radiusSq)
        {
            var ePos = e.Pos;
            double dx = ePos.X - posX;
            double dy = ePos.Y - posY;
            double dz = ePos.Z - posZ;

            return (dx * dx + dy * dy + dz * dz) < radiusSq;
        }

        private bool onIsInRangePartition(Entity e, double posX, double posY, double posZ, double radiusSq)
        {
            return true;
        }


        [Obsolete("In version 1.19.2 and later, this walks through Creature entities only, so recommended to call WalkEntityPartitions() specifying the type of search explicitly for clarity in the calling code")]
        public void WalkEntities(Vec3d centerPos, double radius, ActionConsumable<Entity> callback)
        {
            WalkEntities(centerPos, radius, callback, EnumEntitySearchType.Creatures);
        }

        [Obsolete("In version 1.19.2 and later, use WalkEntities specifying the searchtype (Creatures or Inanimate) explitly in the calling code.")]
        public void WalkInteractableEntities(Vec3d centerPos, double radius, ActionConsumable<Entity> callback)
        {
            WalkEntities(centerPos, radius, callback, EnumEntitySearchType.Creatures);
        }

        /// <summary>
        /// This performs a entity search inside a spacially partioned search grid thats refreshed every 16ms, limited to Creature entities only for performance reasons.
        /// This can be a lot faster for when there are thousands of entities on a small space. It is used by EntityBehaviorRepulseAgents to improve performance, because otherwise when spawning 1000 creatures nearby, it has to do 1000x1000 = 1mil search operations every frame
        /// A small search grid allows us to ignore most of those during the search.  Return false to stop the walk.
        /// <br/>Note in 1.19.2 onwards we do not do an Interactable check here, calling code must check Interactable if required (e.g. Bees, and player in Spectator mode, are not Interactable)
        /// </summary>
        /// <param name="centerPos"></param>
        /// <param name="radius"></param>
        /// <param name="callback">Return false to stop the walk</param>
        /// <param name="searchType">Creatures or Inanimate</param>
        public void WalkEntities(Vec3d centerPos, double radius, ActionConsumable<Entity> callback, EnumEntitySearchType searchType)
        {
            if (api.Side == EnumAppSide.Client)
            {
                WalkEntities(centerPos.X, centerPos.Y, centerPos.Z, radius, callback, onIsInRangeClient, searchType);
            } else
            {
                WalkEntities(centerPos.X, centerPos.Y, centerPos.Z, radius, callback, onIsInRangeServer, searchType);
            }
        }

        /// <summary>
        /// Same as <see cref="WalkEntities(Vec3d,double,Vintagestory.API.Common.ActionConsumable{Vintagestory.API.Common.Entities.Entity}(Vintagestory.API.Common.Entities.Entity))"/> but does no exact radius distance check, walks all entities that it finds in the grid
        /// </summary>
        /// <param name="centerPos"></param>
        /// <param name="radius"></param>
        /// <param name="callback"></param>
        public void WalkEntityPartitions(Vec3d centerPos, double radius, ActionConsumable<Entity> callback)
        {
            WalkEntities(centerPos.X, centerPos.Y, centerPos.Z, radius, callback, onIsInRangePartition, EnumEntitySearchType.Creatures);
        }


        public void WalkEntities(double centerPosX, double centerPosY, double centerPosZ, double radius, ActionConsumable<Entity> callback, RangeTestDelegate onRangeTest, EnumEntitySearchType searchType)
        {
            var blockAccessor = api.World.BlockAccessor;
            long chunkMapSizeX = blockAccessor.MapSizeX / chunkSize;
            long chunkMapSizeZ = blockAccessor.MapSizeZ / chunkSize;

            int dimension = (int)centerPosY / BlockPos.DimensionBoundary;
            double trueY = centerPosY - dimension * BlockPos.DimensionBoundary;

            int gridXMax = blockAccessor.MapSizeX / gridSizeInBlocks - 1;
            int cyTop = blockAccessor.MapSizeY / chunkSize - 1;
            int gridZMax = blockAccessor.MapSizeZ / gridSizeInBlocks - 1;

            int mingx = (int)GameMath.Clamp((centerPosX - radius) / gridSizeInBlocks, 0, gridXMax);
            int maxgx = (int)GameMath.Clamp((centerPosX + radius) / gridSizeInBlocks, 0, gridXMax);

            int mincy = (int)GameMath.Clamp((trueY - radius) / chunkSize, 0, cyTop);
            int maxcy = (int)GameMath.Clamp((trueY + radius) / chunkSize, 0, cyTop);

            int mingz = (int)GameMath.Clamp((centerPosZ - radius) / gridSizeInBlocks, 0, gridZMax);
            int maxgz = (int)GameMath.Clamp((centerPosZ + radius) / gridSizeInBlocks, 0, gridZMax);

            double radiusSq = radius * radius;

            var Partitions = this.Partitions;
            EntityPartitionChunk partitionChunk = null;
            long index3d;
            long lastIndex3d = -1;

            for (int cy = mincy; cy <= maxcy; cy++)
            {
                for (int gridX = mingx; gridX <= maxgx; gridX++)
                {
                    int cx = gridX * gridSizeInBlocks / chunkSize;
                    int lgx = gridX % partitionsLength;

                    for (int gridZ = mingz; gridZ <= maxgz; gridZ++)
                    {
                        int cz = gridZ * gridSizeInBlocks / chunkSize;

                        index3d = MapUtil.Index3dL(cx, cy, cz, chunkMapSizeX, chunkMapSizeZ);
                        if (index3d != lastIndex3d)
                        {
                            lastIndex3d = index3d;
                            Partitions.TryGetValue(index3d, out partitionChunk);
                        }
                        if (partitionChunk == null) continue;

                        int index = (gridZ % partitionsLength) * partitionsLength + lgx;
                        List<Entity> entities = searchType == EnumEntitySearchType.Creatures ? partitionChunk.Entities[index] : partitionChunk.InanimateEntities?[index];
                        if (entities == null) continue;

                        foreach (Entity entity in entities)
                        {
                            if (entity.Pos.Dimension != dimension) continue;
                            if (onRangeTest(entity, centerPosX, centerPosY, centerPosZ, radiusSq) && !callback(entity))   // continues looping entities and calling the callback, but stops if the callback returns false
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
