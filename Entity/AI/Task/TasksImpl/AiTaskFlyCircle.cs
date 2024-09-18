using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskFlyCircle : AiTaskBase
    {
        float radius;
        float height;
        protected double desiredYPos;
        protected float moveSpeed = 0.04f;

        double dir = 1;
        float dirchangeCoolDown = 0;

        public Vec3d CircleCenter;

        public AiTaskFlyCircle(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            radius = taskConfig["radius"].AsFloat(10f);
            height = taskConfig["height"].AsFloat(5f);
            moveSpeed = taskConfig["moveSpeed"].AsFloat(0.04f);            
        }

        public override void OnEntityLoaded()
        {
            
        }

        public override bool ShouldExecute()
        {
            return true;
        }

        public override void StartExecute()
        {
            if (entity.WatchedAttributes.HasAttribute("circleCenterX"))
            {
                CircleCenter = new Vec3d(
                    entity.WatchedAttributes.GetDouble("circleCenterX"),
                    entity.WatchedAttributes.GetDouble("circleCenterY"),
                    entity.WatchedAttributes.GetDouble("circleCenterZ")
                );
            }
            else
            {
                CircleCenter = entity.ServerPos.XYZ;
                entity.WatchedAttributes.SetDouble("circleCenterX", CircleCenter.X);
                entity.WatchedAttributes.SetDouble("circleCenterY", CircleCenter.Y);
                entity.WatchedAttributes.SetDouble("circleCenterZ", CircleCenter.Z);
            }

            base.StartExecute();
        }

        public override bool ContinueExecute(float dt)
        {
            if (entity.OnGround || entity.World.Rand.NextDouble() < 0.03)
            {
                ReadjustFlyHeight();
            }

            radius = 20;

            double dy = desiredYPos - entity.ServerPos.Y;
            double yMot = GameMath.Clamp(dy, -1, 1);

            double dx = entity.ServerPos.X - CircleCenter.X;
            double dz = entity.ServerPos.Z - CircleCenter.Z;
            double rad = Math.Sqrt(dx*dx + dz*dz);
            double offs = radius - rad;
            
            entity.ServerPos.Yaw = (float)Math.Atan2(dx, dz) + GameMath.PIHALF;

            float bla = (float)GameMath.Clamp(offs / 20.0, -1, 1);
            double cosYaw = Math.Cos(entity.ServerPos.Yaw - bla);
            double sinYaw = Math.Sin(entity.ServerPos.Yaw - bla);
            entity.Controls.WalkVector.Set(sinYaw, yMot, cosYaw);
            entity.Controls.WalkVector.Mul(moveSpeed);
            if (yMot < 0) entity.Controls.WalkVector.Mul(0.75);

            if (entity.Swimming)
            {
                entity.Controls.WalkVector.Y = 2 * moveSpeed;
                entity.Controls.FlyVector.Y = 2 * moveSpeed;
            }

            dirchangeCoolDown = Math.Max(0, dirchangeCoolDown - dt);
            if (entity.CollidedHorizontally && dirchangeCoolDown < 0)
            {
                dirchangeCoolDown = 2;
                dir *= -1;
            }

            return entity.Alive;
        }

        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
        }


        protected void ReadjustFlyHeight()
        {
            int terrainYPos = entity.World.BlockAccessor.GetTerrainMapheightAt(entity.SidedPos.AsBlockPos);
            int tries = 10;
            while (tries-- > 0)
            {
                Block block = entity.World.BlockAccessor.GetBlock((int)entity.ServerPos.X, terrainYPos, (int)entity.ServerPos.Z, BlockLayersAccess.Fluid);
                if (block.IsLiquid())
                {
                    terrainYPos++;
                }
                else
                {
                    break;
                }
            }

            desiredYPos = terrainYPos + height;
        }

    }
}
