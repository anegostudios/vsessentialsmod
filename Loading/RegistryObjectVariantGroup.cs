using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.ServerMods
{
    /// <summary>
    /// This is used to define a set of variant groups for specific registry objects.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///"variantgroups": [
    ///	{
    ///		"code": "type",
    ///		"states": [ "spelt", "rye", "flax", "rice", "cassava", "amaranth", "sunflower" ]
    ///	},
    ///	{
    ///		"code": "state",
    ///		"states": [ "partbaked", "perfect", "charred" ]
    ///	}
    ///],
    ///</code>
    ///<code language="json">
    ///"variantgroups": [
    ///	{
    ///		"code": "rock",
    ///		"loadFromProperties": "block/rockwithdeposit"
    ///	}
    ///],
    ///</code>
    ///</example>
    [DocumentAsJson]
    public class RegistryObjectVariantGroup
    {
        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// If set, copies a WorldProperties asset to create variants from.
        /// </summary>
        [DocumentAsJson] public AssetLocation LoadFromProperties;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A set of world properties to combine to create variants from.
        /// </summary>
        [DocumentAsJson] public AssetLocation[] LoadFromPropertiesCombine;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// A unique code for this variant. Essentially an ID for each variant type. 
        /// </summary>
        [DocumentAsJson] public string Code;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional><jsondefault>None</jsondefault>-->
        /// A list of all the valid states for this variant. Only required if <see cref="LoadFromProperties"/> or <see cref="LoadFromPropertiesCombine"/> are not set.
        /// </summary>
        [DocumentAsJson] public string[] States;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>Multiply</jsondefault>-->
        /// How this variant combines with other variant types to create individual objects.
        /// </summary>
        [DocumentAsJson] public EnumCombination Combine = EnumCombination.Multiply;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Required if using the <see cref="EnumCombination.SelectiveMultiply"/> in <see cref="Combine"/>.
        /// </summary>
        [DocumentAsJson] public string OnVariant;

        public string IsValue;
    }
}
