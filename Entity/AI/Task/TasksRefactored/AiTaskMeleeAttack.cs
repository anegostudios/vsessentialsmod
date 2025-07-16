using Newtonsoft.Json;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Tries to deal damage to a target.<br/>
/// If fearReductionFactor less or equal to 0, this entity wont attack.<br/>
/// <br/>
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2:<br/>
/// - attackRange => seekingRange<br/>
/// - minDist => maxAttackDistance<br/>
/// - minVerDist => maxAttackVerticalDistance<br/>
/// - damagePlayerAtMs => damageWindowMs
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskMeleeAttackConfig : AiTaskBaseTargetableConfig
{
    /// <summary>
    /// Attack damage.
    /// </summary>
    [JsonProperty] public float Damage = 0f;

    /// <summary>
    /// Attack knockback strength, equal to half of square root of <see cref="Damage"/> if not specified.
    /// </summary>
    [JsonProperty] public float KnockbackStrength = float.MinValue;

    /// <summary>
    /// Attack damage type. Case sensitive.
    /// </summary>
    [JsonProperty] public EnumDamageType DamageType = EnumDamageType.BluntAttack;

    /// <summary>
    /// Attack damage tier.
    /// </summary>
    [JsonProperty] public int DamageTier = 0;

    /// <summary>
    /// Distance to target should be less than this value to deal damage.
    /// </summary>
    [JsonProperty] public float MaxAttackDistance = 2f;

    /// <summary>
    /// Vertical distance to target should be less than this value to deal damage.
    /// </summary>
    [JsonProperty] public float MaxAttackVerticalDistance = 1f;

    /// <summary>
    /// Maximum angular distance between direction this entity faces and direction to target, for this entity to deal damage to target.<br/>
    /// Entity will turn towards target if <see cref="TurnToTarget"/> set to 'true' until target is in angular range.
    /// </summary>
    [JsonProperty] public float AttackAngleRangeDeg = 20f;

    /// <summary>
    /// This task will finish after this amount of time.<br/>
    /// Task cooldown will also be adjusted to be greater or equal to attack duration.
    /// </summary>
    [JsonProperty] public int AttackDurationMs = 1000;

    /// <summary>
    /// This task will try to deal damage in this time window.<br/>
    /// Attack can fail due to target not having direct contact. Attack will be retried until damage is dealt or task is out of this time window.
    /// </summary>
    [JsonProperty] public int[] DamageWindowMs = [0, int.MaxValue];

    /// <summary>
    /// If set to 'true', entity will try to turn to face target before starting attack animation.<br/>
    /// Time entity spends to turn to target is not counted towards attack duration or damage window.
    /// </summary>
    [JsonProperty] public bool TurnToTarget = true;

    /// <summary>
    /// If set to 'true', if target died from this attack, entity will try to set 'saturated' emotion state and reset 'lastMealEatenTotalHours'.<br/>
    /// Player death will not trigger 'lastMealEatenTotalHours' unless <see cref="PlayerIsMeal"/> set to true
    /// </summary>
    [JsonProperty] public bool EatAfterKill = true;

    /// <summary>
    /// If set to 'true', player death from this attack will reset 'lastMealEatenTotalHours' if <see cref="EatAfterKill"/> to 'true'.
    /// </summary>
    [JsonProperty] public bool PlayerIsMeal = false;

    /// <summary>
    /// Whether attack should ignore invulnerability frames.
    /// </summary>
    [JsonProperty] public bool IgnoreInvFrames = true;

    /// <summary>
    /// if set to 'true' attack damage will be multiplied by <see cref="GlobalConstants.CreatureDamageModifier"/>.
    /// </summary>
    [JsonProperty] public bool AffectedByGlobalDamageMultiplier = true;

    /// <summary>
    /// Ignore checks for being able to start task, if was recently attacked.
    /// </summary>
    [JsonProperty] public bool RetaliateUnconditionally = false;



    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (KnockbackStrength <= float.MinValue)
        {
            KnockbackStrength = Damage >= 0 ? GameMath.Sqrt(Damage / 4f) : GameMath.Sqrt(- Damage / 4f);
        }

        int cooldownRange = MaxCooldownMs - MinCooldownMs;
        MinCooldownMs = Math.Max(MinCooldownMs, AttackDurationMs);
        MaxCooldownMs = Math.Max(Math.Max(MaxCooldownMs, MinCooldownMs + cooldownRange), AttackDurationMs);
    }
}

public class AiTaskMeleeAttackR : AiTaskBaseTargetableR
{
    public static bool ShowExtraDamageInfo { get; set; } = true;

    private AiTaskMeleeAttackConfig Config => GetConfig<AiTaskMeleeAttackConfig>();

    protected bool damageInflicted;
    protected float currentTurnRadPerSec;
    protected bool didStartAnimation;
    protected bool fullyTamed;

    public AiTaskMeleeAttackR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskMeleeAttackConfig>(entity, taskConfig, aiConfig);
        
        if (Config.DamageWindowMs.Length != 2)
        {
            string errorMessage = $"Error loading AI task config for task '{Config.Code}' and entity '{entity.Code}': damageWindow should be an array of two integers.";
            entity.Api.Logger.Error(errorMessage);
            throw new ArgumentException(errorMessage);
        }

        int generation = GetOwnGeneration();
        fullyTamed = generation >= Config.TamingGenerations;

        SetExtraInfoText();
    }

    public override bool ShouldExecute()
    {
        if (!(PreconditionsSatisficed() || Config.RetaliateUnconditionally && RecentlyAttacked)) return false;

        float fearReductionFactor = GetFearReductionFactor();
        if (fearReductionFactor <= 0) return false;

        if (!RecentlyAttacked)
        {
            ClearAttacker();
        }

        if (ShouldRetaliate())
        {
            targetEntity = attackedByEntity;
        }
        else
        {
            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);

            targetEntity = partitionUtil.GetNearestEntity(pos, Config.SeekingRange * fearReductionFactor, entity => IsTargetableEntity(entity, Config.SeekingRange * fearReductionFactor), Config.SearchType);
        }

        return targetEntity != null;
    }

    public override void StartExecute()
    {
        didStartAnimation = false;
        damageInflicted = false;
        currentTurnRadPerSec = pathTraverser.curTurnRadPerSec;

        if (!Config.TurnToTarget) base.StartExecute();
    }

    public override bool ContinueExecute(float dt)
    {
        if (targetEntity == null) return false;
        
        EntityPos thisPos = entity.ServerPos;
        EntityPos targetPos = targetEntity.ServerPos;
        if (thisPos.Dimension != targetPos.Dimension) return false;   // One or other changed dimension, no further attack processing

        bool correctYaw = true;

        if (Config.TurnToTarget)
        {
            float desiredYaw = (float)Math.Atan2(targetPos.X - thisPos.X, targetPos.Z - thisPos.Z);
            float yawDistance = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
            entity.ServerPos.Yaw += GameMath.Clamp(yawDistance, -currentTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier, currentTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier);
            entity.ServerPos.Yaw %= GameMath.TWOPI;

            correctYaw = Math.Abs(yawDistance) < Config.AttackAngleRangeDeg * GameMath.DEG2RAD;

            if (correctYaw && !didStartAnimation)
            {
                didStartAnimation = true;
                base.StartExecute();
            }
        }
        
        if (executionStartTimeMs + Config.DamageWindowMs[0] > entity.World.ElapsedMilliseconds) return true;

        if (executionStartTimeMs + Config.DamageWindowMs[0] <= entity.World.ElapsedMilliseconds &&
            executionStartTimeMs + Config.DamageWindowMs[1] >= entity.World.ElapsedMilliseconds &&
            !damageInflicted &&
            correctYaw)
        {
            damageInflicted = AttackTarget();
        }

        if (executionStartTimeMs + Config.AttackDurationMs > entity.World.ElapsedMilliseconds) return true;
        
        return false;
    }

    protected virtual bool AttackTarget()
    {
        if (targetEntity == null) return false;
        
        if (!HasDirectContact(targetEntity, Config.MaxAttackDistance, Config.MaxAttackVerticalDistance)) return false;

        bool alive = targetEntity.Alive;

        targetEntity.ReceiveDamage(
            new DamageSource()
            {
                Source = EnumDamageSource.Entity,
                SourceEntity = entity,
                Type = Config.DamageType,
                DamageTier = Config.DamageTier,
                KnockbackStrength = Config.KnockbackStrength,
                IgnoreInvFrames = Config.IgnoreInvFrames
            },
            Config.Damage * (Config.AffectedByGlobalDamageMultiplier ? GlobalConstants.CreatureDamageModifier : 1)
        );

        if (entity is IMeleeAttackListener listener)
        {
            listener.DidAttack(targetEntity);
        }

        if (alive && !targetEntity.Alive && Config.EatAfterKill)
        {
            if (Config.PlayerIsMeal || targetEntity is not EntityPlayer)
            {
                entity.WatchedAttributes.SetDouble("lastMealEatenTotalHours", entity.World.Calendar.TotalHours);
            }
            emotionStatesBehavior?.TryTriggerState("saturated", targetEntity.EntityId);
        }

        return true;
    }

    protected override bool IsTargetableEntity(Entity target, float range)
    {
        if (fullyTamed && (IsNonAttackingPlayer(target) || entity.ToleratesDamageFrom(target))) return false;

        if (!base.IsTargetableEntity(target, range)) return false;

        return HasDirectContact(target, Config.MaxAttackDistance, Config.MaxAttackVerticalDistance);
    }

    protected override bool ShouldRetaliate()
    {
        return attackedByEntity != null && base.ShouldRetaliate() && HasDirectContact(attackedByEntity, Config.MaxAttackDistance, Config.MaxAttackVerticalDistance);
    }

    protected void SetExtraInfoText() // @TODO rework to work with several melee attack tasks
    {
        ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("extraInfoText");
        tree.SetString("dmgTier", Lang.Get("Damage tier: {0}", Config.DamageTier));
        if (ShowExtraDamageInfo)
        {
            tree.SetString("dmgDamage", Lang.Get("Damage: {0}", Config.Damage));
            tree.SetString("dmgType", Lang.Get("Damage type: {0}", Lang.Get($"{Config.DamageType}")));
        }
    }
}
