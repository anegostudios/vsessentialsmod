using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityParticleWaterStrider : EntityParticle
    {
        static Random rand = new Random();

        ICoreClientAPI capi;
        float jumpCooldown = 0;
        float dieAccum = 0;

        public override string Type => "waterStrider";

        public EntityParticleWaterStrider(ICoreClientAPI capi, double x, double y, double z)
        {
            this.capi = capi;
            this.Position.Set(x, y, z);

            ColorAlpha = 255;
            Alive = true;
            Size = 0.35f + (float)capi.World.Rand.NextDouble() * 0.125f;
            GravityStrength = 0;

            ColorRed = 70;
            ColorGreen = 109;
            ColorBlue = 117;

            VertexFlags = (int)EnumWindBitMode.Water << API.Common.VertexFlags.WindModeBitsPos;
        }

        public override void TickNow(float dt, float physicsdt, ICoreClientAPI api, ParticlePhysics physicsSim)
        {
            base.TickNow(dt, physicsdt, api, physicsSim);

            Velocity.X *= 0.97f;
            Velocity.Z *= 0.97f;
        }

        protected override void doSlowTick(ParticlePhysics physicsSim, float dt)
        {
            base.doSlowTick(physicsSim, dt);

            // Make a 2nd jump right after the first jump
            if (jumpCooldown < 1.5f && jumpCooldown > 0 && (flags & EnumCollideFlags.CollideY) > 0 && Velocity.Y <= 0 && rand.NextDouble() < 0.4)
            {
                propel((float)rand.NextDouble()*0.66f - 0.33f, 0, (float)rand.NextDouble() * 0.66f - 0.33f);
            }

            if (jumpCooldown > 0)
            {
                jumpCooldown = GameMath.Max(0, jumpCooldown - dt);
                return;
            }

            if (rand.NextDouble() < 0.02)
            {
                propel((float)rand.NextDouble() * 0.66f - 0.33f, 0f, (float)rand.NextDouble() * 0.66f - 0.33f);
                return;
            }

            var npe = capi.World.NearestPlayer(Position.X, Position.Y, Position.Z).Entity;
            double sqdist = 50 * 50;
            if (npe != null && (sqdist = npe.Pos.SquareHorDistanceTo(Position)) < 3 * 3)
            {
                var vec = npe.Pos.XYZ.Sub(Position).Normalize();
                propel((float)-vec.X/3f, 0f, (float)-vec.Z/3f);
            }

            var block = capi.World.BlockAccessor.GetBlock((int)Position.X, (int)Position.Y, (int)Position.Z, BlockLayersAccess.Fluid);
            if (!block.IsLiquid())
            {
                Alive = false;
                return;
            }

            Position.Y = (int)Position.Y + block.LiquidLevel / 8f;

            if (npe == null || sqdist > 20 * 20)
            {
                dieAccum += dt;
                if (dieAccum > 15)
                {
                    Alive = false;
                }
            }
            else
            {
                dieAccum = 0;
            }
        }

        private void propel(float dirx, float diry, float dirz)
        {
            Velocity.Add(dirx, diry, dirz);
            jumpCooldown = 2f;
        }
    }

}
