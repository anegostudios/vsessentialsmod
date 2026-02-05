using Newtonsoft.Json;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{
    /// <summary>
    /// A type of behavior to attach to a <see cref="CollectibleType"/> object.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///"behaviors": [ { "name": "NWOrientable" } ],
    /// </code>
    /// <code language="json">
    ///"behaviors": [
    ///	{
    ///		"name": "UnstableFalling",
    ///		"properties": {
    ///			"fallSound": null,
    ///			"dustIntensity": 0
    ///		}
    ///	}
    ///],
    /// </code>
    /// </example>
    [DocumentAsJson]
    public class CollectibleBehaviorType
    {
        /// <summary>
        /// The code of the collectible behavior to add.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Required")]
        public string name;

        /// <summary>
        /// A list of properties for the specific behavior.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        [DocumentAsJson("Optional", "None")]
        public JsonObject properties;
    }

}
