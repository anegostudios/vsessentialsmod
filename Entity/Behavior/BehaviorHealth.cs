using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public delegate float OnDamagedDelegate(float damage, DamageSource dmgSource);
    public class EntityBehaviorHealth : EntityBehavior
    {
        ITreeAttribute healthTree;
        float secondsSinceLastUpdate;

        public event OnDamagedDelegate onDamaged = (dmg, dmgSource) => dmg;

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

            totalMaxHealth += entity.Stats.GetBlended("maxhealthExtraPoints") - 1;

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

                BaseMaxHealth = typeAttributes["maxhealth"].AsFloat(20);
                Health = typeAttributes["currenthealth"].AsFloat(BaseMaxHealth);
                MarkDirty();
                return;
            }

            Health = healthTree.GetFloat("currenthealth");
            BaseMaxHealth = healthTree.GetFloat("basemaxhealth");

            if (BaseMaxHealth == 0) BaseMaxHealth = typeAttributes["maxhealth"].AsFloat(20);

            MarkDirty();
            secondsSinceLastUpdate = (float) entity.World.Rand.NextDouble();   // Randomise which game tick these update, a starting server would otherwise start all loaded entities with the same zero timer
        }


        public override void OnGameTick(float deltaTime)
        {
            entity.World.FrameProfiler.Mark("not-bhhealth");
            if (entity.Pos.Y < -30)
            {
                entity.ReceiveDamage(new DamageSource()
                {
                    Source = EnumDamageSource.Void,
                    Type = EnumDamageType.Gravity
                }, 4);
            }

            secondsSinceLastUpdate += deltaTime;

            if (secondsSinceLastUpdate >= 1)
            {
                secondsSinceLastUpdate = 0;

                if (entity.Alive)
                {
                    float health = Health;  // higher performance to read this TreeAttribute only once
                    float maxHealth = MaxHealth;
                    if (health < maxHealth)
                    {
                        float recoverySpeed = 0.01f;

                        // Only players have the hunger behavior, and the different nutrient saturations
                        if (entity is EntityPlayer plr)
                        {
                            EntityBehaviorHunger ebh = entity.GetBehavior<EntityBehaviorHunger>();

                            if (ebh != null)
                            {
                                if (entity.World.PlayerByUid(plr.PlayerUID).WorldData.CurrentGameMode == EnumGameMode.Creative) return;

                                // When below 75% satiety, autoheal starts dropping
                                recoverySpeed = GameMath.Clamp(0.01f * ebh.Saturation / ebh.MaxSaturation * 1 / 0.75f, 0, 0.01f);

                                ebh.ConsumeSaturation(150f * recoverySpeed);
                            }
                        }

                        Health = Math.Min(health + recoverySpeed, maxHealth);
                    }
                }

                if (entity is EntityPlayer && entity.World.Side == EnumAppSide.Server)
                {
                    // A costly check every 1s for hail damage, but it applies only to players who are in the open

                    int rainy = entity.World.BlockAccessor.GetRainMapHeightAt((int)entity.ServerPos.X, (int)entity.ServerPos.Z);
                    if (entity.ServerPos.Y >= rainy)
                    {
                        WeatherSystemBase wsys = entity.Api.ModLoader.GetModSystem<WeatherSystemBase>();
                        var state = wsys.GetPrecipitationState(entity.ServerPos.XYZ);

                        if (state != null && state.ParticleSize >= 0.5 && state.Type == EnumPrecipitationType.Hail && entity.World.Rand.NextDouble() < state.Level / 2)
                        {
                            entity.ReceiveDamage(new DamageSource()
                            {
                                Source = EnumDamageSource.Weather,
                                Type = EnumDamageType.BluntAttack
                            }, (float)state.ParticleSize / 15f);
                        }
                    }
                }
            }
        }



        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (onDamaged != null)
            {
                foreach (OnDamagedDelegate dele in onDamaged.GetInvocationList())
                {
                    damage = dele.Invoke(damage, damageSource);
                }
            }

            if (damageSource.Type == EnumDamageType.Heal)
            {
                if (damageSource.Source != EnumDamageSource.Revive)
                {
                    damage *= Math.Max(0, entity.Stats.GetBlended("healingeffectivness"));
                    Health = Math.Min(Health + damage, MaxHealth);
                } else
                {
                    damage = Math.Min(damage, MaxHealth);
                    damage *= Math.Max(0.33f, entity.Stats.GetBlended("healingeffectivness"));
                    Health = damage;
                }

                entity.OnHurt(damageSource, damage);
                UpdateMaxHealth();
                return;
            }

            if (!entity.Alive) return;
            if (damage <= 0) return;
            

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

                if (damageSource.Type != EnumDamageType.Heal)
                {
                    entity.PlayEntitySound("hurt");
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

        public override void GetInfoText(StringBuilder infotext)
        {
           
        }

        public override string PropertyName()
        {
            return "health";
        }
    }
}
