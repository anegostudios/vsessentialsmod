using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class EntityBehaviorAimingAccuracy : EntityBehavior
    {
        public Random Rand;
        public bool IsAiming;

        List<AccuracyModifier> modifiers = new List<AccuracyModifier>();

        public EntityBehaviorAimingAccuracy(Entity entity) : base(entity)
        {
            EntityAgent agent = entity as EntityAgent;

            modifiers.Add(new BaseAimingAccuracy(agent));
            modifiers.Add(new MovingAimingAccuracy(agent));
            modifiers.Add(new SprintAimingAccuracy(agent));
            modifiers.Add(new OnHurtAimingAccuracy(agent));

            entity.Attributes.RegisterModifiedListener("aiming", OnAimingChanged);

            Rand = new Random((int)(entity.EntityId + entity.World.ElapsedMilliseconds));
        }

        private void OnAimingChanged()
        {
            bool beforeAiming = IsAiming;
            IsAiming = entity.Attributes.GetInt("aiming") > 0;

            if (beforeAiming == IsAiming) return;

            if (IsAiming && entity.World is API.Server.IServerWorldAccessor)
            {
                double rndpitch = Rand.NextDouble() - 0.5;
                double rndyaw = Rand.NextDouble() - 0.5;
                entity.WatchedAttributes.SetDouble("aimingRandPitch", rndpitch);
                entity.WatchedAttributes.SetDouble("aimingRandYaw", rndyaw);
            }

            for (int i = 0; i < modifiers.Count; i++)
            {
                if (IsAiming)
                {
                    modifiers[i].BeginAim();
                }
                else
                {
                    modifiers[i].EndAim();
                }
            }
        }

        public override void OnGameTick(float deltaTime)
        {
            if (!IsAiming) return;

            if (!entity.Alive)
            {
                entity.Attributes.SetInt("aiming", 0);
            }

            float accuracy = 0;

            for (int i = 0; i < modifiers.Count; i++)
            {
                modifiers[i].Update(deltaTime, ref accuracy);
            }

            entity.Attributes.SetFloat("aimingAccuracy", accuracy);
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);

            if (damageSource.Type == EnumDamageType.Heal) return;

            for (int i = 0; i < modifiers.Count; i++)
            {
                modifiers[i].OnHurt(damage);
            }
        }

        public override string PropertyName()
        {
            return "aimingaccuracy";
        }
    }


    public class AccuracyModifier
    {
        internal EntityAgent entity;
        internal long aimStartMs;

        public float SecondsSinceAimStart
        {
            get { return (entity.World.ElapsedMilliseconds - aimStartMs) / 1000f; }
        }

        public AccuracyModifier(EntityAgent entity)
        {
            this.entity = entity;
        }

        public virtual void BeginAim()
        {
            aimStartMs = entity.World.ElapsedMilliseconds;
        }

        public virtual void EndAim()
        {

        }

        public virtual void OnHurt(float damage) { }

        public virtual void Update(float dt, ref float accuracy)
        {

        }
    }


    public class BaseAimingAccuracy : AccuracyModifier
    {
        public BaseAimingAccuracy(EntityAgent entity) : base(entity)
        {
        }

        // 0 Accuracy = +/- 0.25*0.75 radians spread in yaw and pitch
        // 1 Accuracy = Zero spread
        public override void Update(float dt, ref float accuracy)
        {
            float rangedAcc = entity.Stats.GetBlended("rangedWeaponsAcc");
            float modspeed = entity.Stats.GetBlended("rangedWeaponsSpeed");

            // https://pfortuny.net/fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIxLTAuMDc1L3giLCJjb2xvciI6IiMwMDAwMDAifSx7InR5cGUiOjEwMDAsIndpbmRvdyI6WyIwIiwiMTAiLCIwIiwiMiJdLCJzaXplIjpbNjQ5LDM5OV19XQ--
            float maxAccuracy = Math.Min(1 - 0.075f / rangedAcc, 1);

            accuracy = GameMath.Clamp(SecondsSinceAimStart * modspeed * 1.7f, 0, maxAccuracy);

            if (SecondsSinceAimStart >= 0.75f)
            {
                accuracy += GameMath.Sin(SecondsSinceAimStart * 8) / 80f / Math.Max(1, rangedAcc);
            }

        }
    }

    /// <summary>
    /// Moving around decreases accuracy by 20% in 0.75 secconds
    /// </summary>
    public class MovingAimingAccuracy : AccuracyModifier
    {
        float accuracyPenalty;

        public MovingAimingAccuracy(EntityAgent entity) : base(entity)
        {
        }

        public override void Update(float dt, ref float accuracy)
        {
            float rangedAcc = entity.Stats.GetBlended("rangedWeaponsAcc");

            if (entity.Controls.TriesToMove)
            {
                accuracyPenalty = GameMath.Clamp(accuracyPenalty + dt / 0.75f, 0, 0.2f);
            } else
            {
                accuracyPenalty = GameMath.Clamp(accuracyPenalty - dt / 2f, 0, 0.2f);
            }

            accuracy -= accuracyPenalty / Math.Max(1, rangedAcc);
        }
    }


    /// <summary>
    /// Sprinting around decreases accuracy by 30% in 0.75 secconds
    /// </summary>
    public class SprintAimingAccuracy : AccuracyModifier
    {
        float accuracyPenalty;

        public SprintAimingAccuracy(EntityAgent entity) : base(entity)
        {
        }

        public override void Update(float dt, ref float accuracy)
        {
            float rangedAcc = entity.Stats.GetBlended("rangedWeaponsAcc");

            if (entity.Controls.TriesToMove && entity.Controls.Sprint)
            {
                accuracyPenalty = GameMath.Clamp(accuracyPenalty + dt / 0.75f, 0, 0.3f);
            }
            else
            {
                accuracyPenalty = GameMath.Clamp(accuracyPenalty - dt / 2f, 0, 0.3f);
            }

            accuracy -= accuracyPenalty / Math.Max(1, rangedAcc);
        }
    }

    public class OnHurtAimingAccuracy : AccuracyModifier
    {
        float accuracyPenalty;

        public OnHurtAimingAccuracy(EntityAgent entity) : base(entity)
        {
        }

        public override void Update(float dt, ref float accuracy)
        {
            accuracyPenalty = GameMath.Clamp(accuracyPenalty - dt / 3, 0, 0.4f);
        }

        public override void OnHurt(float damage)
        {

            if (damage > 3)
            {
                float rangedAcc = entity.Stats.GetBlended("rangedWeaponsAcc");

                accuracyPenalty = -0.4f / Math.Max(1, rangedAcc);
            }
        }
    }
    
}
