using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{

    public class WeatherEventConfig : ConditionalPatternConfig
    {
        public string Code;
        public string Name;
        
        public NatFloat DurationHours = NatFloat.createUniform(7.5f, 4.5f);

        public NatFloat Strength = NatFloat.Zero;
        public NoiseConfig StrengthNoise;

        public LightningConfig Lightning;
        public EnumPrecipitationType PrecType = EnumPrecipitationType.Auto;

        public NatFloat ParticleSize;
    }
}

