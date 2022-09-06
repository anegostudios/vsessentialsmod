using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.Essentials
{
    public class StraightLineTraverser : PathTraverserBase
    {
        float minTurnAnglePerSec;
        float maxTurnAnglePerSec;
        Vec3f targetVec = new Vec3f();

        public StraightLineTraverser(EntityAgent entity) : base(entity)
        {
            if (entity?.Properties.Server?.Attributes?.GetTreeAttribute("pathfinder") != null)
            {
                minTurnAnglePerSec = (float)entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetDecimal("minTurnAnglePerSec", 250);
                maxTurnAnglePerSec = (float)entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetDecimal("maxTurnAnglePerSec", 450);
            } else
            {
                minTurnAnglePerSec = 250;
                maxTurnAnglePerSec = 450;
            }
        }

        protected override bool BeginGo()
        {
            entity.Controls.Forward = true;
            entity.ServerControls.Forward = true;
            curTurnRadPerSec = minTurnAnglePerSec + (float)entity.World.Rand.NextDouble() * (maxTurnAnglePerSec - minTurnAnglePerSec);
            curTurnRadPerSec *= GameMath.DEG2RAD * 50;

            stuckCounter = 0;

            return true;
        }

        Vec3d prevPos = new Vec3d();

        public override void OnGameTick(float dt)
        {
            if (!Active) return;


            // For land dwellers only check horizontal distance
            double sqDistToTarget = 
                entity.Properties.Habitat == API.Common.EnumHabitat.Land ?
                    target.SquareDistanceTo(entity.ServerPos.X, target.Y, entity.ServerPos.Z) :
                    target.SquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z)
                ;


            if (sqDistToTarget < targetDistance * targetDistance)
            {
                Stop();
                OnGoalReached?.Invoke();
                return;
            }

            bool stuck =
                (entity.CollidedVertically && entity.Controls.IsClimbing) ||
                (entity.ServerPos.SquareDistanceTo(prevPos) < 0.005 * 0.005) ||  // This used to test motion, but that makes no sense, we want to test if the entity moved, not if it had motion
                (entity.CollidedHorizontally && entity.ServerPos.Motion.Y <= 0)
            ;

            prevPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);

            stuckCounter = stuck ? (stuckCounter + 1) : 0;
            
            if (GlobalConstants.OverallSpeedMultiplier > 0 && stuckCounter > 20 / GlobalConstants.OverallSpeedMultiplier)
            {
                //entity.World.SpawnParticles(10, ColorUtil.WhiteArgb, prevPos, prevPos, new Vec3f(0, 0, 0), new Vec3f(0, -1, 0), 1, 1);
                Stop();
                OnStuck?.Invoke();
                return;
            }


            EntityControls controls = entity.MountedOn == null ? entity.Controls : entity.MountedOn.Controls;
            if (controls == null) return;

            targetVec.Set(
                (float)(target.X - entity.ServerPos.X),
                (float)(target.Y - entity.ServerPos.Y),
                (float)(target.Z - entity.ServerPos.Z)
            );

            float desiredYaw = 0;
            
            if (sqDistToTarget >= 0.01)
            {
                desiredYaw = (float)Math.Atan2(targetVec.X, targetVec.Z);
            }

            float nowMoveSpeed = movingSpeed;

            if (sqDistToTarget < 1)
            {
                nowMoveSpeed = Math.Max(0.005f, movingSpeed * Math.Max((float)sqDistToTarget, 0.2f));
            }


            float yawDist = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
            float turnSpeed = curTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier * movingSpeed;
            entity.ServerPos.Yaw += GameMath.Clamp(yawDist, -turnSpeed, turnSpeed);
            entity.ServerPos.Yaw = entity.ServerPos.Yaw % GameMath.TWOPI;

            

            double cosYaw = Math.Cos(entity.ServerPos.Yaw);
            double sinYaw = Math.Sin(entity.ServerPos.Yaw);
            controls.WalkVector.Set(sinYaw, GameMath.Clamp(targetVec.Y, -1, 1), cosYaw);
            controls.WalkVector.Mul(nowMoveSpeed * GlobalConstants.OverallSpeedMultiplier);

            // Make it walk along the wall, but not walk into the wall, which causes it to climb
            if (entity.Properties.RotateModelOnClimb && entity.Controls.IsClimbing && entity.ClimbingOnFace != null && entity.Alive)
            {
                BlockFacing facing = entity.ClimbingOnFace;
                if (Math.Sign(facing.Normali.X) == Math.Sign(controls.WalkVector.X))
                {
                    controls.WalkVector.X = 0;
                }

                if (Math.Sign(facing.Normali.Z) == Math.Sign(controls.WalkVector.Z))
                {
                    controls.WalkVector.Z = 0;
                }
            }

         //   entity.World.SpawnParticles(0.3f, ColorUtil.WhiteAhsl, target, target, new Vec3f(), new Vec3f(), 0.1f, 0.1f, 3f, EnumParticleModel.Cube);


            if (entity.Swimming)
            {
                controls.FlyVector.Set(controls.WalkVector);

                Vec3d pos = entity.Pos.XYZ;
                Block inblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z, BlockLayersAccess.Fluid);
                Block aboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 1), (int)pos.Z, BlockLayersAccess.Fluid);
                float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0);
                float bottomSubmergedness = waterY - (float)pos.Y;

                // 0 = at swim line
                // 1 = completely submerged
                float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
                swimlineSubmergedness = Math.Min(1, swimlineSubmergedness + 0.075f);
                controls.FlyVector.Y = GameMath.Clamp(controls.FlyVector.Y, 0.002f, 0.004f) * swimlineSubmergedness;


                if (entity.CollidedHorizontally)
                {
                    controls.FlyVector.Y = 0.05f;
                }
            }
        }


        public override void Stop()
        {
            Active = false;
            entity.Controls.Forward = false;
            entity.ServerControls.Forward = false;
            entity.Controls.WalkVector.Set(0, 0, 0);
            stuckCounter = 0;
        }
    }
}
