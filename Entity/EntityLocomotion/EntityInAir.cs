using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityInAir : EntityLocomotion
    {
        public float airMovingStrength = 0.05f;
        double wallDragFactor = 0.3f;

        internal override void Initialize(JsonObject physics)
        {
            if (physics != null)
            {
                wallDragFactor = 0.3 * (float)physics["wallDragFactor"].AsDouble(1);
                airMovingStrength = (float)physics["airMovingStrength"].AsDouble(0.05);
            }
        }

        public override bool Applicable(Entity entity, EntityPos pos, EntityControls controls)
        {
            return (controls.IsFlying || (!entity.Collided && !entity.FeetInLiquid)) && entity.Alive;
        }

        public override void DoApply(float dt, Entity entity, EntityPos pos, EntityControls controls)
        {
            if (controls.IsFlying)
            {
                ApplyFlying(dt, pos, controls);
            }
            else
            {
                ApplyOnGround(dt, entity, pos, controls);
            }
        }

        protected virtual void ApplyOnGround(float dt, Entity entity, EntityPos pos, EntityControls controls)
        {
            if (controls.IsClimbing)
            {
                pos.Motion.Add(controls.WalkVector);
                pos.Motion.X *= Math.Pow(1 - wallDragFactor, dt * 60);
                pos.Motion.Y *= Math.Pow(1 - wallDragFactor, dt * 60);
                pos.Motion.Z *= Math.Pow(1 - wallDragFactor, dt * 60);
            }
            else
            {
                float strength = airMovingStrength * dt * 60f;

                pos.Motion.Add(controls.WalkVector.X * strength, controls.WalkVector.Y * strength, controls.WalkVector.Z * strength);
            }
        }

        

        protected virtual void ApplyFlying(float dt, EntityPos pos, EntityControls controls)
        {
            pos.Motion.Add(controls.FlyVector.X, (controls.Up || controls.Down) ? 0 : controls.FlyVector.Y, controls.FlyVector.Z);

            float moveSpeed = dt * GlobalConstants.BaseMoveSpeed * controls.MovespeedMultiplier / 2;

            pos.Motion.Add(0, (controls.Up ? moveSpeed : 0) + (controls.Down ? -moveSpeed : 0), 0);
        }
    }
}
