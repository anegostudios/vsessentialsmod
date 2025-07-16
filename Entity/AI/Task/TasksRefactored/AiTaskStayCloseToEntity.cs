using Newtonsoft.Json;
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
///
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2:<br/>
/// - executionChance default value: 0.01 => 1.0<br/>
/// - minSeekSeconds => minTimeBeforeGiveUpSec<br/>
/// - teleportToRange => MinTeleportDistanceToTarget<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskStayCloseToEntityConfig : AiTaskBaseTargetableConfig
{
    /// <summary>
    /// Entity moving speed.
    /// </summary>
    [JsonProperty] public float MoveSpeed = 0.03f;

    /// <summary>
    /// Max distance to target for entity to be able to teleport to it.
    /// </summary>
    [JsonProperty] public float TeleportMaxRange = float.MaxValue;

    /// <summary>
    /// Time until which entity will try to find path to target or teleport to it.
    /// </summary>
    [JsonProperty] public float MinTimeBeforeGiveUpSec = 3f;

    /// <summary>
    /// Extra distance to target for pathfinder to consider target reached. It is added to average between entity and target sizes.
    /// </summary>
    [JsonProperty] public float ExtraMinDistanceToTarget = 1f;

    /// <summary>
    /// Affects pathfinding, <see cref="EnumAICreatureType"/>. If not set, value from entity attributes will be used.
    /// </summary>
    [JsonProperty] public EnumAICreatureType? AiCreatureType = EnumAICreatureType.LandCreature;

    /// <summary>
    /// Entity will approach target position offset by distance from 0 to this value.
    /// </summary>
    [JsonProperty] public float RandomTargetOffset = 2f;

    /// <summary>
    /// Distance that target entity should move to trigger pathfinder to retarget, is needed to keep up with target entity.
    /// </summary>
    [JsonProperty] public float MinDistanceToRetarget = 3f;

    /// <summary>
    /// Allow for entity to teleport when stuck or too far away.
    /// </summary>
    [JsonProperty] public bool AllowTeleport = false;

    /// <summary>
    /// Range after which entity will try to teleport to target.
    /// </summary>
    [JsonProperty] public float TeleportAfterRange = 30;

    /// <summary>
    /// Delay between task start and entity being able to teleport.s
    /// </summary>
    [JsonProperty] public float TeleportDelaySec = 4f;

    /// <summary>
    /// Chance to teleport at each tick, if should teleport.
    /// </summary>
    [JsonProperty] public float TeleportChance = 0.05f;

    /// <summary>
    /// Min distance between target and teleport destination.
    /// </summary>
    [JsonProperty] public float MinTeleportDistanceToTarget = 2f;

    /// <summary>
    /// Max distance between target and teleport destination.
    /// </summary>
    [JsonProperty] public float MaxTeleportDistanceToTarget = 4.5f;

    /// <summary>
    /// Min distance to target for task to be executed. If not set, <see cref="ExtraMinDistanceToTarget"/> will be used instead. Is added to entities average size.
    /// </summary>
    [JsonProperty] public float MinRangeToTrigger = float.MinValue;



    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (MinRangeToTrigger <= float.MinValue)
        {
            MinRangeToTrigger = ExtraMinDistanceToTarget;
        }
    }
}

public class AiTaskStayCloseToEntityR : AiTaskBaseTargetableR
{
    public virtual float TeleportMaxRange => Config.TeleportMaxRange;
    public virtual int AllowTeleportCount { get; set; }

    private AiTaskStayCloseToEntityConfig Config => GetConfig<AiTaskStayCloseToEntityConfig>();

    protected bool stuck;
    protected FastVec3d targetOffset;
    protected FastVec3d initialTargetPos;
    protected float executingTimeSec;
    protected int stuckCounter;

    #region Variables to reduce heap allocations cause we dont use structs
    private readonly BlockPos blockPosBuffer = new(0);
    #endregion

    public AiTaskStayCloseToEntityR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskStayCloseToEntityConfig>(entity, taskConfig, aiConfig);
    }

    public override bool ShouldExecute()
    {
        if (stuckCounter > 3)
        {
            stuckCounter = 0;
            cooldownUntilMs = entity.World.ElapsedMilliseconds + 60 * 1000;
        }

        if (!PreconditionsSatisficed()) return false;

        if (!SearchForTarget()) return false;

        if (targetEntity != null)
        {
            float distanceSquared = entity.ServerPos.SquareDistanceTo(targetEntity.ServerPos);
            float minDistance = MinDistanceToTarget(Config.MinRangeToTrigger);
            if (distanceSquared <= minDistance * minDistance) return false;
        }

        return true;
    }

    public override void StartExecute()
    {
        if (targetEntity == null) return;

        base.StartExecute();

        executingTimeSec = 0;
        initialTargetPos.Set(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);

        pathTraverser.NavigateTo_Async(targetEntity.ServerPos.XYZ, Config.MoveSpeed, MinDistanceToTarget(Config.ExtraMinDistanceToTarget), OnGoalReached, OnStuck, OnNoPath, 1000, 1, Config.AiCreatureType);
        targetOffset.Set(entity.World.Rand.NextDouble() * Config.RandomTargetOffset - Config.RandomTargetOffset / 2f, 0, entity.World.Rand.NextDouble() * Config.RandomTargetOffset - Config.RandomTargetOffset / 2f);
        stuck = false;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;

        if (targetEntity == null) return false;

        if (initialTargetPos.Distance(targetEntity.ServerPos.XYZ) > Config.MinDistanceToRetarget)
        {
            initialTargetPos.Set(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            pathTraverser.Retarget();
        }

        double x = targetEntity.ServerPos.X + targetOffset.X;
        double y = targetEntity.ServerPos.Y;
        double z = targetEntity.ServerPos.Z + targetOffset.Z;

        pathTraverser.CurrentTarget.X = x;
        pathTraverser.CurrentTarget.Y = y;
        pathTraverser.CurrentTarget.Z = z;

        float distance = entity.ServerPos.SquareDistanceTo(x, y, z);

        float minDistance = MinDistanceToTarget();

        if (distance < minDistance * minDistance)
        {
            pathTraverser.Stop();
            return false;
        }

        if ((Config.AllowTeleport || AllowTeleportCount > 0) &&
            executingTimeSec > Config.TeleportDelaySec &&
            (distance > Config.TeleportAfterRange * Config.TeleportAfterRange || stuck) &&
            distance < Config.TeleportMaxRange * Config.TeleportMaxRange &&
            Rand.NextDouble() < Config.TeleportChance)
        {
            TryTeleport();
        }

        executingTimeSec += dt;

        return (!stuck && pathTraverser.Active) || executingTimeSec < Config.MinTimeBeforeGiveUpSec;
    }

    public override void FinishExecute(bool cancelled)
    {
        if (stuck)
        {
            stuckCounter++;
        }
        else
        {
            stuckCounter = 0;
        }

        base.FinishExecute(cancelled);
    }

    public override bool CanContinueExecute()
    {
        return pathTraverser.Ready;
    }

    protected virtual bool FindDecentTeleportPos(out FastVec3d teleportPosition)
    {
        teleportPosition = new();

        if (targetEntity == null) return false;

        IBlockAccessor blockAccessor = entity.World.BlockAccessor;

        for (int teleportRangeStep = (int)Config.MinTeleportDistanceToTarget * 5; teleportRangeStep < (int)(Config.MaxTeleportDistanceToTarget + 1) * 5; teleportRangeStep++)
        {
            float range = GameMath.Clamp(teleportRangeStep / 5f, Config.MinTeleportDistanceToTarget, Config.MaxTeleportDistanceToTarget);

            double xOffset = Rand.NextDouble() * 2 * range - range;
            double zOffset = Rand.NextDouble() * 2 * range - range;

            for (int yOffsetSeed = 0; yOffsetSeed < 8; yOffsetSeed++)
            {
                // Produces: 0, -1, 1, -2, 2, -3, 3
                int yOffset = (1 - (yOffsetSeed % 2) * 2) * (int)Math.Ceiling(yOffsetSeed / 2f);

                teleportPosition.Set(targetEntity.ServerPos.X + xOffset, targetEntity.ServerPos.Y + yOffset, targetEntity.ServerPos.Z + zOffset);

                blockPosBuffer.Set((int)teleportPosition.X, (int)teleportPosition.Y, (int)teleportPosition.Z);
                Block aboveBlock = blockAccessor.GetBlock(blockPosBuffer);
                Cuboidf[] boxes = aboveBlock.GetCollisionBoxes(blockAccessor, blockPosBuffer);
                if (boxes != null && boxes.Length > 0) continue;
                if (aboveBlock.Attributes?["insideDamage"].AsInt(0) > 0) continue;

                blockPosBuffer.Set((int)teleportPosition.X, (int)teleportPosition.Y - 1, (int)teleportPosition.Z);
                Block belowBlock = blockAccessor.GetBlock(blockPosBuffer);
                boxes = belowBlock.GetCollisionBoxes(blockAccessor, blockPosBuffer);
                if (boxes == null || boxes.Length == 0) continue;
                if (belowBlock.Attributes?["insideDamage"].AsInt(0) > 0) continue;

                teleportPosition.Y = (int)teleportPosition.Y - 1 + boxes.Max(cuboid => cuboid.Y2);

                return true;
            }
        }

        return false;
    }

    protected virtual void TryTeleport()
    {
        if ((!Config.AllowTeleport && AllowTeleportCount <= 0) || targetEntity == null) return;
        bool found = FindDecentTeleportPos(out FastVec3d pos);
        if (found)
        {
            entity.TeleportToDouble(pos.X, pos.Y, pos.Z, () =>
            {
                initialTargetPos.Set(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
                pathTraverser.Retarget();
                AllowTeleportCount = Math.Max(0, AllowTeleportCount - 1);
            });
        }
    }

    protected virtual void OnStuck()
    {
        stuck = true;
    }

    protected virtual void OnNoPath()
    {

    }

    protected virtual void OnGoalReached()
    {
        stopTask = true;
    }
}
