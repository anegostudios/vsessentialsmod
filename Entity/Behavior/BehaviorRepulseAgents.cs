using System;
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

            offset = attributes["offset"].AsObject<Vec3d>();
            radius = attributes["radius"].AsObject<Vec3d>();
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            touchdist = Math.Max(radius.X, radius.Z);
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
            var ownPosRepulseY = entity.ownPosRepulse.Y + entity.Pos.DimensionYAdjustment;

            if (e.ownPosRepulse.Y > ownPosRepulseY+radius.Y || ownPosRepulseY > e.ownPosRepulse.Y + e.SelectionBox.Height)
            {
                return true;
            }

            var ownPosRepulseX = entity.ownPosRepulse.X;
            var ownPosRepulseZ = entity.ownPosRepulse.Z;

            double dx = ownPosRepulseX - e.ownPosRepulse.X;
            double dz = ownPosRepulseZ - e.ownPosRepulse.Z;

            var yaw = entity.Pos.Yaw;

            double dist = RelDistanceToEllipsoid(dx, dz, radius.X, radius.Z, yaw);
            if (dist >= 1) return true;

            double pushForce = -1 * (1 - dist);
            double px = dx * pushForce;
            double py = 0;
            double pz = dz * pushForce;

            var mySize = entity.SelectionBox.Length * entity.SelectionBox.Height;

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
        protected EntityAgent selfEagent;

        protected double touchdist;

        public EntityBehaviorRepulseAgents(Entity entity) : base(entity)
        {
            entity.hasRepulseBehavior = true;

            selfEagent = entity as EntityAgent;
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            movable = attributes["movable"].AsBool(true);
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();
            ignorePlayers = entity is EntityPlayer && entity.World.Config.GetAsBool("player2PlayerCollisions", true);
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            touchdist = entity.SelectionBox.XSize * 2;
        }

        protected double ownPosRepulseX, ownPosRepulseY, ownPosRepulseZ;
        protected float mySize;

        public override void UpdateColSelBoxes()
        {
            touchdist = entity.SelectionBox.XSize * 2;
        }


        public override void OnGameTick(float deltaTime)
        {
            if (entity.State == EnumEntityState.Inactive || !entity.IsInteractable || !movable || (entity is EntityAgent eagent && eagent.MountedOn != null)) return;
            if (entity.World.ElapsedMilliseconds < 2000) return;

            pushVector.Set(0, 0, 0);

            ownPosRepulseX = entity.ownPosRepulse.X;
            ownPosRepulseY = entity.ownPosRepulse.Y + entity.Pos.DimensionYAdjustment;
            ownPosRepulseZ = entity.ownPosRepulse.Z;
            mySize = entity.SelectionBox.Length * entity.SelectionBox.Height;

            if (selfEagent != null && selfEagent.Controls.Sneak) mySize *= 2;

            partitionUtil.WalkEntityPartitions(entity.ownPosRepulse, touchdist + partitionUtil.LargestTouchDistance + 0.1, WalkEntity);

            pushVector.X = GameMath.Clamp(pushVector.X, -3, 3);
            pushVector.Y = GameMath.Clamp(pushVector.Y, -3, 0.5);
            pushVector.Z = GameMath.Clamp(pushVector.Z, -3, 3);

            entity.SidedPos.Motion.Add(pushVector.X / 30, pushVector.Y / 30, pushVector.Z / 30);
        }


        private bool WalkEntity(Entity e)
        {
            if (!e.hasRepulseBehavior || !e.IsInteractable || e == entity || (ignorePlayers && e is EntityPlayer)) return true;
            if (e is EntityAgent eagent && eagent.MountedOn?.Entity == entity) return true;

            if (e.customRepulseBehavior)
            {
                return e.GetInterface<ICustomRepulseBehavior>().Repulse(entity, pushVector);
            }

            double dx = ownPosRepulseX - e.ownPosRepulse.X;
            double dy = ownPosRepulseY - e.ownPosRepulse.Y;
            double dz = ownPosRepulseZ - e.ownPosRepulse.Z;

            double distSq = dx * dx + dy * dy + dz * dz;
            double minDistSq = entity.touchDistanceSq + e.touchDistanceSq;

            if (distSq >= minDistSq) return true;
            
            double pushForce = (1 - distSq / minDistSq) / Math.Max(0.001f, GameMath.Sqrt(distSq));
            double px = dx * pushForce;
            double py = dy * pushForce;
            double pz = dz * pushForce;

            float hisSize = e.SelectionBox.Length * e.SelectionBox.Height;

            float pushDiff = GameMath.Clamp(hisSize / mySize, 0, 1);

            if (entity.OnGround) pushDiff *= 3;
            
            pushVector.Add(px * pushDiff, py * pushDiff * 0.75, pz * pushDiff);

            return true;
        }


        public override string PropertyName()
        {
            return "repulseagents";
        }
    }
}
