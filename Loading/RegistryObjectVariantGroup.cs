using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable disable

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
        /// If set, copies a WorldProperties asset to create variants from.
        /// </summary>
        [DocumentAsJson("Optional", "None")]
        public AssetLocation LoadFromProperties;

        /// <summary>
        /// A set of world properties to combine to create variants from.
        /// </summary>
        [DocumentAsJson("Optional", "None")]
        public AssetLocation[] LoadFromPropertiesCombine;

        /// <summary>
        /// A unique code for this variant. Essentially an ID for each variant type. 
        /// </summary>
        [DocumentAsJson("Required")]
        public string Code;

        /// <summary>
        /// A list of all the valid states for this variant. Only required if <see cref="LoadFromProperties"/> or <see cref="LoadFromPropertiesCombine"/> are not set.
        /// </summary>
        [DocumentAsJson("Required", "None")]
        public string[] States;

        /// <summary>
        /// How this variant combines with other variant types to create individual objects.
        /// </summary>
        [DocumentAsJson("Optional", "Multiply")]
        public EnumCombination Combine = EnumCombination.Multiply;

        /// <summary>
        /// Required if using the <see cref="EnumCombination.SelectiveMultiply"/> in <see cref="Combine"/>.
        /// </summary>
        [DocumentAsJson("Optional", "None")]
        public string OnVariant;

        public string IsValue;
    }
}
