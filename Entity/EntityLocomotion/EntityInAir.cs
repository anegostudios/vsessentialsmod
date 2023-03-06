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

        float airMovingStrengthFalling;

        internal override void Initialize(JsonObject physics)
        {
            if (physics != null)
            {
                wallDragFactor = 0.3 * (float)physics["wallDragFactor"].AsDouble(1);
                airMovingStrength = (float)physics["airMovingStrength"].AsDouble(0.05);
            }

            airMovingStrengthFalling = airMovingStrength / 4;
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

        private void ApplyOnGround(float dt, Entity entity, EntityPos pos, EntityControls controls)
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
                float strength = airMovingStrength * (float)Math.Min(1, entity.Stats?.GetBlended("walkspeed") ?? 1.0) * dt * 60f;

                if (!controls.Jump && entity is EntityPlayer)
                {
                    strength = airMovingStrengthFalling;
                    pos.Motion.X *= (float)Math.Pow(0.98f, dt * 33);
                    pos.Motion.Z *= (float)Math.Pow(0.98f, dt * 33);
                }

                pos.Motion.Add(controls.WalkVector.X * strength, controls.WalkVector.Y * strength, controls.WalkVector.Z * strength);
            }
        }

        

        private static void ApplyFlying(float dt, EntityPos pos, EntityControls controls)
        {
            if (controls.Gliding)
            {
                double cosPitch = Math.Cos(pos.Pitch);
                double sinPitch = Math.Sin(pos.Pitch);

                double cosYaw = Math.Cos(Math.PI / 2 - pos.Yaw);
                double sinYaw = Math.Sin(Math.PI / 2 - pos.Yaw);

                double glideFac = sinPitch + 0.15;

                controls.GlideSpeed = GameMath.Clamp(controls.GlideSpeed - glideFac * dt * 0.25f, 0.005f, 0.75f);

                var gs = GameMath.Clamp(controls.GlideSpeed, 0.005f, 0.4f);

                pos.Motion.Add(
                    -cosPitch * sinYaw * gs,
                    sinPitch * gs,
                    cosPitch * cosYaw * gs
                );

                pos.Motion.Mul(GameMath.Clamp(1 - pos.Motion.Length()*0.13f, 0, 1));
            }
            else
            {
                pos.Motion.Add(controls.FlyVector.X, (controls.Up || controls.Down) ? 0 : controls.FlyVector.Y, controls.FlyVector.Z);

                float moveSpeed = dt * GlobalConstants.BaseMoveSpeed * controls.MovespeedMultiplier / 2;

                pos.Motion.Add(0, (controls.Up ? moveSpeed : 0) + (controls.Down ? -moveSpeed : 0), 0);
            }
        }
    }
}
