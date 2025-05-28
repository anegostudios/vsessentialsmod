using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public interface ICustomRepulseBehavior
    {
        bool Repulse(Entity entity, Vec3d pushVector);
    }

    public class EntityBehaviorEllipsoidalRepulseAgents : EntityBehaviorRepulseAgents, ICustomRepulseBehavior
    {
        protected Vec3d offset;
        protected Vec3d radius;

        public EntityBehaviorEllipsoidalRepulseAgents(Entity entity) : base(entity)
        {
            entity.customRepulseBehavior = true;
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
             base.Initialize(properties, attributes);

            offset = attributes["offset"].AsObject<Vec3d>(new Vec3d());
            radius = attributes["radius"].AsObject<Vec3d>();
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            touchdist = Math.Max(radius.X, radius.Z);
            entity.BHRepulseAgents = this;                  // These two lines are needed because we do not call the base.AfterInitialized()
            entity.AfterPhysicsTick = AfterPhysicsTick;
        }

        public override void UpdateColSelBoxes()
        {
            touchdist = Math.Max(radius.X, radius.Z);
        }


        public override float GetTouchDistance(ref EnumHandling handling)
        {
            handling = EnumHandling.PreventDefault;
            return (float)Math.Max(radius.X, radius.Z) + 0.5f;
        }


        public bool Repulse(Entity e, Vec3d pushVector)
        {
            if (e.BHRepulseAgents is not EntityBehaviorRepulseAgents theirRepulse) return true;
            var ourEntity = this.entity;
            var radius = this.radius;

            double theirRepulseY = theirRepulse.ownPosRepulseY;
            if (theirRepulseY > ownPosRepulseY + radius.Y || ownPosRepulseY > theirRepulseY + e.SelectionBox.Height)
            {
                return true;
            }

            double dx = ownPosRepulseX - theirRepulse.ownPosRepulseX;
            double dz = ownPosRepulseZ - theirRepulse.ownPosRepulseZ;

            var yaw = ourEntity.ServerPos.Yaw;

            double dist = RelDistanceToEllipsoid(dx, dz, radius.X, radius.Z, yaw);
            if (dist >= 1) return true;

            double pushForce = -1 * (1 - dist);
            double px = dx * pushForce;
            double py = 0;
            double pz = dz * pushForce;

            var mySize = ourEntity.SelectionBox.Length * ourEntity.SelectionBox.Height;

            float hisSize = e.SelectionBox.Length * e.SelectionBox.Height;

            float pushDiff = GameMath.Clamp(hisSize / mySize, 0, 1)/1.5f;
            if (e.OnGround) pushDiff *= 10f;

            pushVector.Add(px * pushDiff, py * pushDiff * 0.75, pz * pushDiff);

            return true;
        }

        public double RelDistanceToEllipsoid(double x, double z, double wdt, double len, double yaw)
        {
            var th = yaw;
            // Apply inverse rotation to the point
            double xPrime = x * Math.Cos(th) - z * Math.Sin(th);
            //double yPrime = y; // No change in the y-coordinate
            double zPrime = x * Math.Sin(th) + z * Math.Cos(th);

            xPrime += offset.X;
            zPrime += offset.Z;

            // Calculate the squared distances
            double distanceSquared = (xPrime * xPrime) / (wdt * wdt) + (zPrime * zPrime) / (len * len);

            return distanceSquared;
        }
    }

    public class EntityBehaviorRepulseAgents : EntityBehavior
    {
        protected Vec3d pushVector = new Vec3d();
        protected EntityPartitioning partitionUtil;
        protected bool movable = true;
        protected bool ignorePlayers = false;

        protected double touchdist;
        public override bool ThreadSafe { get { return true; } }

        IClientWorldAccessor cworld;

        public EntityBehaviorRepulseAgents(Entity entity) : base(entity)
        {
            entity.hasRepulseBehavior = true;
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            movable = attributes["movable"].AsBool(true);
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();
            ignorePlayers = entity is EntityPlayer && entity.World.Config.GetAsBool("player2PlayerCollisions", true);

            cworld = entity.World as IClientWorldAccessor;
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            touchdist = entity.touchDistance;
            entity.BHRepulseAgents = this;
            entity.AfterPhysicsTick = AfterPhysicsTick;
        }

        public double ownPosRepulseX, ownPosRepulseY, ownPosRepulseZ;
        public float mySize;
        protected int dimension;

        public override void UpdateColSelBoxes()
        {
            touchdist = entity.touchDistance;
        }

        public void AfterPhysicsTick()
        {
            var entity = this.entity;
            var SidedPos = entity.SidedPos;
            var CollisionBox = entity.CollisionBox;
            var OriginCollisionBox = entity.OriginCollisionBox;
            ownPosRepulseX = SidedPos.X + (CollisionBox.X2 - OriginCollisionBox.X2);
            ownPosRepulseY = SidedPos.Y + (CollisionBox.Y2 - OriginCollisionBox.Y2);
            ownPosRepulseZ = SidedPos.Z + (CollisionBox.Z2 - OriginCollisionBox.Z2);
        }

        public override void OnGameTick(float deltaTime)
        {
            var entity = this.entity;
            if (entity.State == EnumEntityState.Inactive || !entity.IsInteractable || !movable) return;
            EntityAgent eagent = entity as EntityAgent;
            if (eagent?.MountedOn != null) return;
            if (entity.World.ElapsedMilliseconds < 2000) return;

            var pushVector = this.pushVector;
            pushVector.Set(0, 0, 0);

            mySize = entity.SelectionBox.Length * entity.SelectionBox.Height * (eagent != null && eagent.Controls.Sneak ? 2 : 1);
            dimension = entity.ServerPos.Dimension;

            if (cworld != null && entity != cworld.Player.Entity)
            {
                WalkEntity(cworld.Player.Entity);
            }
            else
            {
                partitionUtil.WalkEntities(ownPosRepulseX, ownPosRepulseY, ownPosRepulseZ, touchdist + partitionUtil.LargestTouchDistance + 0.1, WalkEntity, IsInRangePartition, EnumEntitySearchType.Creatures);
            }

            if (pushVector.X == 0 && pushVector.Z == 0 && pushVector.Y == 0) return;

            pushVector.X = GameMath.Clamp(pushVector.X, -3, 3) / 30;
            pushVector.Y = GameMath.Clamp(pushVector.Y, -3, 0.5) / 30;
            pushVector.Z = GameMath.Clamp(pushVector.Z, -3, 3) / 30;

            if (cworld != null && entity == cworld.Player.Entity)
            {
                // Necessary in 1.20 because currently BehaviorPlayerPhysics clientside calls SimPhysics on entity.SidedPos
                entity.SidedPos.Motion.Add(pushVector);
            }
            else
            {
                entity.ServerPos.Motion.Add(pushVector);
            }
        }


        private bool WalkEntity(Entity e)
        {
            var ourEntity = this.entity;
            if (e == ourEntity || (e.BHRepulseAgents is not EntityBehaviorRepulseAgents theirRepulse) || !e.IsInteractable || (ignorePlayers && e is EntityPlayer)) return true;
            if (e is EntityAgent eagent && eagent.MountedOn?.Entity == ourEntity) return true;
            if (e.ServerPos.Dimension != dimension) return true;

            if (theirRepulse is ICustomRepulseBehavior custom)
            {
                return custom.Repulse(ourEntity, pushVector);
            }

            double dx = ownPosRepulseX - theirRepulse.ownPosRepulseX;
            double dy = ownPosRepulseY - theirRepulse.ownPosRepulseY;
            double dz = ownPosRepulseZ - theirRepulse.ownPosRepulseZ;

            double distSq = dx * dx + dy * dy + dz * dz;
            double minDistSq = ourEntity.touchDistanceSq + e.touchDistanceSq;

            if (distSq >= minDistSq) return true;
            
            double pushForce = (1 - distSq / minDistSq) / Math.Max(0.001f, GameMath.Sqrt(distSq));
            double px = dx * pushForce;
            double py = dy * pushForce;
            double pz = dz * pushForce;

            float theirSize = e.SelectionBox.Length * e.SelectionBox.Height;

            float pushDiff = GameMath.Clamp(theirSize / mySize, 0, 1);

            if (ourEntity.OnGround) pushDiff *= 3;
            
            pushVector.Add(px * pushDiff, py * pushDiff * 0.75, pz * pushDiff);

            return true;
        }


        public override string PropertyName()
        {
            return "repulseagents";
        }


        private bool IsInRangePartition(Entity e, double posX, double posY, double posZ, double radiusSq)
        {
            return true;
        }
    }
}
