using Vintagestory.API;
using Vintagestory.API.Common;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{
    /// <summary>
    /// Used for crop blocks that grow over time.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///"cropProps": {
	///	"requiredNutrient": "K",
	///	"nutrientConsumption": 40,
	///	"growthStages": 7,
	///	"totalGrowthMonths": 1.2,
	///	"coldDamageBelow": -10,
	///	"damageGrowthStuntMul": 0.75,
	///	"coldDamageRipeMul": 0.5,
	///	"heatDamageAbove": 32
	///},
    /// </code>
    /// </example>
    [DocumentAsJson]
    public class BlockCropPropertiesType
    {
        /// <summary>
        /// Which nutrient category this crop requires to grow
        /// </summary>
        [DocumentAsJson("Recommended", "N")]
        public EnumSoilNutrient RequiredNutrient;

        /// <summary>
        /// Total amount of nutrient consumed to reach full maturity. (100 is the maximum available for farmland)
        /// </summary>
        [DocumentAsJson("Required")]
        public float NutrientConsumption;

        /// <summary>
        /// Amount of growth stages this crop has.
        /// </summary>
        [DocumentAsJson("Required")]
        public int GrowthStages;

        /// <summary>
        /// Obsolete. Please use <see cref="TotalGrowthMonths"/> instead. Total time in ingame days required for the crop to reach full maturity assuming full nutrient levels
        /// </summary>
        [DocumentAsJson("Obsolete")]
        public float TotalGrowthDays;

        /// <summary>
        /// Total time in ingame months required for the crop to reach full maturity assuming full nutrient levels.
        /// </summary>
        [DocumentAsJson("Required")]
        public float TotalGrowthMonths;

        /// <summary>
        /// Currently unused. <!--If true, the player may harvests from the crop multiple times.-->
        /// </summary>
        [DocumentAsJson("Unused", "false")]
        public bool MultipleHarvests;

        /// <summary>
        /// Currently unused. <!--When <see cref="MultipleHarvests"/> is true, this is the amount of growth stages the crop should go back when harvested.-->
        /// </summary>
        [DocumentAsJson("Unused", "0")]
        public int HarvestGrowthStageLoss;

        /// <summary>
        /// The crop will be damaged if it falls below this temperature.
        /// </summary>
        [DocumentAsJson("Optional", "-5")]
        public float ColdDamageBelow = -5;

        /// <summary>
        /// If this crop is growing and damaged from cold or heat, the final yield will be multiplied by this amount.
        /// </summary>
        [DocumentAsJson("Optional", "0.5")]
        public float DamageGrowthStuntMul = 0.5f;

        /// <summary>
        /// If this crop is damaged from cold or heat and the crop is already grown, the yield will be multiplied by this amount.
        /// </summary>
        [DocumentAsJson("Optional", "0.5")]
        public float ColdDamageRipeMul = 0.5f;

        /// <summary>
        /// The crop will be damaged if it goes above this temperature.
        /// </summary>
        [DocumentAsJson("Optional", "40")]
        public float HeatDamageAbove = 40;

        /// <summary>
        /// Allows customization of crop growth behavior. BlockEntityFarmland calls methods on all behaviors to allow greater control. 
        /// </summary>
        [DocumentAsJson("Optional", "None")]
        public CropBehaviorType[] Behaviors;
    }
}
