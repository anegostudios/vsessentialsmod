using Vintagestory.API;
using Vintagestory.API.Common;

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
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>N</jsondefault>-->
        /// Which nutrient category this crop requires to grow
        /// </summary>
        [DocumentAsJson] public EnumSoilNutrient RequiredNutrient;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// Total amount of nutrient consumed to reach full maturity. (100 is the maximum available for farmland)
        /// </summary>
        [DocumentAsJson] public float NutrientConsumption;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// Amount of growth stages this crop has.
        /// </summary>
        [DocumentAsJson] public int GrowthStages;

        /// <summary>
        /// <!--<jsonoptional>Obsolete</jsonoptional>-->
        /// Obsolete. Please use <see cref="TotalGrowthMonths"/> instead. Total time in ingame days required for the crop to reach full maturity assuming full nutrient levels
        /// </summary>
        [DocumentAsJson] public float TotalGrowthDays;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// Total time in ingame months required for the crop to reach full maturity assuming full nutrient levels.
        /// </summary>
        [DocumentAsJson] public float TotalGrowthMonths;

        /// <summary>
        /// <!--<jsonoptional>Unused</jsonoptional><jsondefault>false</jsondefault>-->
        /// Currently unused. <!--If true, the player may harvests from the crop multiple times.-->
        /// </summary>
        [DocumentAsJson] public bool MultipleHarvests;

        /// <summary>
        /// <!--<jsonoptional>Unused</jsonoptional><jsondefault>0</jsondefault>-->
        /// Currently unused. <!--When <see cref="MultipleHarvests"/> is true, this is the amount of growth stages the crop should go back when harvested.-->
        /// </summary>
        [DocumentAsJson] public int HarvestGrowthStageLoss;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>-5</jsondefault>-->
        /// The crop will be damaged if it falls below this temperature.
        /// </summary>
        [DocumentAsJson] public float ColdDamageBelow = -5;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.5</jsondefault>-->
        /// If this crop is growing and damaged from cold or heat, the final yield will be multiplied by this amount.
        /// </summary>
        [DocumentAsJson] public float DamageGrowthStuntMul = 0.5f;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.5</jsondefault>-->
        /// If this crop is damaged from cold or heat and the crop is already grown, the yield will be multiplied by this amount.
        /// </summary>
        [DocumentAsJson] public float ColdDamageRipeMul = 0.5f;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>40</jsondefault>-->
        /// The crop will be damaged if it goes above this temperature.
        /// </summary>
        [DocumentAsJson] public float HeatDamageAbove = 40;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Allows customization of crop growth behavior. BlockEntityFarmland calls methods on all behaviors to allow greater control. 
        /// </summary>
        [DocumentAsJson] public CropBehaviorType[] Behaviors;
    }
}