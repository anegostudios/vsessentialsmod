using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorRepulseAgents : EntityBehavior
    {
        Vec3d pushVector = new Vec3d();
        EntityPartitioning partitionUtil;
        bool movable = true;

        public EntityBehaviorRepulseAgents(Entity entity) : base(entity)
        {
            entity.hasRepulseBehavior = true;
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            movable = attributes["movable"].AsBool(true);
            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();
        }

        double ownPosRepulseX, ownPosRepulseY, ownPosRepulseZ;
        float mySize;

        public override void OnGameTick(float deltaTime)
        {
            if (entity.State == EnumEntityState.Inactive || !entity.IsInteractable || !movable) return;
            if (entity.World.ElapsedMilliseconds < 2000) return;

            double touchdist = entity.SelectionBox.XSize / 2;

            pushVector.Set(0, 0, 0);

            ownPosRepulseX = entity.ownPosRepulse.X;
            ownPosRepulseY = entity.ownPosRepulse.Y;
            ownPosRepulseZ = entity.ownPosRepulse.Z;
            mySize = entity.SelectionBox.Length * entity.SelectionBox.Height;

            partitionUtil.WalkEntityPartitions(entity.ownPosRepulse, touchdist + partitionUtil.LargestTouchDistance + 0.1, WalkEntity);

            pushVector.X = GameMath.Clamp(pushVector.X, -3, 3);
            pushVector.Y = GameMath.Clamp(pushVector.Y, -3, 3);
            pushVector.Z = GameMath.Clamp(pushVector.Z, -3, 3);

            entity.SidedPos.Motion.Add(pushVector.X / 30, pushVector.Y / 30, pushVector.Z / 30);
        }


        private bool WalkEntity(Entity e)
        {
            if (!e.hasRepulseBehavior || !e.IsInteractable || e == entity) return true;
            
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

            pushVector.Add(px * pushDiff, py * pushDiff, pz * pushDiff);

            return true;
        }


        public override string PropertyName()
        {
            return "repulseagents";
        }
    }
}
