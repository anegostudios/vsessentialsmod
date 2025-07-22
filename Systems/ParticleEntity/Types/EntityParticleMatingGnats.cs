using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class EntityParticleMatingGnats : EntityParticle
    {
        protected ICoreClientAPI capi;
        protected float dieAccum = 0;
        protected static Random rand = new Random();

        Vec3d centerPosition;

        public override string Type => "matinggnats";
        float cohesion;

        public EntityParticleMatingGnats(ICoreClientAPI capi, float cohesion, double x, double y, double z)
        {
            this.capi = capi;
            centerPosition = new Vec3d(x,y,z);

            this.Position.Set(x + rand.NextDouble() - 0.5, y + rand.NextDouble() - 0.5, z + rand.NextDouble() - 0.5);

            ColorAlpha = 200;
            SwimOnLiquid = true;
            GravityStrength = 0f;
            Alive = true;
            Size = 0.25f;
            ColorRed = 33;
            ColorGreen = 33;
            ColorBlue = 33;
            this.cohesion = cohesion;
        }

        public override void TickNow(float dt, float physicsdt, ICoreClientAPI api, ParticlePhysics physicsSim)
        {
            base.TickNow(dt, physicsdt, api, physicsSim);

            if (rand.NextDouble() < 0.5)
            {
                Vec3d vec = this.centerPosition.SubCopy(this.Position).Normalize();

                Velocity.Add(
                    (float)(vec.X / 2f + rand.NextDouble() / 8 - 0.125f / 2f) / (3f / cohesion),
                    (float)(vec.Y / 2f + rand.NextDouble() / 8 - 0.125f / 2f) / 3f,
                    (float)(vec.Z / 2f + rand.NextDouble() / 8 - 0.125f / 2f) / (3f / cohesion)
                );
            }

            Velocity.X = GameMath.Clamp(Velocity.X, -0.5f, 0.5f);
            Velocity.Y = GameMath.Clamp(Velocity.Y, -0.5f, 0.5f);
            Velocity.Z = GameMath.Clamp(Velocity.Z, -0.5f, 0.5f);
        }


        protected override void doSlowTick(ParticlePhysics physicsSim, float dt)
        {
            base.doSlowTick(physicsSim, dt);

            var npe = capi.World.NearestPlayer(Position.X, Position.Y, Position.Z).Entity;
            double sqdist = npe.Pos.SquareHorDistanceTo(Position);

            if (sqdist > 10*10)
            {
                var block = capi.World.BlockAccessor.GetBlockRaw((int)Position.X, (int)Position.Y, (int)Position.Z, BlockLayersAccess.Fluid);
                if (block.IsLiquid() || GlobalConstants.CurrentWindSpeedClient.Length() > 0.35f)
                {
                    dieAccum += dt;
                    if (dieAccum > 5) Alive = false;
                    return;
                }
            }

            if (npe == null || sqdist > 15 * 15)
            {
                dieAccum += dt;
                if (dieAccum > 10)
                {
                    Alive = false;
                }
            }
            else
            {
                dieAccum = 0;
            }

            if (sqdist < 2*2)
            {
                var vec = npe.Pos.XYZ.Sub(Position).Normalize();
                Velocity.Add(-(float)vec.X/2f, 0, -(float)vec.Z/2f);

                var block = capi.World.BlockAccessor.GetBlockRaw((int)Position.X, (int)Position.Y - 1, (int)Position.Z, BlockLayersAccess.Solid);
                if (block.Replaceable < 6000) Velocity.Add(0, 0.5f, 1);
            }
        }
    }
}
