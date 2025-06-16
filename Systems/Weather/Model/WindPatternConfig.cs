using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{

    public class WindPatternConfig
    {
        public string Code;
        public string Name;
        public float Weight = 1f;
        public NatFloat DurationHours = NatFloat.createUniform(7.5f, 4.5f);

        public NatFloat Strength;
        public NoiseConfig StrengthNoise;
    }
}

