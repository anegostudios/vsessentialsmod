using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[PolymorphicAssetElement("Sense")]
[JsonConverter(typeof(AssetElementJsonConverter<IStateTreeAsset, SenseDefinition>))]
public abstract class SenseDefinition
{
    [JsonProperty]
    [DefaultValue(true)]
    public bool ExcludeInanimateEntities { get; set; } = true;

    [JsonProperty]
    public bool EnableDebugDraw { get; set; }

    protected DebugDrawSystem? GetDebugDrawSystem(Entity entity)
    {
        return EnableDebugDraw ? entity.World.Api.ModLoader.GetModSystem<DebugDrawSystem>() : null;
    }

    public abstract Sense Build(Entity entity, EntityBehaviorStates states);
}
