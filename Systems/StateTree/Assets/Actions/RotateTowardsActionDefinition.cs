using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

public enum RotateTowardsActionType
{
    /// <summary>
    /// Rotate towards the position of the current target entity (if any).
    /// </summary>
    TargetEntityPosition,

    /// <summary>
    /// Rotate towards the position of the current target block (if any).
    /// </summary>
    TargetBlockPosition
}

[AssetSchemaMetadata("Rotate towards", "Rotates the NPC towards a target position.")]
public class RotateTowardsActionDefinition : ActionDefinition
{
    [JsonProperty(Required = Required.Always)]
    [DefaultValue("TargetEntityPosition")]
    public RotateTowardsActionType Type { get; set; }

    [JsonProperty]
    [DefaultValue(1.0)]
    public Variable<float> RotateSpeed { get; set; }

    [JsonProperty]
    [DefaultValue(0.1)]
    [AssetSchemaMetadata(null, "The entity will stop rotating once its absolute angle to the target falls within this threshold.")]
    public Variable<float> Threshold { get; set; }

    public override Action Build(Entity entity)
    {
        EntityBehaviorMemory.TryGet(entity, out var entityMemory);

        return Build(new RotateTowardsAction(
            Weight,
            Type,
            RotateSpeed.GetValue(entity),
            Threshold.GetValue(entity),
            entity,
            entityMemory));
    }
}
