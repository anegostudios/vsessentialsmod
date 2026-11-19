using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[PolymorphicAssetElement("Condition")]
[JsonConverter(typeof(AssetElementJsonConverter<IStateTreeAsset, IConditionDefinition>))]
public interface IConditionDefinition
{
    public ICondition Build(Entity entity);
}
