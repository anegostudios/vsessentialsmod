using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public interface IMeleeAttackListener
    {
        void DidAttack(Entity targetEntity);
    }

    public class AiTaskMeleeAttack : AiTaskBaseTargetable
    {
        protected long lastCheckOrAttackMs;
        protected float damage = 2f;
        protected float knockbackStrength = 1f;

        protected float minDist = 1.5f;
        protected float minVerDist = 1f;
        protected float attackAngleRangeDeg = 20f;

        protected bool damageInflicted = false;

        protected int attackDurationMs = 1500;
        protected int damagePlayerAtMs = 500;



        public EnumDamageType damageType = EnumDamageType.BluntAttack;
        public int damageTier = 0;
        protected float tamingGenerations = 10f;
        protected float attackRange = 3f;
        protected bool turnToTarget = true;

        public AiTaskMeleeAttack(EntityAgent entity) : base(entity)
        {            
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            this.tamingGenerations = taskConfig["tamingGenerations"].AsFloat(10f);
            this.damage = taskConfig["damage"].AsFloat(2);
            this.knockbackStrength = taskConfig["knockbackStrength"].AsFloat(GameMath.Sqrt(damage / 4f));
            this.attackAngleRangeDeg = taskConfig["attackAngleRangeDeg"].AsFloat(20);
            this.attackDurationMs = taskConfig["attackDurationMs"].AsInt(1500);
            this.damagePlayerAtMs = taskConfig["damagePlayerAtMs"].AsInt(1000);
            this.minDist = taskConfig["minDist"].AsFloat(2f);
            this.minVerDist = taskConfig["minVerDist"].AsFloat(1f);

            string strdt = taskConfig["damageType"].AsString();
            if (strdt != null)
            {
                this.damageType = (EnumDamageType)Enum.Parse(typeof(EnumDamageType), strdt, true);
            }
            this.damageTier = taskConfig["damageTier"].AsInt(0);

            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("extraInfoText");
            tree.SetString("dmgTier", Lang.Get("Damage tier: {0}", damageTier));
        }

        public override bool ShouldExecute()
        {
            long ellapsedMs = entity.World.ElapsedMilliseconds;
            if (ellapsedMs - lastCheckOrAttackMs < attackDurationMs || cooldownUntilMs > ellapsedMs)
            {
                return false;
            }

            if (!PreconditionsSatisifed()) return false;

            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);

            int generation = entity.WatchedAttributes.GetInt("generation", 0);
            float fearReductionFactor = Math.Max(0f, (tamingGenerations - generation) / tamingGenerations);
            if (whenInEmotionState != null) fearReductionFactor = 1;

            if (fearReductionFactor <= 0) return false;

            if (entity.World.ElapsedMilliseconds - attackedByEntityMs > 30000)
            {
                attackedByEntity = null;
            }
            if (retaliateAttacks && attackedByEntity != null && attackedByEntity.Alive && attackedByEntity.IsInteractable && IsTargetableEntity(attackedByEntity, 15, true) && hasDirectContact(attackedByEntity, minDist, minVerDist))
            {
                targetEntity = attackedByEntity;
            }
            else
            {
                targetEntity = entity.World.GetNearestEntity(pos, attackRange * fearReductionFactor, attackRange * fearReductionFactor, (e) =>
                {
                    return IsTargetableEntity(e, 15) && hasDirectContact(e, minDist, minVerDist);
                });
            }

            lastCheckOrAttackMs = entity.World.ElapsedMilliseconds;
            damageInflicted = false;

            return targetEntity != null;
        }


        float curTurnRadPerSec;
        bool didStartAnim;

        public override void StartExecute()
        {
            didStartAnim = false;
            curTurnRadPerSec = entity.GetBehavior<EntityBehaviorTaskAI>().PathTraverser.curTurnRadPerSec;

            if (!turnToTarget) base.StartExecute();
        }

        public override bool ContinueExecute(float dt)
        {
            EntityPos own = entity.ServerPos;
            EntityPos his = targetEntity.ServerPos;
            bool correctYaw = true;

            if (turnToTarget)
            {
                float desiredYaw = (float)Math.Atan2(his.X - own.X, his.Z - own.Z);
                float yawDist = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
                entity.ServerPos.Yaw += GameMath.Clamp(yawDist, -curTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier, curTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier);
                entity.ServerPos.Yaw = entity.ServerPos.Yaw % GameMath.TWOPI;

                correctYaw = Math.Abs(yawDist) < attackAngleRangeDeg * GameMath.DEG2RAD;

                if (correctYaw && !didStartAnim)
                {
                    didStartAnim = true;
                    base.StartExecute();
                }
            }

            if (lastCheckOrAttackMs + damagePlayerAtMs > entity.World.ElapsedMilliseconds) return true;

            if (!damageInflicted && correctYaw)
            {
                attackTarget();
                damageInflicted = true;
            }

            if (lastCheckOrAttackMs + attackDurationMs > entity.World.ElapsedMilliseconds) return true;
            return false;
        }


        protected virtual void attackTarget()
        {
            if (!hasDirectContact(targetEntity, minDist, minVerDist)) return;

            bool alive = targetEntity.Alive;

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

            if (alive && !targetEntity.Alive)
            {
                if (!(targetEntity is EntityPlayer)) entity.WatchedAttributes.SetDouble("lastMealEatenTotalHours", entity.World.Calendar.TotalHours);
                bhEmo?.TryTriggerState("saturated", targetEntity.EntityId);
            }
        }
    }
}