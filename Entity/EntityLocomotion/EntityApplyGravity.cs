using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityApplyGravity : EntityLocomotion
    {
        double gravityPerSecond = GlobalConstants.GravityPerSecond;

        internal override void Initialize(JsonObject physics)
        {
            if (physics != null)
            {
                gravityPerSecond = GlobalConstants.GravityPerSecond * (float)physics["gravityFactor"].AsDouble(1);
            }
        }

        public override bool Applicable(Entity entity, EntityPos pos, EntityControls controls)
        {
            return 
                ((!controls.IsFlying || controls.Gliding) && entity.Properties.Habitat != EnumHabitat.Air)
                && (entity.Properties.Habitat != EnumHabitat.Sea && entity.Properties.Habitat != EnumHabitat.Underwater || !entity.Swimming)
                && !controls.IsClimbing
            ;
        }

        public override void DoApply(float dt, Entity entity, EntityPos pos, EntityControls controls)
        {
            if (entity.Swimming && controls.TriesToMove && entity.Alive) return;
            if (!entity.ApplyGravity) return;

            if (pos.Y > -100)
            {
                double gravity = (gravityPerSecond + Math.Max(0, -0.015f * pos.Motion.Y)) * (entity.FeetInLiquid ? 0.33f : 1f) * dt;
                pos.Motion.Y -= gravity * GameMath.Clamp(1 - 50 * controls.GlideSpeed * controls.GlideSpeed, 0, 1);
            }
            
        }
    }
}
