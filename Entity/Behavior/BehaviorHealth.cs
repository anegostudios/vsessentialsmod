using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{
    public interface ICanHealCreature
    {
        bool CanHeal(Entity eagent);

        WorldInteraction[] GetHealInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player);
    }

    public delegate float OnDamagedDelegate(float damage, DamageSource dmgSource);
    public class EntityBehaviorHealth : EntityBehavior
    {
        // Experimentally determined - at 3.5 blocks the player has a motion of -0.19
        public const double FallDamageYMotionThreshold = -0.19;
        public const float FallDamageFallenDistanceThreshold = 3.5f;

        float secondsSinceLastUpdate;

        public event OnDamagedDelegate onDamaged = (dmg, dmgSource) => dmg;

        ITreeAttribute healthTree => entity.WatchedAttributes.GetTreeAttribute("health");

        public float Health
        {
            get { return healthTree.GetFloat("currenthealth"); }
            set { healthTree.SetFloat("currenthealth", value); entity.WatchedAttributes.MarkPathDirty("health"); }
        }

        public float? FutureHealth
        {
            get { return healthTree.GetFloat("futureHealth"); }
            set {
                if (value == null) healthTree.RemoveAttribute("futureHealth");
                else healthTree.SetFloat("futureHealth", (float)value); 

                entity.WatchedAttributes.MarkPathDirty("health");
            }
        }

        public float PreviousHealth
        {
            get { return healthTree.GetFloat("previousHealthValue"); }
            set {
                healthTree.SetFloat("previousHealthValue", (float)value); 
                entity.WatchedAttributes.MarkPathDirty("health"); 
            }
        }

        public float HealthChangeVelocity
        {
            get { return healthTree.GetFloat("healthChangeVelocity"); }
            set { healthTree.SetFloat("healthChangeVelocity", value); entity.WatchedAttributes.MarkPathDirty("health"); }
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

        [Obsolete("Please call SetMaxHealthModifiers() instead of writing to it directly.")]
        public Dictionary<string, float> MaxHealthModifiers { get; set; }
       
        protected Dictionary<string, float> maxHealthModifiers = null;


        public void SetMaxHealthModifiers(string key, float value)
        {
            bool dirty = true;
            if (maxHealthModifiers == null)
            {
                maxHealthModifiers = new Dictionary<string, float>();
                if (value == 0f) dirty = false;
            }
            else if (maxHealthModifiers.TryGetValue(key, out float oldvalue) && oldvalue == value)
            {
                dirty = false;
            }
            maxHealthModifiers[key] = value;
            if (dirty) MarkDirty();              // Only markDirty if it actually changed
        }




        public void MarkDirty()
        {
            UpdateMaxHealth();
            entity.WatchedAttributes.MarkPathDirty("health");
        }

        public void UpdateMaxHealth()
        {
            float totalMaxHealth = BaseMaxHealth;
            var MaxHealthModifiers = this.maxHealthModifiers;
            if (MaxHealthModifiers != null)
            {
                foreach (var val in MaxHealthModifiers) totalMaxHealth += val.Value;
            }

            totalMaxHealth += entity.Stats.GetBlended("maxhealthExtraPoints") - 1;

            bool wasFullHealth = Health >= MaxHealth;

            MaxHealth = totalMaxHealth;

            if (wasFullHealth) Health = MaxHealth;
        }

        public EntityBehaviorHealth(Entity entity) : base(entity) { }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            var healthTree = entity.WatchedAttributes.GetTreeAttribute("health");

            if (healthTree == null)
            {
                entity.WatchedAttributes.SetAttribute("health", healthTree = new TreeAttribute());

                BaseMaxHealth = typeAttributes["maxhealth"].AsFloat(20);
                Health = typeAttributes["currenthealth"].AsFloat(BaseMaxHealth);
                PreviousHealth = Health;
                MarkDirty();
                return;
            }

            float baseMaxHealth = healthTree.GetFloat("basemaxhealth");
            if (baseMaxHealth == 0)
            {
                BaseMaxHealth = typeAttributes["maxhealth"].AsFloat(20);
                MarkDirty();
            }
            // Otherwise we don't need to read and immediately set the same values back to the healthTree, nor mark it as dirty: and a MarkDirty() here messes up EntityPlayer health on joining a game, if done prior to initialising BehaviorHunger and its MaxHealthModifiers

            secondsSinceLastUpdate = (float) entity.World.Rand.NextDouble();   // Randomise which game tick these update, a starting server would otherwise start all loaded entities with the same zero timer
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side == EnumAppSide.Client) return;

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
                if (entity.Alive)
                {
                    var health = Health;  // higher performance to read this TreeAttribute only once
                    var maxHealth = MaxHealth;
                    if (health < maxHealth)
                    {
                        var healthRegenSpeed = entity is EntityPlayer ? entity.Api.World.Config.GetString("playerHealthRegenSpeed", "1").ToFloat() : entity.WatchedAttributes.GetFloat("regenSpeed", 1);

                        // previous value = 0.01 , -> 0.01 / 30 = 0.000333333f (60 * 0,5 = 30 (SpeedOfTime * CalendarSpeedMul))
                        var healthRegenPerGameSecond = 0.000333333f * healthRegenSpeed;
                        var multiplierPerGameSec = secondsSinceLastUpdate * entity.Api.World.Calendar.SpeedOfTime * entity.Api.World.Calendar.CalendarSpeedMul;

                        // Only players have the hunger behavior, and the different nutrient saturations
                        if (entity is EntityPlayer plr)
                        {
                            var ebh = entity.GetBehavior<EntityBehaviorHunger>();

                            if (ebh != null)
                            {
                                if (plr.Player.WorldData.CurrentGameMode == EnumGameMode.Creative) return;

                                // When below 75% satiety, autoheal starts dropping
                                healthRegenPerGameSecond = GameMath.Clamp(healthRegenPerGameSecond * ebh.Saturation / ebh.MaxSaturation * 1 / 0.75f, 0, healthRegenPerGameSecond);

                                ebh.ConsumeSaturation(150f * multiplierPerGameSec * healthRegenPerGameSecond);
                            }
                        }

                        Health = Math.Min(health + multiplierPerGameSec * healthRegenPerGameSecond, maxHealth);
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
                secondsSinceLastUpdate = 0;
            }
        }



        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (entity.World.Side == EnumAppSide.Client) return;
            var damageBeforeArmor = damage;

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

            if (entity is EntityPlayer player && damageSource.GetCauseEntity() is EntityPlayer otherPlayer)
            {
                string weapon;
                if (damageSource.SourceEntity != otherPlayer)
                {
                    weapon = damageSource.SourceEntity.Code.ToString();
                }
                else
                {
                    weapon = otherPlayer.Player.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible.Code.ToString() ?? "hands";
                }

                entity.Api.Logger.Audit("{0} at {1} got {2}/{3} damage {4} {5} by {6}",
                    player.Player.PlayerName, entity.Pos.AsBlockPos, damage, damageBeforeArmor, damageSource.Type.ToString().ToLowerInvariant(), weapon, otherPlayer.GetName());
            }

            PreviousHealth = Health;
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
            bool gliding = (entity as EntityAgent)?.ServerControls.Gliding == true;

            double yDistance = Math.Abs(positionBeforeFalling.Y - entity.Pos.Y);

            if (yDistance < FallDamageFallenDistanceThreshold) return;
            if (gliding)
            {
                yDistance = Math.Min(yDistance / 2, Math.Min(14, yDistance));
                withYMotion /= 2;

                // 1.5x pi is down
                // 1 x pi is horizontal
                // 0.5x pi half is up
                if (entity.ServerPos.Pitch < 1.25 * GameMath.PI)
                {
                    yDistance = 0;
                }
            }

            if (withYMotion > FallDamageYMotionThreshold) return;

            yDistance *= entity.Properties.FallDamageMultiplier;
            double fallDamage = Math.Max(0, yDistance - FallDamageFallenDistanceThreshold);

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
            var capi = entity.Api as ICoreClientAPI;
            if (capi?.World.Player?.WorldData?.CurrentGameMode == EnumGameMode.Creative)
            {
                infotext.AppendLine(Lang.Get("Health: {0}/{1}", Health, MaxHealth));
            }
        }

        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
        {
            if (IsHealable(player.Entity))
            {
                ICanHealCreature canHealCreature = player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.GetCollectibleInterface<ICanHealCreature>();

                if (canHealCreature != null)
                {
                    return canHealCreature.GetHealInteractionHelp(world, es, player).Append(base.GetInteractionHelp(world, es, player, ref handled));
                }
            }

            return base.GetInteractionHelp(world, es, player, ref handled);
        }

        public bool IsHealable(EntityAgent eagent, ItemSlot slot = null)
        {
            ICanHealCreature canHealCreature = (slot ?? eagent.RightHandItemSlot)?.Itemstack?.Collectible?.GetCollectibleInterface<ICanHealCreature>();

            return Health < MaxHealth && canHealCreature?.CanHeal(entity) == true;
        }

        public override string PropertyName()
        {
            return "health";
        }
    }
}
