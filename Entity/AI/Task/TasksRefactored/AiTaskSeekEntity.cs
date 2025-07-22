using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

// @TODO write description
/// <summary>
/// Seeks, jumps, circles.
/// 
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2<br/>
/// - hardcoded additional execution chance when no emotion state behavior is not present was removed, use adjust executionChance value instead <br/>
/// - leapAnimation => jumpAnimationCode<br/>
/// - leapChance => JumpChance<br/>
/// - leapAtTarget => JumpAtTarget<br/>
/// - belowTempSeekingRange => belowTemperatureSeekingRange<br/>
/// - belowTempThreshold => belowTemperatureThreshold<br/>
/// - maxFollowTime => maxFollowTimeSec<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskSeekEntityConfig : AiTaskBaseTargetableConfig
{
    /// <summary>
    /// Entity moving speed.
    /// </summary>
    [JsonProperty] public float MoveSpeed = 0.02f;

    /// <summary>
    /// Jumping animation code. Set to 'null' for jumping without animation.
    /// </summary>
    [JsonProperty] public string? JumpAnimationCode = "jump";

    /// <summary>
    /// Chance to jump at each tick.
    /// </summary>
    [JsonProperty] public float JumpChance = 1f;

    /// <summary>
    /// Use to adjust jumping height. Arbitrary units.
    /// </summary>
    [JsonProperty] public float JumpHeightFactor = 1f;

    /// <summary>
    /// Set to 'true' for entity to try to jump to target.
    /// </summary>
    [JsonProperty] public bool JumpAtTarget = false;

    /// <summary>
    /// Pathfinder will consider that entity reached the target when distance to target is lower than some minimum distance.<br/>
    /// Minimum distance is based on target and entity selection boxes. Extra target distance value is also added to minimum distance.
    /// </summary>
    [JsonProperty] public float ExtraTargetDistance = 0f;

    /// <summary>
    /// Seeking range below <see cref="BelowTemperatureThreshold"/> temperature.
    /// </summary>
    [JsonProperty] public float BelowTemperatureSeekingRange = 25f;

    /// <summary>
    /// Temperature below which to change seeking range. In degrees Celsius.
    /// </summary>
    [JsonProperty] public float BelowTemperatureThreshold = -99;

    /// <summary>
    /// Time in seconds until entity gives up following.
    /// </summary>
    [JsonProperty] public float MaxFollowTimeSec = 60;

    /// <summary>
    /// Is set to 'true' other entities with same herd id will be triggered to seek same target.
    /// </summary>
    [JsonProperty] public bool AlarmHerd = false;

    /// <summary>
    /// Task will alarm entities with same herd id in this range. If not set, <see cref="SeekingRange"/> will be used instead.
    /// </summary>
    [JsonProperty] public float HerdAlarmRange = 0;

    /// <summary>
    /// Affects pathfinding algorithm, see <see cref="EnumAICreatureType"/>. If not specified, value from the ai task behavior config will be used.
    /// </summary>
    [JsonProperty] public EnumAICreatureType? AiCreatureType = null;

    /// <summary>
    /// Stop task if being attacked by target entity, and it is outside of seeking range.
    /// </summary>
    [JsonProperty] public bool StopWhenAttackedByTargetOutsideOfSeekingRange = true;

    /// <summary>
    /// Trigger flee ai tasks if being attacked by target entity, and it is outside of seeking range.
    /// </summary>
    [JsonProperty] public bool FleeWhenAttackedByTargetOutsideOfSeekingRange = true;

    /// <summary>
    /// Ignore checks for being able to start task, if was recently attacked.
    /// </summary>
    [JsonProperty] public bool RetaliateUnconditionally = true;

    /// <summary>
    /// Multiplies seeking range in case of targeting entity that was the last who recently attacked this entity.
    /// </summary>
    [JsonProperty] public float RetaliationSeekingRangeFactor = 1.5f;
    
    /// <summary>
    /// Restricts how often pathfinder should update path to target for better performance.
    /// </summary>
    [JsonProperty] public float PathUpdateCooldownSec = 0.75f;

    /// <summary>
    /// Min distance target entity should move to trigger pathfinder to recalculate path to target.
    /// </summary>
    [JsonProperty] public float MinDistanceToUpdatePath = 3f;
    
    /// <summary>
    /// Entity will move to place where target entity is currently plus target entity velocity multiplied by this factor.
    /// </summary>
    [JsonProperty] public float MotionAnticipationFactor = 10f;

    /// <summary>
    /// Jump animation will be stopped if not finished in this amount of time.
    /// </summary>
    [JsonProperty] public int JumpAnimationTimeoutMs = 2000;

    /// <summary>
    /// Entity will jump only if distance to target is within this range.
    /// </summary>
    [JsonProperty] public float[] DistanceToTargetToJump = [0.5f, 4f];

    /// <summary>
    /// If this entity is higher than the target entity by more than this value, then this entity wont jump.
    /// </summary>
    [JsonProperty] public float MaxHeightDifferenceToJump = 0.1f;

    /// <summary>
    /// Cooldown between jumps.
    /// </summary>
    [JsonProperty] public int JumpCooldownMs = 3000;

    /// <summary>
    /// List of animations that will be stopped before playing jump animation.
    /// </summary>
    [JsonProperty] public string[] AnimationToStopForJump = ["walk", "run"];

    /// <summary>
    /// Ho much target entity velocity will affect where this entity jumps.
    /// </summary>
    [JsonProperty] public float JumpMotionAnticipationFactor = 80;

    /// <summary>
    /// This entity velocity during jump will be multiplied by this factor.
    /// </summary>
    [JsonProperty] public float JumpSpeedFactor = 1;

    /// <summary>
    /// How much this entity velocity will be reduced after jump is landed.
    /// </summary>
    [JsonProperty] public float AfterJumpSpeedReduction = 0.5f;

    /// <summary>
    /// Entity will flee if unable to reach or circle around target.
    /// </summary>
    [JsonProperty] public bool FleeIfCantReach = true;

    /// <summary>
    /// If set to true and this entity is fully tamed, this ai task will ignore player. If player attacked the entity, it will ignore player only if can tolerate damage from them.
    /// </summary>
    [JsonProperty] public bool IgnorePlayerIfFullyTamed = false;

    [JsonProperty] public float MaxVerticalJumpSpeed = 0.13f;

    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (HerdAlarmRange <= 0)
        {
            HerdAlarmRange = SeekingRange;
        }
    }
}

public class AiTaskSeekEntityR : AiTaskBaseTargetableR
{
    public override string Id => "seekentity";

    private AiTaskSeekEntityConfig Config => GetConfig<AiTaskSeekEntityConfig>();

    protected int pathSearchDepth = 3500;
    protected int pathDeepSearchDepth = 10000;
    protected float chanceOfDeepSearch = 0.05f;
    protected int updatePathDepth = 2000;
    protected int circlePathSearchDepth = 3500;

    protected float currentSeekingRange;
    protected float currentFollowTimeSec;
    protected float lastPathUpdateSecondsSec;
    protected EnumAttackPattern attackPattern;
    protected long finishedMs;
    protected bool jumpAnimationOn;
    protected long jumpedMs = 0;
    protected readonly Dictionary<long, int> futilityCounters = [];
    protected readonly Vec3d targetPosition = new();
    protected readonly Vec3d previousPosition = new();
    protected bool updatedPathAfterLanding = true;
    protected Vec3d jumpHorizontalVelocity = new();

    #region Variables to reduce heap allocations in HasDirectContact methods cause we dont use structs
    private readonly Vec3d posBuffer = new();
    #endregion

    public AiTaskSeekEntityR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskSeekEntityConfig>(entity, taskConfig, aiConfig);
    }

    public override bool ShouldExecute()
    {
        if (jumpAnimationOn && entity.World.ElapsedMilliseconds - finishedMs > Config.JumpAnimationTimeoutMs)
        {
            entity.AnimManager.StopAnimation("jump");
            jumpAnimationOn = false;
        }

        if (!(PreconditionsSatisficed() || Config.RetaliateUnconditionally && RecentlyAttacked)) return false;

        SetSeekingRange();

        if (!RecentlyAttacked)
        {
            ClearAttacker();
        }

        if (ShouldRetaliate() && attackedByEntity != null)
        {
            targetEntity = attackedByEntity;
            targetPosition.SetWithDimension(attackedByEntity.ServerPos);
            AlarmHerd();
            return true;
        }

        if (!CheckAndResetSearchCooldown()) return false;

        SearchForTarget();

        if (targetEntity == null) return false;

        AlarmHerd();

        targetPosition.SetWithDimension(targetEntity.ServerPos);
        if (entity.ServerPos.SquareDistanceTo(targetPosition) <= MinDistanceToTarget(Config.ExtraTargetDistance))
        {
            return false;
        }

        return true;
    }

    public override void StartExecute()
    {
        if (targetEntity == null) return;

        base.StartExecute();

        currentFollowTimeSec = 0;

        attackPattern = EnumAttackPattern.DirectAttack;

        // 1 in 20 times we do an expensive search
        int searchDepth = pathSearchDepth;
        if (world.Rand.NextDouble() < chanceOfDeepSearch)
        {
            searchDepth = pathDeepSearchDepth;
        }

        pathTraverser.NavigateTo_Async(targetEntity.Pos.XYZ, Config.MoveSpeed, MinDistanceToTarget(Config.ExtraTargetDistance), OnGoalReached, OnStuck, OnSeekUnable, searchDepth, 1, Config.AiCreatureType);

        previousPosition.SetWithDimension(entity.Pos);
    }

    public override bool ContinueExecute(float dt)
    {
        if (targetEntity == null) return false;

        if (!base.ContinueExecute(dt)) return false;

        currentFollowTimeSec += dt;
        lastPathUpdateSecondsSec += dt;

        if (Config.JumpAtTarget && !updatedPathAfterLanding && entity.OnGround && entity.Collided)
        {
            pathTraverser.NavigateTo(targetPosition, Config.MoveSpeed, MinDistanceToTarget(Config.ExtraTargetDistance), OnGoalReached, OnStuck, null, false, updatePathDepth, 1, Config.AiCreatureType);
            lastPathUpdateSecondsSec = 0;
            updatedPathAfterLanding = true;
        }
        if (!Config.JumpAtTarget || entity.OnGround || updatedPathAfterLanding)
        {
            UpdatePath();
        }
        if (Config.JumpAtTarget && !updatedPathAfterLanding && !entity.OnGround)
        {
            entity.ServerPos.Motion.X = jumpHorizontalVelocity.X;
            entity.ServerPos.Motion.Z = jumpHorizontalVelocity.Z;
        }

        RestoreMainAnimation();

        if (attackPattern == EnumAttackPattern.DirectAttack)
        {
            pathTraverser.CurrentTarget.X = targetEntity.ServerPos.X;
            pathTraverser.CurrentTarget.Y = targetEntity.ServerPos.InternalY;
            pathTraverser.CurrentTarget.Z = targetEntity.ServerPos.Z;
        }

        if (PerformJump())
        {
            updatedPathAfterLanding = false;
            pathTraverser.Stop();
        }

        double distance = GetDistanceToTarget();
        bool inCreativeMode = (targetEntity as EntityPlayer)?.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative && !Config.TargetPlayerInAllGameModes;
        float minDistance = MinDistanceToTarget(Config.ExtraTargetDistance);
        bool pathTraverserActive = pathTraverser.Active || !updatedPathAfterLanding;

        return
            targetEntity.Alive &&
            !inCreativeMode &&
            pathTraverserActive &&
            currentFollowTimeSec < Config.MaxFollowTimeSec &&
            distance < currentSeekingRange &&
            (distance > minDistance || targetEntity is EntityAgent entityAgent && entityAgent.ServerControls.TriesToMove);
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);

        finishedMs = entity.World.ElapsedMilliseconds;
        pathTraverser.Stop();
        active = false;
    }

    public override bool CanContinueExecute() => pathTraverser.Ready;

    public override bool Notify(string key, object data)
    {
        if (key == "seekEntity")
        {
            targetEntity = (Entity)data;
            targetPosition.SetWithDimension(targetEntity.ServerPos);
            return true;
        }

        return false;
    }

    public override void OnEntityHurt(DamageSource source, float damage)
    {
        base.OnEntityHurt(source, damage);

        if (!active || targetEntity != source.GetCauseEntity() || targetEntity == null) return;

        double distance = targetEntity.ServerPos.DistanceTo(entity.ServerPos);
        if (distance <= currentSeekingRange) return;

        if (Config.StopWhenAttackedByTargetOutsideOfSeekingRange)
        {
            stopTask = true;
        }

        if (Config.FleeWhenAttackedByTargetOutsideOfSeekingRange)
        {
            entity.GetBehavior<EntityBehaviorTaskAI>().TaskManager.AllTasks.ForEach(t => (t as AiTaskFleeEntity)?.InstaFleeFrom(targetEntity));
        }
    }

    protected override bool CanSense(Entity target, double range)
    {
        if (!base.CanSense(target, range)) return false;

        // Do not target entities which have a positive futility value, but slowly decrease that value so that they can eventually be retargeted
        if (futilityCounters != null && futilityCounters.TryGetValue(target.EntityId, out int futilityCounter) && futilityCounter > 0)
        {
            futilityCounter -= 2;
            futilityCounters[target.EntityId] = futilityCounter;
            return false;
        }

        return true;
    }

    protected override bool SearchForTarget()
    {
        bool fullyTamed = GetOwnGeneration() >= Config.TamingGenerations;
        posBuffer.SetWithDimension(entity.ServerPos);
        targetEntity = partitionUtil.GetNearestEntity(posBuffer, currentSeekingRange, potentialTarget =>
        {
            if (Config.IgnorePlayerIfFullyTamed && fullyTamed && (IsNonAttackingPlayer(potentialTarget) || entity.ToleratesDamageFrom(attackedByEntity))) return false;

            return IsTargetableEntity(potentialTarget, currentSeekingRange);
        }, Config.SearchType);

        return targetEntity != null;
    }

    protected virtual void OnSeekUnable()
    {
        if (targetPosition.DistanceTo(entity.ServerPos.XYZ) < currentSeekingRange && !TryCircleTarget())
        {
            OnCircleTargetUnable();
        }
    }

    protected virtual void OnCircleTargetUnable()
    {
        if (targetEntity == null) return;
        if (Config.FleeIfCantReach) taskAiBehavior.TaskManager.GetTasks<AiTaskFleeEntity>().Foreach(task => task.InstaFleeFrom(targetEntity));
    }

    /// <summary>
    /// I give up, let this be not configurable.
    /// </summary>
    /// <returns></returns>
    protected virtual bool TryCircleTarget()
    {
        attackPattern = EnumAttackPattern.CircleTarget;

        // If we cannot find a path to the target, let's circle it!
        float angle = (float)Math.Atan2(entity.ServerPos.X - targetPosition.X, entity.ServerPos.Z - targetPosition.Z);

        for (int count = 0; count < 3; count++)
        {
            // We need to avoid crossing the path of the target, so we do only small angle variation between us and the target 
            double randAngle = angle + 0.5 + world.Rand.NextDouble() / 2;

            double distance = 4 + world.Rand.NextDouble() * 6;

            double dx = GameMath.Sin(randAngle) * distance;
            double dz = GameMath.Cos(randAngle) * distance;
            targetPosition.Add(dx, 0, dz);

            int tries = 0;
            bool ok = false;
            BlockPos tmp = new((int)targetPosition.X, (int)targetPosition.Y, (int)targetPosition.Z);

            int dy = 0;
            while (tries < 5)
            {
                // Down ok?
                if (world.BlockAccessor.GetBlockBelow(tmp, dy).SideSolid[BlockFacing.UP.Index] && !world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, new Vec3d(tmp.X + 0.5, tmp.Y - dy + 1, tmp.Z + 0.5), false))
                {
                    ok = true;
                    targetPosition.Y -= dy;
                    targetPosition.Y++;
                    break;
                }

                // Up ok?
                if (world.BlockAccessor.GetBlockAbove(tmp, dy).SideSolid[BlockFacing.UP.Index] && !world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, new Vec3d(tmp.X + 0.5, tmp.Y + dy + 1, tmp.Z + 0.5), false))
                {
                    ok = true;
                    targetPosition.Y += dy;
                    targetPosition.Y++;
                    break;
                }

                tries++;
                dy++;
            }

            if (ok)
            {
                pathTraverser.NavigateTo_Async(targetPosition.Clone(), Config.MoveSpeed, MinDistanceToTarget(Config.ExtraTargetDistance), OnGoalReached, OnStuck, OnCircleTargetUnable, circlePathSearchDepth, 1, Config.AiCreatureType);
                return true;
            }
        }

        return false;
    }

    protected virtual double GetDistanceToTarget()
    {
        if (targetEntity == null) return double.MaxValue;
        Cuboidd targetBox = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.InternalY, targetEntity.ServerPos.Z);
        posBuffer.SetWithDimension(entity.ServerPos);
        posBuffer.Add(0, entity.SelectionBox.Y2 / 2, 0).Ahead(entity.SelectionBox.XSize / 2, 0, entity.ServerPos.Yaw);
        double distance = targetBox.ShortestDistanceFrom(posBuffer);
        return distance;
    }

    protected virtual void UpdatePath()
    {
        if (targetEntity == null) return;

        if (attackPattern == EnumAttackPattern.DirectAttack &&
            lastPathUpdateSecondsSec >= Config.PathUpdateCooldownSec &&
            targetPosition.SquareDistanceTo(targetEntity.ServerPos.X, targetEntity.ServerPos.InternalY, targetEntity.ServerPos.Z) >= Config.MinDistanceToUpdatePath * Config.MinDistanceToUpdatePath)
        {
            targetPosition.Set(targetEntity.ServerPos.X + targetEntity.ServerPos.Motion.X * Config.MotionAnticipationFactor, targetEntity.ServerPos.InternalY, targetEntity.ServerPos.Z + targetEntity.ServerPos.Motion.Z * Config.MotionAnticipationFactor);

            pathTraverser.NavigateTo(targetPosition, Config.MoveSpeed, MinDistanceToTarget(Config.ExtraTargetDistance), OnGoalReached, OnStuck, null, false, updatePathDepth, 1, Config.AiCreatureType);
            lastPathUpdateSecondsSec = 0;
        }
    }

    protected virtual void RestoreMainAnimation()
    {
        if (Config.AnimationMeta == null) return;

        if (Config.JumpAtTarget && !entity.AnimManager.IsAnimationActive(Config.AnimationMeta.Code) && entity.AnimManager.Animator.GetAnimationState(Config.JumpAnimationCode)?.Active != true)
        {
            Config.AnimationMeta.EaseInSpeed = 1f;
            Config.AnimationMeta.EaseOutSpeed = 1f;
            entity.AnimManager.StartAnimation(Config.AnimationMeta);
        }

        if (jumpAnimationOn && entity.World.ElapsedMilliseconds - finishedMs > Config.JumpAnimationTimeoutMs)
        {
            entity.AnimManager.StopAnimation(Config.JumpAnimationCode);
            Config.AnimationMeta.EaseInSpeed = 1f;
            Config.AnimationMeta.EaseOutSpeed = 1f;
            entity.AnimManager.StartAnimation(Config.AnimationMeta);
        }
    }

    protected virtual void PlayJumpAnimation()
    {
        if (Config.JumpAnimationCode != null)
        {
            foreach (string animation in Config.AnimationToStopForJump)
            {
                entity.AnimManager.StopAnimation(animation);
                entity.AnimManager.StopAnimation(animation);
            }

            entity.AnimManager.StartAnimation(new AnimationMetaData() { Animation = Config.JumpAnimationCode, Code = Config.JumpAnimationCode }.Init());
            jumpAnimationOn = true;
        }
    }

    protected virtual bool PerformJump()
    {
        if (targetEntity == null) return false;

        double distance = GetDistanceToTarget();
        bool inCreativeMode = (targetEntity as EntityPlayer)?.Player?.WorldData.CurrentGameMode == EnumGameMode.Creative && !Config.TargetPlayerInAllGameModes;

        if (inCreativeMode || !Config.JumpAtTarget || Rand.NextDouble() > Config.JumpChance) return false;

        bool recentlyJumped = entity.World.ElapsedMilliseconds - jumpedMs < Config.JumpCooldownMs;
        bool jumpedJustNow = false;

        if (distance >= Config.DistanceToTargetToJump[0] && distance <= Config.DistanceToTargetToJump[1] && !recentlyJumped && Config.MaxHeightDifferenceToJump >= entity.ServerPos.Y - targetEntity.ServerPos.Y)
        {
            Vec3d horizontalMotion = new(entity.ServerPos.Motion.X, 0, entity.ServerPos.Motion.Z);
            double horizontalSpeed = horizontalMotion.Length();

            double dx = (targetEntity.ServerPos.X - entity.ServerPos.X + targetEntity.ServerPos.Motion.X * Config.JumpMotionAnticipationFactor) * Config.JumpSpeedFactor * 0.033f * 0.5f;
            double dz = (targetEntity.ServerPos.Z - entity.ServerPos.Z + targetEntity.ServerPos.Motion.Z * Config.JumpMotionAnticipationFactor) * Config.JumpSpeedFactor * 0.033f * 0.5f;
            double dy = Config.JumpHeightFactor * GameMath.Max(Config.MaxVerticalJumpSpeed, (targetEntity.ServerPos.Y - entity.ServerPos.Y) * 0.033f);

            horizontalMotion.Set(dx, 0, dz).Normalize().Mul(horizontalSpeed);

            jumpHorizontalVelocity.Set(dx + entity.ServerPos.Motion.X, 0, dz + entity.ServerPos.Motion.Z);

            entity.ServerPos.Motion.X = horizontalMotion.X;
            entity.ServerPos.Motion.Z = horizontalMotion.Z;

            entity.ServerPos.Motion.Add(dx, dy, dz);

            float yaw = (float)Math.Atan2(dx, dz);
            entity.ServerPos.Yaw = yaw;

            PlayJumpAnimation();

            jumpedMs = entity.World.ElapsedMilliseconds;
            finishedMs = entity.World.ElapsedMilliseconds;
            jumpedJustNow = true;
        }

        if (recentlyJumped && !entity.Collided && distance < Config.DistanceToTargetToJump[0])
        {
            entity.ServerPos.Motion *= Config.AfterJumpSpeedReduction;
        }

        return jumpedJustNow;
    }

    protected virtual void AlarmHerd()
    {
        if (!Config.AlarmHerd || entity.HerdId == 0) return;

        posBuffer.SetWithDimension(entity.ServerPos);
        entity.World.GetNearestEntity(posBuffer, Config.HerdAlarmRange, Config.HerdAlarmRange, target =>
        {
            if (target.EntityId != entity.EntityId && target is EntityAgent agent && agent.Alive && agent.HerdId == entity.HerdId)
            {
                agent.Notify("seekEntity", targetEntity);
            }

            return false;
        });
        
    }

    protected virtual void OnStuck()
    {
        stopTask = true;
    }

    protected virtual void OnGoalReached()
    {
        if (targetEntity == null || attackPattern != EnumAttackPattern.DirectAttack) return;
        
        if (previousPosition.SquareDistanceTo(entity.Pos) < 0.001)   // Basically we haven't moved since last time, so we are stuck somehow: e.g. bears chasing small creatures into a hole or crevice
        {
            futilityCounters.TryGetValue(targetEntity.EntityId, out int futilityCounter);
            futilityCounter++;
            futilityCounters[targetEntity.EntityId] = futilityCounter;
            
            if (futilityCounter > 19) return;
        }

        previousPosition.SetWithDimension(entity.Pos);

        pathTraverser.Retarget();
    }

    protected virtual float SetSeekingRange()
    {
        currentSeekingRange = Config.SeekingRange;

        if (Config.BelowTemperatureThreshold > -99)
        {
            float temperature = entity.World.BlockAccessor.GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, entity.World.Calendar.TotalDays).Temperature;
            if (temperature <= Config.BelowTemperatureThreshold)
            {
                currentSeekingRange = Config.BelowTemperatureSeekingRange;
            }
        }

        if (Config.RetaliationSeekingRangeFactor != 1 &&
            Config.RetaliateAttacks &&
            attackedByEntity != null &&
            attackedByEntity.Alive &&
            attackedByEntity.IsInteractable &&
            CanSense(attackedByEntity, currentSeekingRange * Config.RetaliationSeekingRangeFactor) &&
            !entity.ToleratesDamageFrom(attackedByEntity))
        {
            currentSeekingRange *= Config.RetaliationSeekingRangeFactor;
        }

        currentSeekingRange *= GetFearReductionFactor();

        return currentSeekingRange;
    }
}
