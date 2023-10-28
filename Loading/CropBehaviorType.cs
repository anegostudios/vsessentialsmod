using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.ServerMods.NoObf
{
    public class CropBehaviorType
    {
        [JsonProperty]
        public string name;

        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        public JsonObject properties;
    }
}