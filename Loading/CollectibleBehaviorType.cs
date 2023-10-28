using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.ServerMods.NoObf
{
    public class CollectibleBehaviorType
    {
        [JsonProperty]
        public string name;

        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        public JsonObject properties;
    }

}