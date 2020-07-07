using System;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public enum EnumChanceFunction
    {
        None,
        TestRainTemp,
        AvoidHotAndDry
    }


    public class WeatherPatternConfig
    {
        public string Code;
        public string Name;
        public float Weight = 1f;
        public NatFloat DurationHours = NatFloat.createUniform(7.5f, 4.5f);

        public EnumChanceFunction WeightFunction;
        public float? MinRain;
        public float? MaxRain;
        public float RainRange = 1;

        public float? MinTemp;
        public float? MaxTemp;
        public float TempRange = 1;

        public NatFloat SceneBrightness = NatFloat.createUniform(1, 0);
        public WeatherPrecipitationConfig Precipitation;
        public WeatherCloudConfig Clouds;
        public WeatherFogConfig Fog;
        

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

                    //Math.Max(rainfall, (1 - tmprel)/2f);

                    hereweight *= mul;

                    break;
            }
            
            


            return hereweight;
        }
    }

    public class NoiseConfig
    {
        public double[] Amplitudes;
        public double[] Frequencies;
    }

    public class LightningConfig
    {
        public float MinTemperature;
        public float NearRate;
        public float DistantRate;
    }

    public enum EnumPrecipitationType
    {
        Rain,
        Snow,
        Hail
    }

    public class WeatherPrecipitationConfig
    {
        public NatFloat BaseIntensity;
        public NoiseConfig IntensityNoise;
	//	public EnumPrecipitationType Type = EnumPrecipitationType.Auto;
        public float ParticleSize = 1f;
    }

    public class WeatherCloudConfig
    {
        public NatFloat Brightness = NatFloat.createUniform(1, 0);
        public NatFloat HeightMul = NatFloat.createUniform(1,0);
        public NatFloat BaseThickness;
        public NatFloat ThinCloudMode = NatFloat.createUniform(0, 0);
        public NatFloat UndulatingCloudMode = NatFloat.createUniform(0, 0);
        public NatFloat ThicknessMul = NatFloat.createUniform(1, 0);
        public NoiseConfig LocationalThickness;

        public NatFloat Opaqueness;
    }

    public class WeatherFogConfig
    {
        public NatFloat FogBrightness;
        public NatFloat Density;
        public NatFloat MistDensity;
        public NatFloat MistYPos;
    }
}
