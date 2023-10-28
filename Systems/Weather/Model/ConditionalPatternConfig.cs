using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public enum EnumChanceFunction
    {
        None,
        TestRainTemp,
        AvoidHotAndDry
    }

    public abstract class ConditionalPatternConfig
    {
        public EnumChanceFunction WeightFunction;
        public float? MinRain;
        public float? MaxRain;
        public float RainRange = 1;

        public float? MinTemp;
        public float? MaxTemp;
        public float TempRange = 1;

        public float Weight = 1f;


        public float getWeight(float rainfall, float temperature)
        {
            float hereweight = Weight;

            switch (WeightFunction)
            {
                case EnumChanceFunction.None:
                    break;

                case EnumChanceFunction.TestRainTemp:
                    if (MinRain != null)
                    {
                        hereweight *= GameMath.Clamp(rainfall - (float)MinRain, 0, RainRange) / RainRange;
                    }
                    if (MinTemp != null)
                    {
                        hereweight *= GameMath.Clamp(temperature - (float)MinTemp, 0, TempRange) / TempRange;
                    }
                    if (MaxRain != null)
                    {
                        hereweight *= GameMath.Clamp((float)MaxRain - rainfall, 0, RainRange) / RainRange;
                    }
                    if (MaxTemp != null)
                    {
                        hereweight *= GameMath.Clamp((float)MaxTemp - temperature, 0, TempRange) / TempRange;
                    }
                    break;

                case EnumChanceFunction.AvoidHotAndDry:

                    float tmprel = (TempRange + 20) / 60f;
                    float mul = rainfall * (1 - tmprel);
                    hereweight *= mul;

                    break;
            }

            return hereweight;
        }
    }
}
