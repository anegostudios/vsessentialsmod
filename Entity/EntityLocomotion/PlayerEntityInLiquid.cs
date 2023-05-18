using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class PlayerEntityInLiquid : EntityInLiquid
    {
        private IPlayer player;

        public PlayerEntityInLiquid(EntityPlayer eplr)
        {
            this.player = eplr.World.PlayerByUid(eplr.PlayerUID);
        }

        protected override void HandleSwimming(float dt, Entity entity, EntityPos pos, EntityControls controls)
        {
            if ((controls.TriesToMove || controls.Jump) && entity.World.ElapsedMilliseconds - lastPush > 2000)
            {
                push = 6f;
                lastPush = entity.World.ElapsedMilliseconds;
                entity.PlayEntitySound("swim", player);
            }
            else
            {
                push = Math.Max(1f, push - 0.1f * dt * 60f);
            }

            Block inblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)pos.Y, (int)pos.Z, BlockLayersAccess.Fluid);
            Block aboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 1), (int)pos.Z, BlockLayersAccess.Fluid);
            Block twoaboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 2), (int)pos.Z, BlockLayersAccess.Fluid);
            float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0) + (twoaboveblock.IsLiquid() ? 9 / 8f : 0);
            float bottomSubmergedness = waterY - (float)pos.Y;

            // 0 = at swim line
            // 1 = completely submerged
            float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
            swimlineSubmergedness = Math.Min(1, swimlineSubmergedness + 0.075f);


            double yMot;
            if (controls.Jump)
            {
                yMot = 0.005f * swimlineSubmergedness * dt * 60;
            }
            else
            {
                yMot = controls.FlyVector.Y * (1 + push) * 0.03f * swimlineSubmergedness;
            }


            pos.Motion.Add(
                controls.FlyVector.X * (1 + push) * 0.03f,
                yMot,
                controls.FlyVector.Z * (1 + push) * 0.03f
            );
        }
    }
}
