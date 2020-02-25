using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class EntityInLiquid : EntityLocomotion
    {
        public long lastWaterJump = 0;
        public long lastPush = 0;

        float push;

        public override bool Applicable(Entity entity, EntityPos pos, EntityControls controls)
        {
            return entity.FeetInLiquid;
        }

        public override void DoApply(float dt, Entity entity, EntityPos pos, EntityControls controls)
        {
            if (entity.Swimming && entity.Alive)
            {
                string playerUID = entity is EntityPlayer ? ((EntityPlayer)entity).PlayerUID : null;


                if ((controls.TriesToMove || controls.Jump) && entity.World.ElapsedMilliseconds - lastPush > 2000 && playerUID != null)
                {
                    push = 8f;
                    lastPush = entity.World.ElapsedMilliseconds;
                    entity.PlayEntitySound("swim", playerUID == null ? null : entity.World.PlayerByUid(playerUID));
                }
                else
                {
                    push = Math.Max(1f, push - 0.1f);
                }

                Block inblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z);
                Block aboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 1), (int)pos.Z);
                Block twoaboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 2), (int)pos.Z);
                float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0) + (twoaboveblock.IsLiquid() ? 9 / 8f : 0);
                float bottomSubmergedness = waterY - (float)pos.Y;

                // 0 = at swim line
                // 1 = completely submerged
                float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
                swimlineSubmergedness = Math.Min(1, swimlineSubmergedness + 0.075f);


                double yMot = 0;
                if (controls.Jump)
                {
                    yMot = 0.005f * swimlineSubmergedness;
                } else
                {
                    yMot = controls.FlyVector.Y * (1 + push) * 0.03f * swimlineSubmergedness;
                }

                pos.Motion.Add(
                    controls.FlyVector.X * (1 + push) * 0.03f, 
                    yMot,
                    controls.FlyVector.Z * (1 + push) * 0.03f
                );
            }


            Block block = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)pos.Y, (int)pos.Z);
            if (block.PushVector != null)
            {
                pos.Motion.Add(block.PushVector);
            }
            
            // http://fooplot.com/plot/kg6l1ikyx2
            /*float x = entity.Pos.Motion.Length();
            if (x > 0)
            {
                pos.Motion.Normalize();
                pos.Motion *= (float)Math.Log(x + 1) / 1.5f;

            }*/
        }
    }
}
