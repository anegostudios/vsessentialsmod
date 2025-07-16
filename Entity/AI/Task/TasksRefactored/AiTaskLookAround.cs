using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Turns entity at random angle, then waits for specified duration.
/// 
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2:<br/>
/// - turnSpeedMul => turnAngleFactor
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskLookAroundConfig : AiTaskBaseConfig
{
    /// <summary>
    /// Entity turn speed multiplier.
    /// </summary>
    [JsonProperty] public float TurnAngleFactor = 0.75f;
}

public class AiTaskLookAroundR : AiTaskBaseR
{
    private AiTaskLookAroundConfig Config => GetConfig<AiTaskLookAroundConfig>();

    public AiTaskLookAroundR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskLookAroundConfig>(entity, taskConfig, aiConfig);
    }

    public override bool ShouldExecute() => PreconditionsSatisficed();

    public override void StartExecute()
    {
        base.StartExecute();

        entity.ServerPos.Yaw = (float)GameMath.Clamp(
            entity.World.Rand.NextDouble() * GameMath.TWOPI,
            entity.ServerPos.Yaw - GameMath.PI / 4 * GlobalConstants.OverallSpeedMultiplier * Config.TurnAngleFactor,
            entity.ServerPos.Yaw + GameMath.PI / 4 * GlobalConstants.OverallSpeedMultiplier * Config.TurnAngleFactor
        );
    }
}
