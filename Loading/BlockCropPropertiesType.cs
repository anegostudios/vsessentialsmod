using Vintagestory.API.Common;

namespace Vintagestory.ServerMods.NoObf
{
    public class BlockCropPropertiesType
    {
        /// <summary>
        /// Which nutrient category this crop requires to grow
        /// </summary>
        public EnumSoilNutrient RequiredNutrient;

        /// <summary>
        /// Total amount of nutrient consumed to reach full maturity. (100 is the maximum available for farmland)
        /// </summary>
        public float NutrientConsumption;

        /// <summary>
        /// Amount of growth stages this crop has
        /// </summary>
        public int GrowthStages;

        /// <summary>
        /// Total time in ingame days required for the crop to reach full maturity assuming full nutrient levels
        /// </summary>
        public float TotalGrowthDays;

        /// <summary>
        /// Total time in ingame months required for the crop to reach full maturity assuming full nutrient levels
        /// </summary>
        public float TotalGrowthMonths;

        /// <summary>
        /// If true, the player may harvests from the crop multiple times
        /// </summary>
        public bool MultipleHarvests;

        /// <summary>
        /// When multiple harvets is true, this is the amount of growth stages the crop should go back when harvested
        /// </summary>
        public int HarvestGrowthStageLoss;

        public float ColdDamageBelow = -5;
        public float DamageGrowthStuntMul = 0.5f;
        public float ColdDamageRipeMul = 0.5f;
        public float HeatDamageAbove = 40;

        public CropBehaviorType[] Behaviors;
    }
}