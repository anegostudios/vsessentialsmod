using Newtonsoft.Json;
using Vintagestory.API;
using Vintagestory.API.Common;

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