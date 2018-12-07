using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorHealth : EntityBehavior
    {
        ITreeAttribute healthTree;

        public float Health
        {
            get { return healthTree.GetFloat("currenthealth"); }
            set { healthTree.SetFloat("currenthealth", value); entity.WatchedAttributes.MarkPathDirty("health"); }
        }

        public float BaseMaxHealth
        {
            get { return healthTree.GetFloat("basemaxhealth"); }
            set {
                healthTree.SetFloat("basemaxhealth", value);
                entity.WatchedAttributes.MarkPathDirty("health");
            }
        }

        public float MaxHealth
        {
            get { return healthTree.GetFloat("maxhealth"); }
            set
            {
                healthTree.SetFloat("maxhealth", value);
                entity.WatchedAttributes.MarkPathDirty("health");
            }
        }
        


        public Dictionary<string, float> MaxHealthModifiers = new Dictionary<string, float>();

        public void MarkDirty()
        {
            UpdateMaxHealth();
            entity.WatchedAttributes.MarkPathDirty("health");
        }

        public void UpdateMaxHealth()
        {
            float totalMaxHealth = BaseMaxHealth;
            foreach (var val in MaxHealthModifiers) totalMaxHealth += val.Value;

            bool wasFullHealth = Health >= MaxHealth;

            MaxHealth = totalMaxHealth;

            if (wasFullHealth) Health = MaxHealth;
        }

        public EntityBehaviorHealth(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            healthTree = entity.WatchedAttributes.GetTreeAttribute("health");

            if (healthTree == null)
            {
                entity.WatchedAttributes.SetAttribute("health", healthTree = new TreeAttribute());

                Health = typeAttributes["currenthealth"].AsFloat(20);
                BaseMaxHealth = typeAttributes["maxhealth"].AsFloat(20);
                return;
            }

            Health = healthTree.GetFloat("currenthealth");
            BaseMaxHealth = healthTree.GetFloat("basemaxhealth");

            if (BaseMaxHealth == 0) BaseMaxHealth = typeAttributes["maxhealth"].AsFloat(20);
            

            UpdateMaxHealth();

            
        }

        
        public override void OnGameTick(float deltaTime)
        {
            if (entity.Pos.Y < -30)
            {
                entity.ReceiveDamage(new DamageSource()
                {
                    Source = EnumDamageSource.Void,
                    Type = EnumDamageType.Gravity
                }, 4);
            }
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, float damage)
        {
            if (damageSource.Type == EnumDamageType.Heal)
            {
                Health = Math.Min(Health + damage, MaxHealth);
                entity.OnHurt(damageSource, damage);
                UpdateMaxHealth();
                return;
            }

            if (!entity.Alive) return;

            Health -= damage;
            entity.OnHurt(damageSource, damage);
            UpdateMaxHealth();

            if (Health <= 0)
            {
                Health = 0;

                entity.Die(
                    EnumDespawnReason.Death, 
                    damageSource
                );
            } else
            {
                if (damage > 1f)
                {
                    entity.AnimManager.StartAnimation("hurt");
                }
            }
        }


        public override void OnFallToGround(Vec3d positionBeforeFalling, double withYMotion)
        {
            if (!entity.Properties.FallDamage) return;

            double yDistance = Math.Abs(positionBeforeFalling.Y - entity.Pos.Y);

            if (yDistance < 3.5f) return;

            // Experimentally determined - at 3.5 blocks the player has a motion of -0.19
            if (withYMotion > -0.19) return;  

            double fallDamage = yDistance - 3.5f;

            // Some super rough experimentally determined formula that always underestimates
            // the actual ymotion.
            // lets us reduce the fall damage if the player lost in y-motion during the fall
            // will totally break if someone changes the gravity constant
            double expectedYMotion = -0.041f * Math.Pow(fallDamage, 0.75f) - 0.22f;
            double yMotionLoss = Math.Max(0, -expectedYMotion + withYMotion);
            fallDamage -= 20 * yMotionLoss;

            if (fallDamage <= 0) return;

            /*if (fallDamage > 2)
            {
                entity.StartAnimation("heavyimpact");
            }*/

            entity.ReceiveDamage(new DamageSource()
            {
                Source = EnumDamageSource.Fall,
                Type = EnumDamageType.Gravity
            }, (float)fallDamage);
        }

        public override string PropertyName()
        {
            return "health";
        }
    }
}
