using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

public interface ICanHealCreature
{
    bool CanHeal(Entity target);

    WorldInteraction[] GetHealInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player);
}

public delegate float OnDamagedDelegate(float damage, DamageSource dmgSource);

public class DamageOverTimeEffect
{
    public EnumDamageSource DamageSource;
    public EnumDamageType DamageType;
    public int DamageTier;
    public float Damage;
    public int EffectType;

    public TimeSpan TickDuration;
    public TimeSpan PreviousTickTime;
    public int TicksLeft;

    public DamageOverTimeEffect()
    {

    }

    public void ToBytes(BinaryWriter writer)
    {
        writer.Write((byte)DamageSource);
        writer.Write((byte)DamageType);
        writer.Write(DamageTier);
        writer.Write(Damage);
        writer.Write(TickDuration.Ticks);
        writer.Write(PreviousTickTime.Ticks);
        writer.Write(TicksLeft);
        writer.Write(EffectType);
    }
    public static DamageOverTimeEffect FromBytes(BinaryReader reader)
    {
        return new DamageOverTimeEffect
        {
            DamageSource = (EnumDamageSource)reader.ReadByte(),
            DamageType = (EnumDamageType)reader.ReadByte(),
            DamageTier = reader.ReadInt32(),
            Damage = reader.ReadSingle(),
            TickDuration = TimeSpan.FromTicks(reader.ReadInt64()),
            PreviousTickTime = TimeSpan.FromTicks(reader.ReadInt64()),
            TicksLeft = reader.ReadInt32(),
            EffectType = reader.ReadInt32()
        };
    }
}

public class EntityBehaviorHealth : EntityBehavior
{
    // Experimentally determined - at 3.5 blocks the player has a motion of -0.19
    public const double FallDamageYMotionThreshold = -0.19;
    public const float FallDamageFallenDistanceThreshold = 3.5f;

    public event OnDamagedDelegate onDamaged = (dmg, dmgSource) => dmg;

    public float Health
    {
        get { return healthTree.GetFloat("currenthealth"); }
        set { healthTree.SetFloat("currenthealth", value); entity.WatchedAttributes.MarkPathDirty("health"); }
    }

    public float? FutureHealth
    {
        get { return healthTree.GetFloat("futureHealth"); }
        set
        {
            if (value == null) healthTree.RemoveAttribute("futureHealth");
            else healthTree.SetFloat("futureHealth", (float)value);

            entity.WatchedAttributes.MarkPathDirty("health");
        }
    }

    public float PreviousHealth
    {
        get { return healthTree.GetFloat("previousHealthValue"); }
        set
        {
            healthTree.SetFloat("previousHealthValue", (float)value);
            entity.WatchedAttributes.MarkPathDirty("health");
        }
    }

    public float HealthChangeRate
    {
        get { return healthTree.GetFloat("healthChangeRate"); }
        set { healthTree.SetFloat("healthChangeRate", value); entity.WatchedAttributes.MarkPathDirty("health"); }
    }

    public float BaseMaxHealth
    {
        get { return healthTree.GetFloat("basemaxhealth"); }
        set
        {
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

    public List<DamageOverTimeEffect> ActiveDoTEffects { get; } = new();

    [Obsolete("Please call SetMaxHealthModifiers() instead of writing to it directly.")]
    public Dictionary<string, float>? MaxHealthModifiers { get; set; }


    protected Dictionary<string, float>? maxHealthModifiers = null;

    /// <summary>
    /// Hunger, health regeneration and hail damage will be applied each <see cref="HealthUpdateCooldownSec"/> amount of seconds for better performance.
    /// </summary>
    protected virtual float HealthUpdateCooldownSec => 1f;

    /// <summary>
    /// Should entity receive hail damage. By default only players receive hail damage.
    /// </summary>
    protected virtual bool ReceiveHailDamage { get; set; } = false;

    /// <summary>
    /// Health regeneration speed will be slowed down when saturation is below this fraction of total saturation.
    /// </summary>
    protected virtual float AutoRegenSaturationThreshold { get; set; } = 0.75f;

    /// <summary>
    /// How many saturation points will be consumed per health point restored.
    /// </summary>
    protected virtual float SaturationPerHealthPoint { get; set; } = 150f;

    /// <summary>
    /// This animation will be played if entity received more than 1hp damage. If set to 'null', animation wont be played.
    /// </summary>
    protected string? HurtAnimationCode = "hurt";

    /// <summary>
    /// When entity takes damage, entity sound of this type will be played.
    /// </summary>
    protected string HurtEntitySoundCode = "hurt";

    protected float timeSinceLastDoTTickSec = 0;
    protected float timeBetweenDoTTicksSec = 0.5f;


    private float secondsSinceLastUpdate;

    private ITreeAttribute healthTree => entity.WatchedAttributes.GetTreeAttribute("health");



    public EntityBehaviorHealth(Entity entity) : base(entity) { }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        ITreeAttribute? entityHealthTree = entity.WatchedAttributes.GetTreeAttribute("health");

        if (entityHealthTree == null)
        {
            entity.WatchedAttributes.SetAttribute("health", entityHealthTree = new TreeAttribute());

            BaseMaxHealth = attributes["maxhealth"].AsFloat(20);
            Health = attributes["currenthealth"].AsFloat(BaseMaxHealth);
            PreviousHealth = Health;
            MarkDirty();
            return;
        }

        float baseMaxHealth = entityHealthTree.GetFloat("basemaxhealth");
        if (baseMaxHealth == 0)
        {
            BaseMaxHealth = attributes["maxhealth"].AsFloat(20);
            MarkDirty();
        }
        // Otherwise we don't need to read and immediately set the same values back to the healthTree, nor mark it as dirty: and a MarkDirty() here messes up EntityPlayer health on joining a game, if done prior to initialising BehaviorHunger and its MaxHealthModifiers

        secondsSinceLastUpdate = (float)entity.World.Rand.NextDouble();   // Randomise which game tick these update, a starting server would otherwise start all loaded entities with the same zero timer

        ReceiveHailDamage = entity is EntityPlayer;
        if (attributes.KeyExists("receiveHailDamage"))
        {
            ReceiveHailDamage = attributes["receiveHailDamage"].AsBool(entity is EntityPlayer);
        }

        if (attributes.KeyExists("autoRegenSaturationThreshold"))
        {
            AutoRegenSaturationThreshold = attributes["autoRegenSaturationThreshold"].AsFloat();
        }

        if (attributes.KeyExists("saturationPerHealthPoint"))
        {
            SaturationPerHealthPoint = attributes["saturationPerHealthPoint"].AsFloat();
        }

        if (attributes.KeyExists("hurtAnimationCode"))
        {
            HurtAnimationCode = attributes["hurtAnimationCode"].AsString();
        }

        timeBetweenDoTTicksSec = attributes["timeBetweenTicksSec"].AsFloat(0.5f);

        TimeSpan currentTime = TimeSpan.FromMilliseconds(entity.World.ElapsedMilliseconds);
        byte[] data = entity.WatchedAttributes.GetBytes("damageovertime-activeeffects");
        if (data != null)
        {
            ActiveDoTEffectsFromBytes(data);
            foreach (DamageOverTimeEffect effect in ActiveDoTEffects)
            {
                effect.PreviousTickTime = currentTime;
            }
        }
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.World.Side == EnumAppSide.Client) return;

        ProcessDoTEffects(deltaTime);

        if (!entity.Alive) return;

        DamageIfFallingIntoVoid();

        secondsSinceLastUpdate += deltaTime;
        if (secondsSinceLastUpdate < HealthUpdateCooldownSec) return;

        ApplyRegenAndHunger();
        ApplyHailDamage();

        secondsSinceLastUpdate = 0;
    }

    public override void OnEntityDeath(DamageSource damageSourceForDeath)
    {
        base.OnEntityDeath(damageSourceForDeath);

        Health = 0;
    }

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        if (entity.World.Side == EnumAppSide.Client) return;

        if (TurnIntoDoTEffect(damageSource, damage)) return;

        float damageBeforeDelegatesApplied = damage;

        ApplyOnDamageDelegates(damageSource, ref damage);

        if (damageSource.Type == EnumDamageType.Heal)
        {
            ApplyHealing(damageSource, damage);

            entity.OnHurt(damageSource, damage);
            UpdateMaxHealth();
            return;
        }

        if (!entity.Alive || damage <= 0) return;

        LogPlayerToPlayerDamage(damageSource, damage, damageBeforeDelegatesApplied);

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
        }
        else
        {
            if (damage > 1f && HurtAnimationCode != null)
            {
                entity.AnimManager.StartAnimation(HurtAnimationCode);
            }

            entity.PlayEntitySound(HurtEntitySoundCode);
        }
    }

    public override void OnFallToGround(Vec3d lastTerrainContact, double withYMotion)
    {
        if (!entity.Properties.FallDamage) return;
        bool gliding = (entity as EntityAgent)?.ServerControls.Gliding == true;

        double yDistance = Math.Abs(lastTerrainContact.Y - entity.Pos.Y);

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
            Type = EnumDamageType.Gravity,
            IgnoreInvFrames = true
        }, (float)fallDamage);
    }

    public override void GetInfoText(StringBuilder infotext)
    {
        ICoreClientAPI? capi = entity.Api as ICoreClientAPI;
        if (capi?.World.Player?.WorldData?.CurrentGameMode == EnumGameMode.Creative)
        {
            infotext.AppendLine(Lang.Get("Health: {0}/{1}", Health, MaxHealth));
        }
    }

    public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
    {
        if (IsHealable(player.Entity))
        {
            ICanHealCreature? canHealCreature = player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.GetCollectibleInterface<ICanHealCreature>();

            if (canHealCreature != null)
            {
                return canHealCreature.GetHealInteractionHelp(world, es, player).Append(base.GetInteractionHelp(world, es, player, ref handled));
            }
        }

        return base.GetInteractionHelp(world, es, player, ref handled);
    }

    public override string PropertyName() => "health";

    public override void ToBytes(bool forClient)
    {
        base.ToBytes(forClient);
        entity.WatchedAttributes.SetBytes("damageovertime-activeeffects", ActiveDoTEffectsToBytes());
    }


    public void SetMaxHealthModifiers(string key, float value)
    {
        bool dirty = true;
        if (maxHealthModifiers == null)
        {
            maxHealthModifiers = new Dictionary<string, float>();
            if (value == 0f) dirty = false;
        }
        else if (maxHealthModifiers.TryGetValue(key, out float oldValue) && oldValue == value)
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
        Dictionary<string, float>? MaxHealthModifiers = this.maxHealthModifiers;
        if (MaxHealthModifiers != null)
        {
            foreach (KeyValuePair<string, float> val in MaxHealthModifiers) totalMaxHealth += val.Value;
        }

        totalMaxHealth += entity.Stats.GetBlended("maxhealthExtraPoints") - 1;

        bool wasFullHealth = Health >= MaxHealth;

        MaxHealth = totalMaxHealth;

        if (wasFullHealth) Health = MaxHealth;
    }

    public bool IsHealable(EntityAgent eagent, ItemSlot? slot = null)
    {
        ICanHealCreature? canHealCreature = (slot ?? eagent.RightHandItemSlot)?.Itemstack?.Collectible?.GetCollectibleInterface<ICanHealCreature>();

        return Health < MaxHealth && canHealCreature?.CanHeal(entity) == true;
    }

    public virtual int ApplyDoTEffect(
        EnumDamageSource damageSource,
        EnumDamageType damageType,
        int damageTier,
        float totalDamage,
        TimeSpan totalTime,
        int ticksNumber,
        EnumDamageOverTimeEffectType effectType = EnumDamageOverTimeEffectType.Unknown)
    {
        return ApplyDoTEffect(damageSource, damageType, damageTier, totalDamage, totalTime, ticksNumber, (int)effectType);
    }

    public virtual int ApplyDoTEffect(
        EnumDamageSource damageSource,
        EnumDamageType damageType,
        int damageTier,
        float totalDamage,
        TimeSpan totalTime,
        int ticksNumber,
        int effectType = 0)
    {
        DamageOverTimeEffect effect = new()
        {
            DamageSource = damageSource,
            DamageType = damageType,
            DamageTier = damageTier,
            Damage = totalDamage / ticksNumber,
            TickDuration = totalTime / ticksNumber,
            PreviousTickTime = TimeSpan.FromMilliseconds(entity.World.ElapsedMilliseconds),
            TicksLeft = ticksNumber,
            EffectType = effectType
        };
        ActiveDoTEffects.Add(effect);
        return ActiveDoTEffects.Count - 1;
    }

    public virtual void StopDoTEffect(int effectType, int amount = int.MaxValue)
    {
        int count = 0;
        foreach (DamageOverTimeEffect effect in ActiveDoTEffects.Where(effect => effect.EffectType == effectType))
        {
            effect.TicksLeft = 0;
            count++;

            if (count >= amount) break;
        }
    }


    protected virtual void DamageIfFallingIntoVoid()
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

    protected virtual void ApplyHailDamage()
    {
        if (!ReceiveHailDamage) return;

        // A costly check every 1s for hail damage, but it applies only to entities who are in the open
        int rainy = entity.World.BlockAccessor.GetRainMapHeightAt((int)entity.ServerPos.X, (int)entity.ServerPos.Z);
        if (entity.ServerPos.Y >= rainy)
        {
            WeatherSystemBase wsys = entity.Api.ModLoader.GetModSystem<WeatherSystemBase>();
            PrecipitationState state = wsys.GetPrecipitationState(entity.ServerPos.XYZ);

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

    protected virtual void ApplyRegenAndHunger()
    {
        // higher performance to read this TreeAttribute only once
        float health = Health;
        float maxHealth = MaxHealth;
        if (health >= maxHealth) return;

        float healthRegenSpeed = entity is EntityPlayer ? entity.Api.World.Config.GetString("playerHealthRegenSpeed", "1").ToFloat() : entity.WatchedAttributes.GetFloat("regenSpeed", 1);

        // previous value = 0.01 , -> 0.01 / 30 = 0.000333333f (60 * 0,5 = 30 (SpeedOfTime * CalendarSpeedMul))
        float healthRegenPerGameSecond = 0.000333333f * healthRegenSpeed;
        float multiplierPerGameSec = secondsSinceLastUpdate * entity.Api.World.Calendar.SpeedOfTime * entity.Api.World.Calendar.CalendarSpeedMul;

        // Only players have the hunger behavior, and the different nutrient saturations
        if (entity is EntityPlayer player)
        {
            EntityBehaviorHunger? hungerBehavior = entity.GetBehavior<EntityBehaviorHunger>();

            if (hungerBehavior != null && player.Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                // When below 75% satiety, autoheal starts dropping
                healthRegenPerGameSecond = GameMath.Clamp(healthRegenPerGameSecond * hungerBehavior.Saturation / hungerBehavior.MaxSaturation * 1 / AutoRegenSaturationThreshold, 0, healthRegenPerGameSecond);

                hungerBehavior.ConsumeSaturation(SaturationPerHealthPoint * multiplierPerGameSec * healthRegenPerGameSecond);
            }
        }

        Health = Math.Min(health + multiplierPerGameSec * healthRegenPerGameSecond, maxHealth);
    }

    protected virtual void ApplyOnDamageDelegates(DamageSource damageSource, ref float damage)
    {
        if (onDamaged != null)
        {
            foreach (OnDamagedDelegate dele in onDamaged.GetInvocationList().OfType<OnDamagedDelegate>())
            {
                damage = dele.Invoke(damage, damageSource);
            }
        }
    }

    protected virtual void ApplyHealing(DamageSource damageSource, float damage)
    {
        if (damageSource.Source != EnumDamageSource.Revive)
        {
            Health = Math.Min(Health + damage, MaxHealth);
        }
        else
        {
            damage = Math.Min(damage, MaxHealth);
            Health = damage;
        }
    }

    protected virtual void LogPlayerToPlayerDamage(DamageSource damageSource, float damage, float damageBeforeDelegatesApplied)
    {
        if (entity is not EntityPlayer player || damageSource.GetCauseEntity() is not EntityPlayer otherPlayer) return;
        
        string weapon;
        if (damageSource.SourceEntity != otherPlayer)
        {
            weapon = damageSource.SourceEntity.Code.ToString();
        }
        else
        {
            weapon = otherPlayer.Player.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible.Code.ToString() ?? "hands";
        }

        entity.Api.Logger.Audit($"{player.Player.PlayerName} at {entity.Pos.AsBlockPos} got {damage}/{damageBeforeDelegatesApplied} damage {damageSource.Type.ToString().ToLowerInvariant()} {weapon} by {otherPlayer.GetName()}");
    }

    protected virtual void ProcessDoTEffects(float dt)
    {
        if (!entity.Alive)
        {
            ActiveDoTEffects.Clear();
            return;
        }

        if (timeSinceLastDoTTickSec < timeBetweenDoTTicksSec)
        {
            timeSinceLastDoTTickSec += dt;
            return;
        }
        timeSinceLastDoTTickSec = 0;

        TimeSpan currentTime = TimeSpan.FromMilliseconds(entity.World.ElapsedMilliseconds);

        float totalHealthFutureGain = 0f;
        float healthVelocity = 0f;

        for (int index = ActiveDoTEffects.Count - 1; index >= 0; index--)
        {
            DamageOverTimeEffect effect = ActiveDoTEffects[index];

            TimeSpan elapsed = currentTime - effect.PreviousTickTime;
            if (elapsed >= effect.TickDuration)
            {
                entity.ReceiveDamage(new DamageSource()
                {
                    Source = effect.DamageSource,
                    Type = effect.DamageType,
                    DamageTier = effect.DamageTier,
                }, effect.Damage);

                effect.TicksLeft--;

                if (effect.TicksLeft <= 0)
                {
                    ActiveDoTEffects.RemoveAt(index);
                }
                else
                {
                    effect.PreviousTickTime = currentTime;
                }
            }

            if (effect.DamageType == EnumDamageType.Heal)
            {
                totalHealthFutureGain += effect.Damage * effect.TicksLeft;
                healthVelocity += effect.Damage * effect.TicksLeft;
            }
            else
            {
                healthVelocity -= effect.Damage * effect.TicksLeft;
            }
        }

        if (Math.Abs(totalHealthFutureGain) > 0.1)
        {
            FutureHealth = Health + totalHealthFutureGain;
        }
        else
        {
            FutureHealth = null;
        }

        HealthChangeRate = healthVelocity;
    }

    protected virtual byte[] ActiveDoTEffectsToBytes()
    {
        MemoryStream stream = new();
        BinaryWriter writer = new(stream);

        writer.Write(ActiveDoTEffects.Count);
        foreach (DamageOverTimeEffect effect in ActiveDoTEffects)
        {
            effect.ToBytes(writer);
        }

        return stream.ToArray();
    }

    protected virtual void ActiveDoTEffectsFromBytes(byte[] bytes)
    {
        MemoryStream stream = new(bytes);
        BinaryReader reader = new(stream);

        ActiveDoTEffects.Clear();

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ActiveDoTEffects.Add(DamageOverTimeEffect.FromBytes(reader));
        }
    }

    protected virtual bool TurnIntoDoTEffect(DamageSource damageSource, float damage)
    {
        if (damageSource.Duration <= TimeSpan.Zero) return false;

        ApplyDoTEffect(damageSource.Source, damageSource.Type, damageSource.DamageTier, damage, damageSource.Duration, damageSource.TicksPerDuration, damageSource.DamageOverTimeType);

        return true;
    }
}
