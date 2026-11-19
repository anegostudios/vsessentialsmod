using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

public enum TargetBlockActionMode
{
    /// <summary>
    /// Target a block by code.
    /// </summary>
    ByCode,

    /// <summary>
    /// Target a liquid block.
    /// </summary>
    Liquid,

    /// <summary>
    /// Target ground (i.e. a solid block with air above it).
    /// </summary>
    Ground
}

[AssetSchemaMetadata("Target block", "Sets the NPC's target block in its memory behavior.")]
public class TargetBlockActionDefinition : ActionDefinition
{
    [JsonProperty]
    [DefaultValue("ByCode")]
    public TargetBlockActionMode Mode { get; set; } = TargetBlockActionMode.ByCode;

    [JsonProperty]
    [DefaultValue("")]
    public string BlockCode { get; set; } = "";

    [JsonProperty]
    [DefaultValue(4)]
    public Variable<int> HorizontalSearchRadius { get; set; } = 4;

    [JsonProperty]
    [DefaultValue(1)]
    public Variable<int> VerticalSearchRadius { get; set; } = 1;

    public override Action Build(Entity entity)
    {
        var horizontalRadius = HorizontalSearchRadius.GetValue(entity);
        var verticalRadius = VerticalSearchRadius.GetValue(entity);

        if (EntityBehaviorMemory.TryGet(entity, out EntityBehaviorMemory memory))
        {
            var from = new BlockPos(-horizontalRadius, -verticalRadius, -horizontalRadius);
            var to = new BlockPos(horizontalRadius, verticalRadius, horizontalRadius);
            return Build(new TargetBlockAction(Weight, entity, memory, Mode, BlockCode, from, to));
        }

        return null;
    }
}
