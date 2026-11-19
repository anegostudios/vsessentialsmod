using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

public enum TargetEntityActionMode
{
    /// <summary>
    /// Use the most recently recorded entity.
    /// </summary>
    MostRecent,

    /// <summary>
    /// Use a random entity record.
    /// </summary>
    Random
}

[AssetSchemaMetadata("Target entity", "Sets the NPC's target entity in its memory behavior.")]
public class TargetEntityActionDefinition : ActionDefinition
{
    /// <summary>
    /// The memory category to select a target from.
    /// </summary>
    [JsonProperty]
    [DefaultValue("Any")]
    public EntityBehaviorMemory.Category MemoryCategory { get; set; } = EntityBehaviorMemory.Category.Any;

    /// <summary>
    /// How the target should be selected.
    /// </summary>
    [JsonProperty]
    [DefaultValue("MostRecent")]
    public TargetEntityActionMode Mode { get; set; } = TargetEntityActionMode.MostRecent;

    [JsonProperty]
    [AssetSchemaMetadata(null, "How long to lock the target for. The target cannot be changed until this time has elapsed.")]
    [DefaultValue(2000)]
    public Variable<long> LockDurationMs { get; set; } = 2000;

    [JsonProperty]
    [AssetSchemaMetadata(null, "How long until the target should be cleared. Subsequent TargetAction calls with the same target will reset this timer.")]
    [DefaultValue(10000)]
    public Variable<long> ClearAfterMs { get; set; } = 10000;

    public override Action Build(Entity entity)
    {
        var lockDurationMs = LockDurationMs.GetValue(entity);
        var clearAfterMs = ClearAfterMs.GetValue(entity);

        if (clearAfterMs < lockDurationMs)
        {
            entity.Api?.Logger.Warning($"TargetActionDefinition: LockDurationMs of {lockDurationMs} is less than ClearAfterMs of {clearAfterMs}.");
        }

        return EntityBehaviorMemory.TryGet(entity, out EntityBehaviorMemory memory)
            ? Build(new TargetEntityAction(Weight, memory, MemoryCategory, Mode, lockDurationMs, clearAfterMs))
            : null;
    }
}
