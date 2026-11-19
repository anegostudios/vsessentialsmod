using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.StateTree;

[PolymorphicAssetElement("Action")]
[JsonConverter(typeof(AssetElementJsonConverter<IStateTreeAsset, ActionDefinition>))]
public abstract class ActionDefinition
{
    [JsonProperty]
    [AssetSchemaMetadata(null, "Used when the action list mode is set to 'Random'. A higher value increases the chance of this action being picked.")]
    [DefaultValue(1.0)]
    public float Weight { get; set; } = 1.0f;

    [JsonProperty]
    [AssetSchemaMetadata(null, "If true, the action will only run once until the node is deselected. This may be ignored by some action types.")]
    [DefaultValue(false)]
    public bool RunOnce { get; set; }

    public abstract Action Build(Entity entity);

    protected Action Build(Action action) => RunOnce ? new OnceAction(Weight, action) : action;
}
