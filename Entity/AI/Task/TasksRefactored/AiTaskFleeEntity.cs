using Newtonsoft.Json;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

/// <summary>
/// Makes this entity flee from a target entity.<br/><br/>
/// 
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2<br/>
/// - use entityLightLevels instead of minDayLight<br/>
/// - targetOnlyInteractableEntities default value: false => true<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskFleeEntityConfig : AiTaskBaseTargetableConfig
{
    /// <summary>
    /// Entity moving speed while fleeing.
    /// </summary>
    [JsonProperty] public float MoveSpeed = 0.02f;

    /// <summary>
    /// If set to true check for light level will always fail under sea level offset by <see cref="DeepDayLightLevelOffset"/>.
    /// </summary>
    [JsonProperty] public bool IgnoreDeepDayLight = false;

    /// <summary>
    /// Distance entity will try to flee before finishing this task.<br/>
    /// By default is set to <see cref="AiTaskBaseTargetableConfig.SeekingRange"/> + 15.<br/>
    /// Target and this entities sizes divided by 2 are added to fleeing distance.
    /// </summary>
    [JsonProperty] public float FleeingDistance = 0;
    
    /// <summary>
    /// Time until which entity will try flee before stopping this task if target is till in sight.
    /// </summary>
    [JsonProperty] public int FleeDurationMs = 9000;

    /// <summary>
    /// Time until which entity will try flee before stopping this task if target is no longer in sight.
    /// </summary>
    [JsonProperty] public int FleeDurationWhenTargetLost = 5000;

    /// <summary>
    /// Chance to flee from the entity that dealt damage to this entity bypassing any checks for cause entity to be targetable.
    /// </summary>
    [JsonProperty] public float InstaFleeOnDamageChance = 0;

    /// <summary>
    /// If set to true and temporal stability is turned on this entity will be considered low-stability-attracted.<br/>
    /// Entity will also considered low-stability-attracted if it has its attribute 'spawnCloserDuringLowStability' set to 'true'.<br/>
    /// Low-stability-attracted entities will target only <see cref="EntityAgent"/> and for players will check their temporal stability level (<see cref="RequiredTemporalStability"/>).
    /// </summary>
    [JsonProperty] public bool SpawnCloserDuringLowStability = false;

    /// <summary>
    /// If this entity tolerates damage from target entity, fleeing distance will be multiplied by this factor.<br/>
    /// To determine if entity tolerates damage the 'ToleratesDamageFrom' method of <see cref="EntityAgent"/> is used.<br/>
    /// 'EntityBehaviorOwnable' and 'EntityBehaviorRideable' behaviors affect this check in 'ToleratesDamageFrom' method.
    /// </summary>
    [JsonProperty] public float FleeDistanceReductionIfToleratesDamage = 0.5f;

    /// <summary>
    /// Chance at each tick to adjust fleeing direction to new target entity position.
    /// </summary>
    [JsonProperty] public float ChanceToAdjustDirection = 0.2f;

    /// <summary>
    /// Chance at each tick to repeat this entity light level check.
    /// </summary>
    [JsonProperty] public float ChanceToCheckLightLevel = 0.25f;
    
    /// <summary>
    /// Sound chance will be increased by this amount each tick up to <see cref="AiTaskBaseConfig.SoundChance"/>.
    /// </summary>
    [JsonProperty] public float SoundChanceRestoreRate = 0.002f;

    /// <summary>
    /// Sound chance will decrease down to <see cref="SoundChanceMinimum"/> each time this task starts.
    /// </summary>
    [JsonProperty] public float SoundChanceDecreaseRate = 0.2f;

    /// <summary>
    /// Minimum sound chance, <see cref="SoundChanceDecreaseRate"/>.
    /// </summary>
    [JsonProperty] public float SoundChanceMinimum = 0.25f;

    /// <summary>
    /// Player temporal stability should be higher than this value for low-stability-attracted creatures to flee from the player.<br/>
    /// Creature is considered low-stability-attracted if its attribute 'spawnCloserDuringLowStability' set to 'true' or <see cref="SpawnCloserDuringLowStability"/> set to 'true'. Also temporal stability should be turned on in world config.
    /// </summary>
    [JsonProperty] public float RequiredTemporalStability = 0.25f;

    /// <summary>
    /// If <see cref="IgnoreDeepDayLight"/> set to true check for light level will always fail under sea level offset by this value.
    /// </summary>
    [JsonProperty] public float DeepDayLightLevelOffset = -2;




    public bool LowStabilityAttracted;

    public float FleeSeekRangeDifference = 15;

    public float MaxSoundChance;



    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (FleeingDistance <= 0)
        {
            FleeingDistance = SeekingRange + FleeSeekRangeDifference;
        }

        MaxSoundChance = SoundChance;
        FleeSeekRangeDifference = FleeingDistance - SeekingRange;
        LowStabilityAttracted = entity.World.Config.GetString("temporalStability").ToBool(true) && (entity.Properties.Attributes?["spawnCloserDuringLowStability"]?.AsBool() == true || SpawnCloserDuringLowStability);
    }
}

public class AiTaskFleeEntityR : AiTaskBaseTargetableR
{
    public override bool AggressiveTargeting => false;

    private AiTaskFleeEntityConfig Config => GetConfig<AiTaskFleeEntityConfig>();

    protected Vec3d targetPos = new();
    protected Vec3d ownPos = new();
    protected float targetYaw = 0f;
    protected long fleeStartMs;
    protected bool stuck;
    protected float currentFleeingDistance;
    protected bool instaFleeNow = false;

    protected const float minimumPathTraversalTolerance = 0.5f;

    #region Variables to reduce heap allocations cause we dont use structs
    private readonly Vec3d tmpVec3 = new(); // ContinueExecute
    private readonly Vec3d tmpVec2 = new(); // Traversable
    private readonly Vec3d tmpVec1 = new(); // UpdateTargetPosFleeMode
    #endregion

    public AiTaskFleeEntityR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskFleeEntityConfig>(entity, taskConfig, aiConfig);
    }

    public virtual void InstaFleeFrom(Entity fromEntity)
    {
        instaFleeNow = true;
        targetEntity = fromEntity;
    }

    public override bool ShouldExecute()
    {
        Config.SoundChance = Math.Min(Config.MaxSoundChance, Config.SoundChance + Config.SoundChanceRestoreRate);

        if (instaFleeNow) return TryInstaFlee();
        if (!PreconditionsSatisficed()) return false;

        ownPos.SetWithDimension(entity.ServerPos);
        float adjustedRange = ((Config.WhenInEmotionState != null) ? 1 : GetFearReductionFactor()) * Config.SeekingRange;

        entity.World.FrameProfiler.Mark("task-fleeentity-shouldexecute-init");

        if (Config.LowStabilityAttracted)
        {
            targetEntity = partitionUtil.GetNearestEntity(ownPos, adjustedRange, entity =>
            {
                if (entity is not EntityAgent) return false;
                if (!IsTargetableEntity(entity, adjustedRange)) return false;
                if (entity is not EntityPlayer) return true;
                return entity.WatchedAttributes.GetDouble("temporalStability", 1) > Config.RequiredTemporalStability;
            }, Config.SearchType) as EntityAgent;
        }
        else
        {
            targetEntity = partitionUtil.GetNearestEntity(ownPos, adjustedRange, entity => IsTargetableEntity(entity, adjustedRange), Config.SearchType);
        }

        currentFleeingDistance = Config.FleeingDistance;

        entity.World.FrameProfiler.Mark("task-fleeentity-shouldexecute-entitysearch");

        if (targetEntity != null)
        {
            if (entity.ToleratesDamageFrom(targetEntity)) currentFleeingDistance *= Config.FleeDistanceReductionIfToleratesDamage;

            currentFleeingDistance += (entity.SelectionBox.XSize + targetEntity.SelectionBox.XSize) / 2f;
            
            float yaw = (float)Math.Atan2(targetEntity.ServerPos.X - entity.ServerPos.X, targetEntity.ServerPos.Z - entity.ServerPos.Z);
            UpdateTargetPosFleeMode(targetPos, yaw);
            return true;
        }

        return false;
    }

    public override void StartExecute()
    {
        base.StartExecute();

        Config.SoundChance = Math.Max(Config.SoundChanceMinimum, Config.SoundChance - Config.SoundChanceDecreaseRate);

        float entitySizeOffset = Math.Max(minimumPathTraversalTolerance, entity.SelectionBox.XSize / 2f );

        pathTraverser.WalkTowards(targetPos, Config.MoveSpeed, entitySizeOffset, OnGoalReached, OnStuck);

        fleeStartMs = entity.World.ElapsedMilliseconds;
        stuck = false;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;

        if (world.Rand.NextDouble() <= Config.ChanceToAdjustDirection)
        {
            float yaw = targetEntity == null ? -targetYaw : (float)Math.Atan2(targetEntity.ServerPos.X - entity.ServerPos.X, targetEntity.ServerPos.Z - entity.ServerPos.Z);

            tmpVec3.Set(targetPos);

            UpdateTargetPosFleeMode(tmpVec3, yaw);
            pathTraverser.CurrentTarget.X = tmpVec3.X;
            pathTraverser.CurrentTarget.Y = tmpVec3.Y;
            pathTraverser.CurrentTarget.Z = tmpVec3.Z;
            pathTraverser.Retarget();
        }

        if (targetEntity != null && entity.ServerPos.SquareDistanceTo(targetEntity.ServerPos) > currentFleeingDistance * currentFleeingDistance)
        {
            return false;
        }
        if (targetEntity == null && entity.World.ElapsedMilliseconds - fleeStartMs > Config.FleeDurationWhenTargetLost)
        {
            return false;
        }

        if (world.Rand.NextDouble() < Config.ChanceToCheckLightLevel)
        {
            return CheckEntityLightLevel();
        }

        return !stuck && (targetEntity == null || targetEntity.Alive) && (entity.World.ElapsedMilliseconds - fleeStartMs < Config.FleeDurationMs) && pathTraverser.Active;
    }

    public override void FinishExecute(bool cancelled)
    {
        pathTraverser.Stop();
        base.FinishExecute(cancelled);
    }

    public override void OnEntityHurt(DamageSource source, float damage)
    {
        base.OnEntityHurt(source, damage);

        if (source.Type != EnumDamageType.Heal && entity.World.Rand.NextDouble() < Config.InstaFleeOnDamageChance)
        {
            instaFleeNow = true;
            targetEntity = source.GetCauseEntity();
        }
    }

    protected override bool CheckEntityLightLevel()
    {
        // This code section controls drifter behavior - they retreat (flee slowly) from the player in the daytime, this is "switched off" below ground or at night, also switched off in temporal storms
        // Has to be checked every tick because the drifter attributes change during temporal storms  (grrr, this is a slow way to do it)
        if (entity.Attributes.GetBool("ignoreDaylightFlee", false)) return false;
        if (Config.IgnoreDeepDayLight && entity.ServerPos.Y < world.SeaLevel + Config.DeepDayLightLevelOffset) return false;

        return base.CheckEntityLightLevel();
    }

    protected virtual bool TryInstaFlee()
    {
        // Beyond visual range: Run in looking direction
        if (targetEntity == null || entity.ServerPos.DistanceTo(targetEntity.ServerPos) > Config.SeekingRange)
        {
            float cosYaw = GameMath.Cos(entity.ServerPos.Yaw);
            float sinYaw = GameMath.Sin(entity.ServerPos.Yaw);
            double offset = 200;
            targetPos.Set(entity.ServerPos.X + sinYaw * offset, entity.ServerPos.Y, entity.ServerPos.Z + cosYaw * offset);
            targetYaw = entity.ServerPos.Yaw;
            targetEntity = null;
        }
        else
        {
            currentFleeingDistance = (float)entity.ServerPos.DistanceTo(targetEntity.ServerPos) + Config.FleeSeekRangeDifference;
            if (entity.ToleratesDamageFrom(targetEntity)) currentFleeingDistance /= Config.FleeDistanceReductionIfToleratesDamage;// changed from 2.5 to 2.0 (be default) because it is too many magic number other wise, be consistent dammit
            UpdateTargetPosFleeMode(targetPos, entity.ServerPos.Yaw);
        }

        instaFleeNow = false;

        return true;
    }

    protected virtual void OnStuck()
    {
        stuck = true;
    }

    protected virtual void OnGoalReached()
    {
        pathTraverser.Retarget();
    }

    protected void UpdateTargetPosFleeMode(Vec3d targetPos, float yaw)
    {
        // Simple steering behavior
        tmpVec1.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
        tmpVec1.Ahead(0.9, 0, yaw);

        // Try straight
        if (Traversable(tmpVec1))
        {
            targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw);
            return;
        }

        // Try 90 degrees left
        tmpVec1.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
        tmpVec1.Ahead(0.9, 0, yaw - GameMath.PIHALF);
        if (Traversable(tmpVec1))
        {
            targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw - GameMath.PIHALF);
            return;
        }

        // Try 90 degrees right
        tmpVec1.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
        tmpVec1.Ahead(0.9, 0, yaw + GameMath.PIHALF);
        if (Traversable(tmpVec1))
        {
            targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw + GameMath.PIHALF);
            return;
        }

        // Try backwards
        tmpVec1.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);
        tmpVec1.Ahead(0.9, 0, yaw + GameMath.PI);
        targetPos.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z).Ahead(10, 0, yaw + GameMath.PI);
    }

    protected bool Traversable(Vec3d pos)
    {
        return
            !world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, pos, false) ||
            !world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, tmpVec2.Set(pos).Add(0, Math.Min(1, stepHeight), 0), false)
        ;
    }
}
