using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.GameContent
{

    public class EntityProjectile : EntityProjectileBase, IProjectile
    {

        void IProjectile.PreInitialize()
        {
            Collectible = true;
            SetInitialRotation();
        }

        public override bool ApplyGravity
        {
            get { return !Stuck;}
        }

        public override bool IsInteractable
        {
            get { return false; }
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            collisionTestBox = SelectionBox.Clone().OmniGrowBy(0.05f);
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (ShouldDespawn) return;

            EntityPos pos = Pos;

            Stuck = Collided || collTester.IsColliding(World.BlockAccessor, collisionTestBox, pos.XYZ) || WatchedAttributes.GetBool("stuck");
            if (Api.Side == EnumAppSide.Server) WatchedAttributes.SetBool("stuck", Stuck);

            double impactSpeed = Math.Max(motionBeforeCollide.Length(), pos.Motion.Length());

            if (Stuck)
            {
                IsColliding(pos, impactSpeed);
                entitiesHit.Clear(); // to enable falling projectile to hit same entities second time if it was stuck and then released
                return;
            } else
            {
                SetRotationFromMotion();
            }

            if (TryAttackEntity(impactSpeed))
            {
                return;
            }

            beforeCollided = false;
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
        }


        public override void OnCollided()
        {
            EntityPos pos = Pos;

            IsColliding(Pos, Math.Max(motionBeforeCollide.Length(), pos.Motion.Length()));
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
        }


        protected virtual void IsColliding(EntityPos pos, double impactSpeed)
        {
            pos.Motion.Set(0, 0, 0);

            if (!beforeCollided && World is IServerWorldAccessor && World.ElapsedMilliseconds > msCollide + 500)
            {
                if (impactSpeed >= 0.07)
                {
                    World.PlaySoundAt(impactSound, this, null, false, 32);

                    // Resend position to client
                    WatchedAttributes.MarkAllDirty();

                    if (DamageStackOnImpact)
                    {
                        ProjectileStack.Collectible.DamageItem(World, this, new DummySlot(ProjectileStack));
                        int leftDurability = ProjectileStack == null ? 1 : ProjectileStack.Collectible.GetRemainingDurability(ProjectileStack);
                        if (leftDurability <= 0)
                        {
                            Die();
                        }
                    }
                }

                TryAttackEntity(impactSpeed);

                msCollide = World.ElapsedMilliseconds;

                beforeCollided = true;
            }
        }




        public virtual void SetInitialRotation()
        {
            var pos = Pos;
            double speed = pos.Motion.Length();
            if (speed > 0.01)
            {
                pos.Pitch = 0;
                pos.Yaw = GameMath.PI + (float)Math.Atan2(pos.Motion.X / speed, pos.Motion.Z / speed);
                pos.Roll = -(float)Math.Asin(GameMath.Clamp(-pos.Motion.Y / speed, -1, 1));
            }
        }

        public virtual void SetRotationFromMotion()
        {
            EntityPos pos = Pos;

            double speed = pos.Motion.Length();

            if (speed > 0.01)
            {
                pos.Pitch = 0;
                pos.Yaw =
                    GameMath.PI + (float)Math.Atan2(pos.Motion.X / speed, pos.Motion.Z / speed)
                    + GameMath.Cos((World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f
                ;
                pos.Roll =
                    -(float)Math.Asin(GameMath.Clamp(-pos.Motion.Y / speed, -1, 1))
                    + GameMath.Sin((World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f
                ;
            }
        }

    }
}
