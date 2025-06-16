using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class WeatherPatternConfig : ConditionalPatternConfig
    {
        public string Code;
        public string Name;
        public NatFloat DurationHours = NatFloat.createUniform(7.5f, 4.5f);

        public NatFloat SceneBrightness = NatFloat.createUniform(1, 0);
        public WeatherPrecipitationConfig Precipitation;
        public WeatherCloudConfig Clouds;
        public WeatherFogConfig Fog;
        

    }

    public class NoiseConfig
    {
        public double[] Amplitudes;
        public double[] Frequencies;
    }

    public class LightningConfig
    {
        public float MinTemperature;
        public float NearThunderRate;
        public float DistantThunderRate;
        public float LightningRate;
        public bool MulWithRainCloudness;
    }

    public enum EnumPrecipitationType
    {
        Rain,
        Snow,
        Hail,
        Auto
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
