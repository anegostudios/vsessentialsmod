using Newtonsoft.Json;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Makes entity turn their head towards target.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskLookAtEntityConfig : AiTaskBaseTargetableConfig
{
    /// <summary>
    /// Maximum angle in degrees for head to turn relative to <see cref="SpawnAngleDeg"/>.
    /// </summary>
    [JsonProperty] public float MaxTurnAngleDeg = 360;

    /// <summary>
    /// Entity head angle when spawned. Can be used to offset starting position that is used to restrict head movement via <see cref="MaxTurnAngleDeg"/>.
    /// </summary>
    [JsonProperty] public float SpawnAngleDeg = 0;

    /// <summary>
    /// Turning speed is taken from entities attributes 'pathfinder: { minTurnAnglePerSec: 250, maxTurnAnglePerSec: 450 }', if they are missing, these default values are used.
    /// </summary>
    [JsonProperty] public float DefaultMinTurnAngleDegPerSec = 250;

    /// <summary>
    /// Turning speed is taken from entities attributes 'pathfinder: { minTurnAnglePerSec: 250, maxTurnAnglePerSec: 450 }', if they are missing, these default values are used.
    /// </summary>
    [JsonProperty] public float DefaultMaxTurnAngleDegPerSec = 450;



    public float MaxTurnAngleRad => MaxTurnAngleDeg * GameMath.DEG2RAD;

    public float SpawnAngleRad => SpawnAngleDeg * GameMath.DEG2RAD;
}

public class AiTaskLookAtEntityR : AiTaskBaseTargetableR
{
    private AiTaskLookAtEntityConfig Config => GetConfig<AiTaskLookAtEntityConfig>();

    protected float minTurnAnglePerSec;
    protected float maxTurnAnglePerSec;
    protected float currentTurnRadPerSec;

    protected const float yawChangeRateToStop = 0.01f;

    public AiTaskLookAtEntityR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskLookAtEntityConfig>(entity, taskConfig, aiConfig);
    }

    public override bool ShouldExecute()
    {
        if (!PreconditionsSatisficed()) return false;

        return SearchForTarget();
    }

    public override void StartExecute()
    {
        base.StartExecute();

        ITreeAttribute? pathfinder = entity?.Properties.Server?.Attributes?.GetTreeAttribute("pathfinder");
        if (pathfinder != null)
        {
            minTurnAnglePerSec = pathfinder.GetFloat("minTurnAnglePerSec", Config.DefaultMinTurnAngleDegPerSec);
            maxTurnAnglePerSec = pathfinder.GetFloat("maxTurnAnglePerSec", Config.DefaultMaxTurnAngleDegPerSec);
        }
        else
        {
            minTurnAnglePerSec = Config.DefaultMinTurnAngleDegPerSec;
            maxTurnAnglePerSec = Config.DefaultMaxTurnAngleDegPerSec;
        }

        currentTurnRadPerSec = minTurnAnglePerSec + (float)Rand.NextDouble() * (maxTurnAnglePerSec - minTurnAnglePerSec);
        currentTurnRadPerSec *= GameMath.DEG2RAD;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;

        if (targetEntity == null) return false;

        FastVec3f targetVec = new();

        targetVec.Set(
            (float)(targetEntity.ServerPos.X - entity.ServerPos.X),
            (float)(targetEntity.ServerPos.Y - entity.ServerPos.Y),
            (float)(targetEntity.ServerPos.Z - entity.ServerPos.Z)
        );

        float desiredYaw = (float)Math.Atan2(targetVec.X, targetVec.Z);

        if (Config.MaxTurnAngleRad < GameMath.PI)
        {
            desiredYaw = GameMath.Clamp(desiredYaw, Config.SpawnAngleRad - Config.MaxTurnAngleRad, Config.SpawnAngleRad + Config.MaxTurnAngleRad);
        }

        float yawDistance = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
        entity.ServerPos.Yaw += GameMath.Clamp(yawDistance, -currentTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier, currentTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier);
        entity.ServerPos.Yaw %= GameMath.TWOPI;

        return Math.Abs(yawDistance) > yawChangeRateToStop;
    }
}

/// <summary>
/// Hardcoded AI task used only by 'EntityBehaviorConversable' and can only be created from code.
/// </summary>
public sealed class AiTaskLookAtEntityConversable : AiTaskBaseR
{
    private float minTurnAnglePerSec;
    private float maxTurnAnglePerSec;
    private float curTurnRadPerSec;
    private readonly Entity target;

    public AiTaskLookAtEntityConversable(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        world.Logger.Error($"This AI task 'AiTaskLookAtEntityConversable' can only be created from code.");
        throw new InvalidOperationException($"This AI task can only be created from code.");
    }

    public AiTaskLookAtEntityConversable(EntityAgent entity, Entity target) : base(entity)
    {
        this.target = target;
    }

    public override bool ShouldExecute() => false;

    public override void StartExecute()
    {
        if (entity.Properties.Server?.Attributes != null)
        {
            minTurnAnglePerSec = entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetFloat("minTurnAnglePerSec", 250);
            maxTurnAnglePerSec = entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetFloat("maxTurnAnglePerSec", 450);
        }
        else
        {
            minTurnAnglePerSec = 250;
            maxTurnAnglePerSec = 450;
        }

        curTurnRadPerSec = minTurnAnglePerSec + (float)Rand.NextDouble() * (maxTurnAnglePerSec - minTurnAnglePerSec);
        curTurnRadPerSec *= GameMath.DEG2RAD * 50 * 0.02f;
    }

    public override bool ContinueExecute(float dt)
    {
        FastVec3f targetVec = new();

        targetVec.Set(
            (float)(target.ServerPos.X - entity.ServerPos.X),
            (float)(target.ServerPos.Y - entity.ServerPos.Y),
            (float)(target.ServerPos.Z - entity.ServerPos.Z)
        );

        float desiredYaw = (float)Math.Atan2(targetVec.X, targetVec.Z);

        float yawDistance = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
        entity.ServerPos.Yaw += GameMath.Clamp(yawDistance, -curTurnRadPerSec * dt, curTurnRadPerSec * dt);
        entity.ServerPos.Yaw %= GameMath.TWOPI;

        return Math.Abs(yawDistance) > 0.01;
    }
}
