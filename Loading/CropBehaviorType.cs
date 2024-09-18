using Newtonsoft.Json;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

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
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The ID of the crop behavior class to use.
        /// </summary>
        [JsonProperty]
        public string name;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Properties for the specific crop behavior class.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        public JsonObject properties;
    }
}