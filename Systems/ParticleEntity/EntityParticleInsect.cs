using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public abstract class EntityParticleInsect : EntityParticle
    {
        protected ICoreClientAPI capi;
        protected float jumpCooldown = 0;
        protected float dieAccum = 0;
        protected float soundCoolDownLeft = 0;
        protected static Random rand = new Random();

        protected float jumpHeight = 1.3f;
        protected AssetLocation sound;
        protected bool doubleJump=true;

        protected float soundCoolDown = 5f;

        protected virtual float soundRange => 16;
        protected virtual float despawnDistanceSq => 20 * 20;

        public EntityParticleInsect(ICoreClientAPI capi, double x, double y, double z)
        {
            this.capi = capi;
            this.Position.Set(x, y, z);

            ColorAlpha = 255;
            SwimOnLiquid = true;
            Alive = true;
            Size = 0.5f + (float)capi.World.Rand.NextDouble()*0.5f;
        }


        protected override void doSlowTick(ParticlePhysics physicsSim, float dt)
        {
            base.doSlowTick(physicsSim, dt);

            var block = capi.World.BlockAccessor.GetBlock((int)Position.X, (int)Position.Y, (int)Position.Z, BlockLayersAccess.Fluid);
            if (block.IsLiquid())
            {
                dieAccum += dt;
                if (dieAccum > 10) Alive = false;
                return;
            }


            // Make a 2nd jump right after the first jump
            if (jumpHeight > 0 && doubleJump && jumpCooldown < 1.5f && jumpCooldown > 0 && (flags & EnumCollideFlags.CollideY) > 0 && Velocity.Y <= 0 && rand.NextDouble() < 0.4)
            {
                jump((float)rand.NextDouble() - 0.5f, jumpHeight, (float)rand.NextDouble() - 0.5f);
            }

            if (jumpCooldown > 0)
            {
                jumpCooldown = GameMath.Max(0, jumpCooldown - dt);
                return;
            }


            soundCoolDownLeft = GameMath.Max(0, soundCoolDownLeft - dt);

            if (jumpHeight > 0 && rand.NextDouble() < 0.005)
            {
                jump((float)rand.NextDouble() - 0.5f, jumpHeight, (float)rand.NextDouble() - 0.5f);
                return;
            }

            if (soundCoolDownLeft <= 0 && shouldPlaySound())
            {
                capi.Event.EnqueueMainThreadTask(() => capi.World.PlaySoundAt(sound, Position.X, Position.Y, Position.Z, null, RandomPitch(), soundRange), "playginsectsound");
                soundCoolDownLeft = soundCoolDown;
                return;
            }

            var npe = capi.World.NearestPlayer(Position.X, Position.Y, Position.Z).Entity;
            double sqdist = 50 * 50;
            if (npe != null && (sqdist=npe.Pos.SquareHorDistanceTo(Position)) < 3*3)
            {
                if (jumpHeight > 0)
                {
                    var vec = npe.Pos.XYZ.Sub(Position).Normalize();
                    jump((float)-vec.X, jumpHeight, (float)-vec.Z);
                }
            }

            if (npe == null || sqdist > despawnDistanceSq)
            {
                dieAccum += dt;
                if (dieAccum > 10)
                {
                    Alive = false;
                }
            } else
            {
                dieAccum = 0;
            }
        }

        protected virtual float RandomPitch()
        {
            return (float)capi.World.Rand.NextDouble() * 0.5f + 0.75f;
        }

        protected virtual bool shouldPlaySound()
        {
            return rand.NextDouble() < 0.01;
        }

        private void jump(float dirx, float diry, float dirz)
        {
            Velocity.Add(dirx, diry, dirz);
            jumpCooldown = 2f;
        }
    }
}
