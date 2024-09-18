using CompactExifLib;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class AiTaskFlySwoopAttack : AiTaskBaseTargetable
    {
        protected long lastCheckOrAttackMs;
        protected float damage = 2f;
        protected float knockbackStrength = 1f;
        protected float seekingRangeVer = 25f;
        protected float seekingRangeHor = 25f;
        protected float damageRange = 5f;
        protected float moveSpeed = 0.04f;

        bool didDamage = false;

        public EnumDamageType damageType = EnumDamageType.BluntAttack;
        public int damageTier = 0;
        float afterDmgAccum;
        Vec3d targetPos;


        public AiTaskFlySwoopAttack(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            moveSpeed = taskConfig["moveSpeed"].AsFloat(0.04f);
            damage = taskConfig["damage"].AsFloat(2);
            knockbackStrength = taskConfig["knockbackStrength"].AsFloat(GameMath.Sqrt(damage / 2f));
            seekingRangeHor = taskConfig["seekingRangeHor"].AsFloat(25);
            seekingRangeVer = taskConfig["seekingRangeVer"].AsFloat(25);
            damageRange = taskConfig["damageRange"].AsFloat(2);
            string strdt = taskConfig["damageType"].AsString();
            if (strdt != null)
            {
                this.damageType = (EnumDamageType)Enum.Parse(typeof(EnumDamageType), strdt, true);
            }
            this.damageTier = taskConfig["damageTier"].AsInt(0);
        }

        public override void OnEntityLoaded() { }
        public override bool ShouldExecute() {
            long ellapsedMs = entity.World.ElapsedMilliseconds;
            if (cooldownUntilMs > ellapsedMs)
            {
                return false;
            }

            if (!PreconditionsSatisifed()) return false;

            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);

            if (entity.World.ElapsedMilliseconds - attackedByEntityMs > 30000)
            {
                attackedByEntity = null;
            }
            if (retaliateAttacks && attackedByEntity != null && attackedByEntity.Alive && attackedByEntity.IsInteractable && IsTargetableEntity(attackedByEntity, 15, true))
            {
                targetEntity = attackedByEntity;
            }
            else
            {
                seekingRangeVer = 40;
                targetEntity = entity.World.GetNearestEntity(pos, seekingRangeHor, seekingRangeVer, (e) =>
                {
                    return IsTargetableEntity(e, seekingRangeHor) && hasDirectContact(e, seekingRangeHor, seekingRangeVer);
                });
            }

            lastCheckOrAttackMs = entity.World.ElapsedMilliseconds;

            return targetEntity != null;
        }

        Vec3d dir;
        public override void StartExecute()
        {
            afterDmgAccum = 0;
            didDamage = false;
            targetPos = targetEntity.ServerPos.XYZ;
            dir = targetPos - entity.ServerPos.XYZ;
            dir.Normalize();

            base.StartExecute();
        }

        public override bool ContinueExecute(float dt)
        {
            entity.ServerPos.Yaw = (float)Math.Atan2(dir.X, dir.Z);
            entity.Controls.WalkVector.Set(dir.X, didDamage ? -dir.Y : dir.Y, dir.Z);
            entity.Controls.WalkVector.Mul(moveSpeed);

            if (entity.Swimming)
            {
                entity.Controls.WalkVector.Y = 2 * moveSpeed;
                entity.Controls.FlyVector.Y = 2 * moveSpeed;
            }

            var nowdir = targetPos - entity.ServerPos.XYZ;
            double distance = nowdir.Length();
            if (!didDamage && distance < 2 * damageRange)
            {
                if (hasDirectContact(targetEntity, damageRange, damageRange))
                {
                    targetEntity.ReceiveDamage(
                        new DamageSource()
                        {
                            Source = EnumDamageSource.Entity,
                            SourceEntity = entity,
                            Type = damageType,
                            DamageTier = damageTier,
                            KnockbackStrength = knockbackStrength
                        },
                        damage * GlobalConstants.CreatureDamageModifier
                    );

                    if (entity is IMeleeAttackListener imal)
                    {
                        imal.DidAttack(targetEntity);
                    }

                    didDamage = true;
                }
            }

            if (didDamage) afterDmgAccum += dt;

            bool ctd = !didDamage || afterDmgAccum < 2;
            
            return ctd;
        }

        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);
        }



    }
}
