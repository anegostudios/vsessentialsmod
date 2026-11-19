using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace Vintagestory.StateTree;

[JsonConverter(typeof(AssetConverter<IStateTreeAsset>))]
public interface IStateTreeAsset : IBuildableAsset<StateTree>;
