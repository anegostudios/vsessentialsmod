using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class EntityInAir : EntityLocomotion
    {
        public float airMovingStrength = 0.05f;
        double wallDragFactor = 0.3f;

        float airMovingStrengthFalling;

        internal override void Initialize(EntityProperties properties)
        {
            JsonObject physics = properties?.Attributes?["physics"];
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
                pos.Motion.Add(controls.FlyVector.X, (controls.Up || controls.Down) ? 0 : controls.FlyVector.Y, controls.FlyVector.Z);

                float moveSpeed = dt * GlobalConstants.BaseMoveSpeed * controls.MovespeedMultiplier / 2;

                pos.Motion.Add(0, (controls.Up ? moveSpeed : 0) + (controls.Down ? -moveSpeed : 0), 0);

            } else
            {
                if (controls.IsClimbing)
                {
                    pos.Motion.Add(controls.WalkVector);
                    pos.Motion.X *= System.Math.Pow(1 - wallDragFactor, dt * 60);
                    pos.Motion.Y *= System.Math.Pow(1 - wallDragFactor, dt * 60);
                    pos.Motion.Z *= System.Math.Pow(1 - wallDragFactor, dt * 60);

                } else
                {
                    float strength = airMovingStrength * (float)System.Math.Min(1, entity.Stats?.GetBlended("walkspeed") ?? 1.0) * dt * 60f;
                    
                    if (!controls.Jump && entity is EntityPlayer)
                    {
                        strength = airMovingStrengthFalling;
                        pos.Motion.X *= (float)System.Math.Pow(0.98f, dt * 33);
                        pos.Motion.Z *= (float)System.Math.Pow(0.98f, dt * 33);
                    }

                    pos.Motion.Add(controls.WalkVector.X * strength, controls.WalkVector.Y * strength, controls.WalkVector.Z * strength);

                    
                }
                
            }
        }
    }
}
