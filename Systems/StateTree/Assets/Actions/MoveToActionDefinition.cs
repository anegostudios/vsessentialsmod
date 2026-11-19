using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

public enum MoveToActionType
{
    /// <summary>
    /// Move to the position of the current target entity (if any).
    /// </summary>
    TargetEntityPosition,

    /// <summary>
    /// Move to the position of the current target block (if any).
    /// </summary>
    TargetBlockPosition
}

[AssetSchemaMetadata("Move to", "Moves the NPC to a target position.")]
public class MoveToActionDefinition : ActionDefinition
{
    [JsonProperty(Required = Required.Always)]
    [DefaultValue("TargetEntityPosition")]
    public MoveToActionType Type { get; set; }

    [JsonProperty]
    public Variable<float> MoveSpeed { get; set; }

    [JsonProperty]
    [AssetSchemaMetadata(null, "The entity will stop moving when it gets within this distance of the target position.")]
    [DefaultValue(1.0)]
    public float Threshold { get; set; }

    public override Action Build(Entity entity)
    {
        EntityBehaviorStateTree.TryGet(entity, out var stateTree);
        EntityBehaviorMemory.TryGet(entity, out var entityMemory);

        return Build(new MoveToAction(
            Weight,
            Type,
            MoveSpeed.GetValue(entity),
            Threshold,
            stateTree.StraightLineTraverser,
            entityMemory));
    }
}
