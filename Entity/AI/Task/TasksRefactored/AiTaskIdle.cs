using Newtonsoft.Json;
using System;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

/// <summary>
/// Entity stays in this task until specified duration, or is able to target an entity, or day time changes.<br/><br/>
/// 
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2<br/>
/// - chance => executionChance<br/>
/// - set 'WhenFeetInLiquid' to 'false' for land creatures to preserver previous behavior<br/>
/// - set 'stopOnHurt' to 'true' to preserver previous behavior<br/>
/// - dont forget to set 'StopWhenTargetDetected' to 'true' where needed<br/>
/// - onBlockBelowCode => allowedBlockBelowCode<br/>
/// - MinDuration => MinDurationMs<br/>
/// - MaxDuration => MaxDurationMs<br/>
/// - MinDuration default value: 2000 => 0<br/>
/// - MaxDuration default value: 4000 => 0<br/>
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskIdleConfig : AiTaskBaseTargetableConfig
{
    /// <summary>
    /// Will try to search for target on start and while executing, if target is found the task is stopped.<br/>
    /// Will perform search each <see cref="TargetSearchCooldownMs"/> for performance reasons because this search is expensive.
    /// </summary>
    [JsonProperty] public bool StopWhenTargetDetected = false;

    /// <summary>
    /// Entity should be above or inside block with these tags to start this task. If not specified this check will be ignored.
    /// </summary>
    [JsonProperty] private string[]? allowedBlockBelowTags = [];

    /// <summary>
    /// Entity should be above or inside block without these tags to start this task. If not specified this check will be ignored.
    /// </summary>
    [JsonProperty] private string[]? skipBlockBelowTags = [];

    /// <summary>
    /// Entity should be above or inside block with code equal to this value exactly or matching this as wildcard. If not specified this check will be ignored.
    /// </summary>
    [JsonProperty] public AssetLocation? AllowedBlockBelowCode = null;

    /// <summary>
    /// If set to true, block below entity should have its upper side solid.
    /// </summary>
    [JsonProperty] public bool CheckForSolidUpSide = true;

    /// <summary>
    /// If entity is inside block with Replaceable bigger than this value, block below will be checked, else block that entity is inside will be checked instead.
    /// </summary>
    [JsonProperty] public int MinBlockInsideReplaceable = 6000;

    /// <summary>
    /// Chance for each tick to check for entities around and day time frames. If one of these checks fails, task will be stopped.<br/>
    /// This spreads out these expensive entity search check and adds randomness to entity reactions on day time changes and entities appearing in sight.
    /// </summary>
    [JsonProperty] public float ChanceToCheckTarget = 0.3f;



    public BlockTagRule AllowedBlockBelowTags;

    public BlockTagRule SkipBlockBelowTags;
    
    public bool IgnoreBlockCodeAndTags => AllowedBlockBelowTags == BlockTagRule.Empty && SkipBlockBelowTags == BlockTagRule.Empty && AllowedBlockBelowCode == null;


    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (allowedBlockBelowTags != null)
        {
            AllowedBlockBelowTags = new BlockTagRule(entity.Api, allowedBlockBelowTags);
            allowedBlockBelowTags = null;
        }
        if (skipBlockBelowTags != null)
        {
            SkipBlockBelowTags = new BlockTagRule(entity.Api, skipBlockBelowTags);
            skipBlockBelowTags = null;
        }
    }
}

public class AiTaskIdleR : AiTaskBaseTargetableR
{
    private AiTaskIdleConfig Config => GetConfig<AiTaskIdleConfig>();

    public AiTaskIdleR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskIdleConfig>(entity, taskConfig, aiConfig);
    }

    public override bool ShouldExecute()
    {
        if (!PreconditionsSatisficed()) return false;
        if (CheckForTargetToStop()) return false;
        if (!CheckForBlockBelow()) return false;

        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();

        entity.IdleSoundChanceModifier = 0f;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;

        if (Rand.NextDouble() <= Config.ChanceToCheckTarget && CheckForTargetToStop())
        {
            return false;
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);

        entity.IdleSoundChanceModifier = 1f;
    }

    protected virtual bool CheckForTargetToStop()
    {
        // The entityInRange test is expensive. So we only test for it every 4 seconds
        // which should have zero impact on the behavior. It'll merely execute this task 4 seconds later

        if (!Config.StopWhenTargetDetected) return false;

        if (!CheckAndResetSearchCooldown()) return false;

        return SearchForTarget();
    }

    protected virtual bool CheckForBlockBelow()
    {
        Block belowBlock = entity.World.BlockAccessor.GetBlockRaw((int)entity.ServerPos.X, (int)entity.ServerPos.InternalY - 1, (int)entity.ServerPos.Z, BlockLayersAccess.Solid);

        if (Config.CheckForSolidUpSide && !belowBlock.SideSolid[API.MathTools.BlockFacing.UP.Index]) return false; // Only with a solid block below (and here not lake ice: entities should not idle on lake ice!)

        if (Config.IgnoreBlockCodeAndTags) return true;

        Block block = entity.World.BlockAccessor.GetBlockRaw((int)entity.ServerPos.X, (int)entity.ServerPos.InternalY, (int)entity.ServerPos.Z);

        if (block.Replaceable >= Config.MinBlockInsideReplaceable)
        {
            return CheckForBlock(belowBlock);
        }
        else
        {
            return CheckForBlock(block);
        }
    }

    protected virtual bool CheckForBlock(Block block)
    {
        if (Config.IgnoreBlockCodeAndTags) return true;
        if (Config.AllowedBlockBelowTags != BlockTagRule.Empty && !Config.AllowedBlockBelowTags.Intersects(block.Tags)) return false;
        if (Config.SkipBlockBelowTags != BlockTagRule.Empty && Config.SkipBlockBelowTags.Intersects(block.Tags)) return false;
        if (Config.AllowedBlockBelowCode != null && !block.WildCardMatch(Config.AllowedBlockBelowCode)) return false;

        return true;
    }
}
