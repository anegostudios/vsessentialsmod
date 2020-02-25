using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorRepulseAgents : EntityBehavior
    {
        Cuboidd entityCuboid = new Cuboidd();
        Vec3d pushVector = new Vec3d();
        double ownTouchDistance;

        int chunksize;

        EntityPartitioning partitionUtil;

        public EntityBehaviorRepulseAgents(Entity entity) : base(entity)
        {
            chunksize = entity.World.BlockAccessor.ChunkSize;
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();
        }

        Vec3d tmppos = new Vec3d();
        Vec3d ownPos = new Vec3d();
        Vec3d hisPos = new Vec3d();

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.ElapsedMilliseconds < 2000) return;
            if (entity.State == EnumEntityState.Inactive || !entity.IsInteractable) return;

            ownPos.Set(
                entity.LocalPos.X + (entity.CollisionBox.X2 - entity.OriginCollisionBox.X2), 
                entity.LocalPos.Y + (entity.CollisionBox.Y2 - entity.OriginCollisionBox.Y2), 
                entity.LocalPos.Z + (entity.CollisionBox.Z2 - entity.OriginCollisionBox.Z2)
            );

            ownTouchDistance = (entity.CollisionBox.X2 - entity.CollisionBox.X1)/2;
            
            pushVector.Set(0, 0, 0);
            
            partitionUtil.WalkEntityPartitions(ownPos, ownTouchDistance + partitionUtil.LargestTouchDistance + 0.1, WalkEntity);

            pushVector.X = GameMath.Clamp(pushVector.X, -3, 3);
            pushVector.Y = GameMath.Clamp(pushVector.Y, -3, 3);
            pushVector.Z = GameMath.Clamp(pushVector.Z, -3, 3);
            entity.LocalPos.Motion.Add(pushVector.X / 30, pushVector.Y / 30, pushVector.Z / 30);

            entity.World.FrameProfiler.Mark("entity-repulse");
        }


        Cuboidd tmpCuboid = new Cuboidd();

        private void WalkEntity(Entity e)
        {
            double hisTouchDistance = (e.CollisionBox.X2 - e.CollisionBox.X1) / 2;
            EntityPos epos = e.LocalPos;

            hisPos.Set(
                epos.X + (e.CollisionBox.X2 - e.OriginCollisionBox.X2),
                epos.Y + (e.CollisionBox.Y2 - e.OriginCollisionBox.Y2),
                epos.Z + (e.CollisionBox.Z2 - e.OriginCollisionBox.Z2)
            );

            double centerToCenterDistance = GameMath.Sqrt(hisPos.SquareDistanceTo(ownPos));

            if (e != entity && centerToCenterDistance < ownTouchDistance + hisTouchDistance && e.HasBehavior("repulseagents") && e.IsInteractable)
            {
                tmppos.Set(ownPos.X - hisPos.X, ownPos.Y - hisPos.Y, ownPos.Z - hisPos.Z);
                tmppos.Normalize().Mul(1 - centerToCenterDistance / (ownTouchDistance + hisTouchDistance));

                float hisSize = e.CollisionBox.Length * e.CollisionBox.Height;
                float mySize = entity.CollisionBox.Length * entity.CollisionBox.Height;
                float pushDiff = GameMath.Clamp(hisSize / mySize, 0, 1);

                pushVector.Add(tmppos.X * pushDiff, tmppos.Y * pushDiff, tmppos.Z * pushDiff);
            }
        }
        

        public override string PropertyName()
        {
            return "repulseagents";
        }
    }
}
