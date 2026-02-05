using Newtonsoft.Json;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{
    /// <summary>
    /// Allows further complex behavior for crop blocks.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///"cropProps": {
	///	"behaviors": [
	///		{
	///			"name": "Pumpkin",
	///			"properties": {
	///				"vineGrowthStage": 3,
	///				"vineGrowthQuantity": {
	///					"dist": "invexp",
	///					"avg": 2,
	///					"var": 3
	///				}
	///			}
	///		}
	///	],
	///	...
	///},
    /// </code>
    /// </example>
    [DocumentAsJson]
    public class CropBehaviorType
    {
        /// <summary>
        /// The ID of the crop behavior class to use.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Required")]
        public string name;

        /// <summary>
        /// Properties for the specific crop behavior class.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        [DocumentAsJson("Optional", "None")]
        public JsonObject properties;
    }
}
