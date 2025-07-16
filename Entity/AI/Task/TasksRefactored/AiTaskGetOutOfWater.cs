using Newtonsoft.Json;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

/// <summary>
/// Entity will try to get out of water. Executed only if entity is currently swimming.<br/>
/// Most of the settings is meant to fine tune task for specific entity. The default values should work in most cases.<br/><br/>
/// 
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2<br/>
/// - ExecutionChance default value: 0.04 => 1.00 - set this value in config to preserve behavior, now this task uses standard parameter and mechanism instead of hardcoded value.<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskGetOutOfWaterConfig : AiTaskBaseConfig
{
    /// <summary>
    /// Entity moving speed while fleeing.
    /// </summary>
    [JsonProperty] public float MoveSpeed = 0.06f;

    /// <summary>
    /// Starting range in which task will try to find land to move to.
    /// </summary>
    [JsonProperty] public int MinimumRangeToSeekLand = 50;

    /// <summary>
    /// How fast search for land range increases with each failed attempt.<br/>
    /// After land is successfully found attempts counter resets.
    /// </summary>
    [JsonProperty] public float RangeSearchAttemptsFactor = 2;

    /// <summary>
    /// Chance at each tick for task to be stopped.
    /// </summary>
    [JsonProperty] public float ChanceToStopTask = 0.1f;
}

public class AiTaskGetOutOfWaterR : AiTaskBaseR
{
    private AiTaskGetOutOfWaterConfig Config => GetConfig<AiTaskGetOutOfWaterConfig>();

    protected Vec3d target = new();
    protected bool done;
    protected int searchAttempts = 0;

    protected const float minimumRangeOffset = 0.6f;
    protected const int triesPerAttempt = 10;

    public AiTaskGetOutOfWaterR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskGetOutOfWaterConfig>(entity, taskConfig, aiConfig);

        Config.WhenSwimming = true; // Executed only if entity is currently swimming.
    }

    #region Variables to reduce heap allocations in ShouldExecute method cause we dont use structs
    private readonly Vec3d tmpPos = new();
    private readonly BlockPos pos = new(0);
    #endregion

    public override bool ShouldExecute()
    {
        if (!PreconditionsSatisficed()) return false;

        int range = GameMath.Min(Config.MinimumRangeToSeekLand, (int)(Config.MinimumRangeToSeekLand * minimumRangeOffset + searchAttempts * Config.RangeSearchAttemptsFactor));

        target.Y = entity.ServerPos.Y;
        int tries = triesPerAttempt;
        int posX = (int)entity.ServerPos.X;
        int posZ = (int)entity.ServerPos.Z;
        IBlockAccessor blockAccessor = entity.World.BlockAccessor;
        while (tries-- > 0)
        {
            pos.X = posX + Rand.Next(range + 1) - range / 2;
            pos.Z = posZ + Rand.Next(range + 1) - range / 2;
            pos.Y = blockAccessor.GetTerrainMapheightAt(pos) + 1;

            Block block = blockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            if (block.IsLiquid()) continue;

            if (!entity.World.CollisionTester.IsColliding(blockAccessor, entity.CollisionBox, tmpPos.Set(pos.X + 0.5, pos.Y + 0.1f, pos.Z + 0.5)) &&
                entity.World.CollisionTester.IsColliding(blockAccessor, entity.CollisionBox, tmpPos.Set(pos.X + 0.5, pos.Y - 0.1f, pos.Z + 0.5)))
            {
                target.Set(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
                return true;
            }
        }

        searchAttempts++;
        return false;
    }

    public override void StartExecute()
    {
        base.StartExecute();

        searchAttempts = 0;
        done = false;
        pathTraverser.WalkTowards(target, Config.MoveSpeed, 0.5f, OnGoalReached, OnStuck);

    }

    public override bool ContinueExecute(float dt)
    {
        if (Rand.NextDouble() < Config.ChanceToStopTask && !entity.FeetInLiquid)
        {
            return false;
        }

        return !done;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);

        pathTraverser.Stop();
    }

    protected virtual void OnStuck()
    {
        done = true;
    }

    protected virtual void OnGoalReached()
    {
        done = true;
    }
}
