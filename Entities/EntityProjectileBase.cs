using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public abstract class EntityProjectileBase : Entity, IProjectile
    {
        #region IProjectile
        public Entity? FiredBy { get; set; }
        public float Damage { get; set; }
        public int DamageTier { get; set; } = 0;
        public EnumDamageType DamageType { get; set; } = EnumDamageType.PiercingAttack;
        public bool IgnoreInvFrames { get; set; } = false;
        public ItemStack? ProjectileStack { get; set; }
        public ItemStack? WeaponStack { get; set; }
        public float DropOnImpactChance { get; set; } = 0f;
        public bool DamageStackOnImpact { get; set; } = false;
        public bool Collectible
        {
            get { return Attributes.GetBool("collectible"); }
            set { Attributes.SetBool("collectible", value); }
        }
        public bool EntityHit { get; protected set; }
        public float Weight { get; set; } = 0.1f;
        public bool Stuck { get; set; }
        #endregion

        public override bool ApplyGravity => !Stuck;
        public override bool IsInteractable => false;


        public virtual void SetFromConfig(IProjectileJsonConfig config)
        {
            Damage = config.Damage;
            DamageTier = config.DamageTier;
            DamageType = config.DamageType;
            IgnoreInvFrames = config.IgnoreInvFrames;
            DropOnImpactChance = config.DropOnImpactChance;
            DamageStackOnImpact = config.DamageStackOnImpact;
            Collectible = config.Collectible;
            Weight = config.Weight;
        }

        public virtual void PreInitialize()
        {

        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            if (Api.Side == EnumAppSide.Server && FiredBy != null)
            {
                WatchedAttributes.SetLong("firedBy", FiredBy.EntityId);
            }

            if (Api.Side == EnumAppSide.Client)
            {
                FiredBy = Api.World.GetEntityById(WatchedAttributes.GetLong("firedBy"));
            }
            msLaunch = World.ElapsedMilliseconds;

            if (FiredBy is EntityAgent firedByAgent && firedByAgent.MountedOn?.Entity != null)
            {
                firedByMountEntityId = firedByAgent.MountedOn.Entity.EntityId;
            }

            EntityBehaviorPassivePhysics? physicsBehavior = GetBehavior<EntityBehaviorPassivePhysics>();
            if (physicsBehavior != null)
            {
                physicsBehavior.OnPhysicsTickCallback = OnPhysicsTickCallback;
                physicsBehavior.CollisionYExtra = 0f; // Slightly cheap hax so that stones/arrows don't collide with fences
            }

            entityPartitioning = api.ModLoader.GetModSystem<EntityPartitioning>();
        }

        public override bool CanCollect(Entity byEntity)
        {
            return Collectible && Alive && World.ElapsedMilliseconds - msLaunch > collectDelayMs && Pos.Motion.Length() < maxStuckSpeed;
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);
            writer.Write(beforeCollided);
            ProjectileStack?.ToBytes(writer);
        }

        public override void FromBytes(BinaryReader reader, bool isSync)
        {
            base.FromBytes(reader, isSync);
            beforeCollided = reader.ReadBoolean();
            try
            {
                ProjectileStack = new ItemStack(reader);
            }
            catch (Exception)
            {
                // If projectile stack was 'null' when calling 'ToBytes', lets leave it 'null' here and not cause a crash.
            }
        }

        public override ItemStack? OnCollected(Entity byEntity)
        {
            ProjectileStack?.ResolveBlockOrItem(World);
            return ProjectileStack;
        }



        protected bool beforeCollided;
        protected long msLaunch;
        protected long msCollide;
        protected Vec3d motionBeforeCollide = new();
        protected CollisionTester collTester = new();
        protected Cuboidf? collisionTestBox;
        protected EntityPartitioning? entityPartitioning;
        protected List<long> entitiesHit = [];
        protected long firedByMountEntityId;
        protected AssetLocation impactSound = new("sounds/arrow-impact");
        protected AssetLocation hitNotifySound = new("sounds/player/projectilehit");
        protected long collectDelayMs = 1000;
        protected double maxStuckSpeed = 0.2;
        protected double minAttackSpeed = 0.01;
        protected long selfHitDelay = 500;
        protected float collisionCheckRadius = 5;
        protected float knockbackFactor = 10;

        protected virtual void OnPhysicsTickCallback(float dtFac)
        {
            if (ShouldDespawn || !Alive) return;
            if (World.ElapsedMilliseconds <= msCollide + 500) return;

            EntityPos pos = Pos;
            if (pos.Motion.LengthSq() < maxStuckSpeed * maxStuckSpeed) return;  // Don't do damage if stuck in ground // Why not check for Stuck instead?

            Cuboidd collisionBox = CollisionBox.ToDouble().Translate(pos.X, pos.Y, pos.Z);

            AdjustCollisionBox(collisionBox, pos.Motion, dtFac);

            Cuboidd targetCollisionBox = new();

            entityPartitioning?.WalkEntities(pos.XYZ, collisionCheckRadius, (target) => TryHitTarget(target, collisionBox, targetCollisionBox), EnumEntitySearchType.Creatures);
        }

        protected virtual bool TryAttackEntity(double impactSpeed)
        {
            if (World is IClientWorldAccessor || World.ElapsedMilliseconds <= msCollide + 250) return false;
            if (impactSpeed <= minAttackSpeed) return false;

            EntityPos pos = Pos;

            Cuboidd collisionBox = CollisionBox.ToDouble().Translate(pos.X, pos.Y, pos.Z);

            AdjustCollisionBox(collisionBox, pos.Motion, 1.5f); // We give it a bit of extra leeway of 50% because physics ticks can run twice or 3 times in one game tick

            Cuboidd targetCollisionBox = new();
            Entity? target = World.GetNearestEntity(Pos.XYZ, collisionCheckRadius, collisionCheckRadius, (entity) =>
            {
                if (!CanHitTarget(entity)) return false;

                // So projectile does not damage same entity twice
                if (entitiesHit.Contains(entity.EntityId)) return false;

                targetCollisionBox.SetAndTranslate(entity.CollisionBox, entity.Pos.X, entity.Pos.Y, entity.Pos.Z);

                return targetCollisionBox.IntersectsOrTouches(collisionBox);
            });

            if (target != null)
            {
                entitiesHit.Add(target.EntityId);
                ImpactOnEntity(target);
                return true;
            }

            return false;
        }

        protected virtual void ImpactOnEntity(Entity target)
        {
            if (!Alive) return;

            bool canDealDamage = CanDealDamage(target);

            if (canDealDamage && World.Side == EnumAppSide.Server)
            {
                World.PlaySoundAt(impactSound, this, null, false, 24);

                bool dealtDamage = DealDamage(target);

                DamageProjectile(target);

                if (FiredBy is EntityPlayer player && dealtDamage)
                {
                    World.PlaySoundFor(hitNotifySound, player.Player, false, 24);
                }
            }

            EntityHit = true;
            msCollide = World.ElapsedMilliseconds;
            Pos.Motion.Set(0, 0, 0);
        }

        protected virtual bool CanDealDamage(Entity target)
        {
            IServerPlayer? fromPlayer = (FiredBy as EntityPlayer)?.Player as IServerPlayer; ;

            bool targetIsPlayer = target is EntityPlayer;
            bool targetIsCreature = target is EntityAgent;
            bool canDamage = true;

            ICoreServerAPI? serverApi = World.Api as ICoreServerAPI;
            if (fromPlayer != null && serverApi != null)
            {
                if (targetIsPlayer && (!serverApi.Server.Config.AllowPvP || !fromPlayer.HasPrivilege("attackplayers"))) canDamage = false;
                if (targetIsCreature && !fromPlayer.HasPrivilege("attackcreatures")) canDamage = false;
            }

            return canDamage;
        }

        protected virtual void DamageProjectile(Entity target)
        {
            int leftDurability = 1;
            if (DamageStackOnImpact && ProjectileStack != null)
            {
                ProjectileStack.Collectible.DamageItem(target.World, target, new DummySlot(ProjectileStack));
                leftDurability = ProjectileStack == null ? 1 : ProjectileStack.Collectible.GetRemainingDurability(ProjectileStack);
            }

            if (leftDurability <= 0 || World.Rand.NextDouble() >= DropOnImpactChance)
            {
                Die();
            }
        }

        protected virtual bool DealDamage(Entity target)
        {
            bool fromPlayer = FiredBy is EntityPlayer;

            float damage = Damage;

            ApplyModifiers(ref damage, target);

            bool didDamage = target.ReceiveDamage(new DamageSource()
            {
                Source = fromPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity,
                SourceEntity = this,
                CauseEntity = FiredBy,
                Type = DamageType,
                DamageTier = DamageTier,
                IgnoreInvFrames = IgnoreInvFrames,
                KnockbackStrength = Weight * (float)Pos.Motion.Length() * knockbackFactor
            }, damage);

            return didDamage;
        }

        protected virtual void ApplyModifiers(ref float damage, Entity target)
        {
            if (FiredBy != null)
            {
                damage *= FiredBy.Stats.GetBlended("rangedWeaponsDamage");

                if (target.Properties.Attributes?["isMechanical"].AsBool() == true)
                {
                    damage *= FiredBy.Stats.GetBlended("mechanicalsDamage");
                }
            }
        }

        protected virtual void AdjustCollisionBox(Cuboidd collisionBox, Vec3d velocity, float dt)
        {
            if (velocity.X < 0)
            {
                collisionBox.X1 += velocity.X * dt;
            }
            else
            {
                collisionBox.X2 += velocity.X * dt;
            }

            if (velocity.Y < 0)
            {
                collisionBox.Y1 += velocity.Y * dt;
            }
            else
            {
                collisionBox.Y2 += velocity.Y * dt;
            }

            if (velocity.Z < 0)
            {
                collisionBox.Z1 += velocity.Z * dt;
            }
            else
            {
                collisionBox.Z2 += velocity.Z * dt;
            }
        }

        protected virtual bool TryHitTarget(Entity target, Cuboidd projectileCollisionBox, Cuboidd targetCollisionBoxBuffer)
        {
            if (!CanHitTarget(target)) return true;

            if (entitiesHit.Contains(target.EntityId)) return true;

            targetCollisionBoxBuffer.SetAndTranslate(target.CollisionBox, target.Pos.X, target.Pos.Y, target.Pos.Z);

            if (targetCollisionBoxBuffer.IntersectsOrTouches(projectileCollisionBox))
            {
                ImpactOnEntity(target);
                return false;
            }

            return true;
        }

        protected virtual bool CanHitTarget(Entity target)
        {
            return !(
                target.EntityId == EntityId ||
                !target.IsInteractable ||
                (FiredBy != null && target.EntityId == FiredBy.EntityId && World.ElapsedMilliseconds - msLaunch < selfHitDelay) ||
                (target.EntityId == firedByMountEntityId && World.ElapsedMilliseconds - msLaunch < selfHitDelay)
            );
        }



        /// <summary>
        /// Common code for spawning and initiating the motion of a projectile with dispersion and initial position offset
        /// </summary>
        /// <param name="entity">The projectile. Does not have to be an EntityProjectile, but if it is then we call .SetRotation()</param>
        /// <param name="byEntity">The thrower</param>
        /// <param name="dispersionFactor">A multiplier for the thrower's accuracy: usually 0.75 but a smaller number would make this projectile type more accurate than most</param>
        /// <param name="verticalOffset">The height above or below eye-height of the launched projectile</param>
        /// <param name="horizontalOffset">The horizontal distance from eyes to throwing arm - used for thrown stones and snowballs. Positive for right arm, negative for left arm</param>
        /// <param name="forwardOffset">How far ahead the player's eyes the projectile starts when first spawned, affects the feel of throwing/firing it: default -0.21</param>
        /// <param name="speed">How fast the projectile should move: default 0.5</param>
        public static void SpawnProjectile(Entity entity, EntityAgent byEntity, double speed = 1, double dispersionFactor = 1, double verticalOffset = 0, double horizontalOffset = 0, double forwardOffset = 0, double parallaxDistance = 0)
        {
            (FastVec3d position, FastVec3d direction) = GetProjectileDirection(byEntity, dispersionFactor, verticalOffset, horizontalOffset, forwardOffset, parallaxDistance);

            entity.Pos.SetPosWithDimension(position);
            entity.Pos.Motion.Set(direction * speed);
            entity.World = byEntity.World;

            (entity as IProjectile)?.PreInitialize();

            byEntity.World?.SpawnPriorityEntity(entity);
        }

        /// <summary>
        /// Calculates projectile direction: its starting point and direction of its velocity.
        /// </summary>
        /// <param name="byEntity">Entity that shot/thrown the projectile</param>
        /// <param name="dispersionFactor">Projectile dispersion will be multiplied by this number: usually 0.75</param>
        /// <param name="verticalOffset">The height above or below eye-height of the launched projectile</param>
        /// <param name="horizontalOffset">The horizontal distance from eyes to throwing arm - used for thrown stones and snowballs. Positive for right arm, negative for left arm</param>
        /// <param name="forwardOffset">How far ahead the player's eyes the projectile starts when first spawned, affects the feel of throwing/firing it: default -0.21</param>
        /// <param name="parallaxDistance">Correction for horizontal offset to make sure that projectile direction and view direction intersect and this distance. If set to 0 this correction is ignored.</param>
        public static (FastVec3d position, FastVec3d direction) GetProjectileDirection(EntityAgent byEntity, double dispersionFactor, double verticalOffset = 0, double horizontalOffset = 0, double forwardOffset = 0, double parallaxDistance = 0)
        {
            float dispersion = Math.Max(0.001f, (1 - byEntity.Attributes.GetFloat("aimingAccuracy", 0)));
            double pitch = byEntity.WatchedAttributes.GetDouble("aimingRandPitch", 1) * dispersion * dispersionFactor;
            double yaw = byEntity.WatchedAttributes.GetDouble("aimingRandYaw", 1) * dispersion * dispersionFactor;

            FastVec3d offset = new(
                0 - GameMath.Cos(byEntity.Pos.Yaw) * horizontalOffset,
                verticalOffset,
                GameMath.Sin(byEntity.Pos.Yaw) * horizontalOffset
                );
            FastVec3d position = byEntity.Pos.BehindCopy(forwardOffset).XYZFast.Add(byEntity.LocalEyePos) + offset;

            if (horizontalOffset != 0 && parallaxDistance != 0)
            {
                yaw = ParallaxCorrection(yaw, parallaxDistance, horizontalOffset);
            }
            FastVec3d aheadPos = position.AheadCopy(1, byEntity.Pos.Pitch + pitch, byEntity.Pos.Yaw + yaw);
            FastVec3d direction = (aheadPos - position).Normalize();

            return (position, direction);
        }

        public static double ParallaxCorrection(double yaw, double distance, double offset)
        {
            FastVec3d initialDirection = new(Math.Cos(yaw), 0, Math.Sin(yaw));
            FastVec3d intersection = initialDirection * distance;
            FastVec3d right = new(Math.Sin(yaw), 0, -Math.Cos(yaw));
            FastVec3d start = right * offset;
            FastVec3d correctedDirection = (intersection - start).Normalize();

            return Math.Atan2(correctedDirection.Z, correctedDirection.X);
        }
    }
}
