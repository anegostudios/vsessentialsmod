using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Rotate", "Rotates the NPC at a constant speed. Probably not useful outside of debugging/testing.")]
public class RotateActionDefinition : ActionDefinition
{
    [JsonProperty]
    [DefaultValue(1.0f)]
    public Variable<float> Speed { get; set; } = 1.0f;

    public override Action Build(Entity entity)
    {
        return Build(new RotateAction(Weight, entity, Speed.GetValue(entity)));
    }
}
