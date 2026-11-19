using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent;

[JsonConverter(typeof(AssetConverter<IAbilityAsset>))]
public interface IAbilityAsset : IBuildableAsset<Ability>;
