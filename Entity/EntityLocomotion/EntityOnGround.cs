﻿using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityOnGround : EntityLocomotion
    {
        long lastJump;
        double groundDragFactor = 0.3f;

        Vec3d motionDelta = new Vec3d();

        float accum;

        internal override void Initialize(EntityProperties properties)
        {
            JsonObject physics = properties.Attributes?["physics"];
            if (physics != null)
            {
                groundDragFactor = 0.3 * (float)physics["groundDragFactor"].AsDouble(1);
            }
        }

        public override bool Applicable(Entity entity, EntityPos pos, EntityControls controls)
        {
            return entity.OnGround;
        }

        public override void DoApply(float dt, Entity entity, EntityPos pos, EntityControls controls)
        {
            Block belowBlock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y - 0.05f), (int)pos.Z);

            accum = Math.Min(1, accum + dt);
            float frametime = 1 / 60f;

            while (accum > frametime)
            {
                accum -= frametime;

                if (!entity.Swimming && entity.Alive)
                {
                    // Apply walk motion
                    double multiplier = (entity as EntityAgent).GetWalkSpeedMultiplier(groundDragFactor);

                    motionDelta.Set(
                        motionDelta.X + (controls.WalkVector.X * multiplier - motionDelta.X) * belowBlock.DragMultiplier,
                        0,
                        motionDelta.Z + (controls.WalkVector.Z * multiplier - motionDelta.Z) * belowBlock.DragMultiplier
                    );

                    pos.Motion.Add(motionDelta.X, 0, motionDelta.Z);
                }

                if (!entity.Swimming)
                {
                    // Apply ground drag
                    double dragstrength = 1 - groundDragFactor;

                    pos.Motion.X *= dragstrength;
                    pos.Motion.Z *= dragstrength;
                }
            }

            if (controls.Jump && entity.World.ElapsedMilliseconds - lastJump > 500 && entity.Alive && !entity.Swimming)
            {
                // Apply jump motion
                lastJump = entity.World.ElapsedMilliseconds;
                pos.Motion.Y = GlobalConstants.BaseJumpForce * 1 / 60f;

                EntityPlayer entityPlayer = entity as EntityPlayer;
                IPlayer player = entityPlayer != null ? entityPlayer.World.PlayerByUid(entityPlayer.PlayerUID) : null;
                entity.PlayEntitySound("jump", player, false);
            }

        }
    }
}
