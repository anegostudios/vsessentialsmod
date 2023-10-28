using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class PlayerEntityInAir : EntityInAir
    {
        float airMovingStrengthFalling;

        internal override void Initialize(JsonObject physics)
        {
            base.Initialize(physics);
            airMovingStrengthFalling = airMovingStrength / 4;
        }

        protected override void ApplyOnGround(float dt, Entity entity, EntityPos pos, EntityControls controls)
        {
            if (controls.IsClimbing)
            {
                base.ApplyOnGround(dt, entity, pos, controls);
            }
            else
            {
                float strength = airMovingStrength * (float)Math.Min(1, ((EntityPlayer)entity).walkSpeed) * dt * 60f;

                if (!controls.Jump)
                {
                    strength = airMovingStrengthFalling;
                    pos.Motion.X *= (float)Math.Pow(0.98f, dt * 33);
                    pos.Motion.Z *= (float)Math.Pow(0.98f, dt * 33);
                }

                pos.Motion.Add(controls.WalkVector.X * strength, controls.WalkVector.Y * strength, controls.WalkVector.Z * strength);
            }
        }

        

        protected override void ApplyFlying(float dt, EntityPos pos, EntityControls controls)
        {
            if (controls.Gliding)
            {
                double cosPitch = Math.Cos(pos.Pitch);
                double sinPitch = Math.Sin(pos.Pitch);

                double cosYaw = Math.Cos(Math.PI / 2 - pos.Yaw);
                double sinYaw = Math.Sin(Math.PI / 2 - pos.Yaw);

                double glideFac = sinPitch + 0.15;

                controls.GlideSpeed = GameMath.Clamp(controls.GlideSpeed - glideFac * dt * 0.25f, 0.005f, 0.75f);

                var gs = GameMath.Clamp(controls.GlideSpeed, 0.005f, 0.2f);

                pos.Motion.Add(
                    -cosPitch * sinYaw * gs,
                    sinPitch * gs,
                    cosPitch * cosYaw * gs
                );

                pos.Motion.Mul(GameMath.Clamp(1 - pos.Motion.Length()*0.13f, 0, 1));
            }
            else
            {
                base.ApplyFlying(dt, pos, controls);
            }
        }
    }
}
