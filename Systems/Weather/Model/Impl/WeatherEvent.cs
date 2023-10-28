using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WeatherEventState
    {
        public int Index;
        public float BaseStrength;
        public double ActiveUntilTotalHours;

        public float LightningRate;
        public float NearThunderRate;
        public float DistantThunderRate;
        public float LightningMinTemp;
        public EnumPrecipitationType PrecType = EnumPrecipitationType.Auto;

        public float ParticleSize;
    }

    public class WeatherEvent
    {
        public WeatherEventConfig config;

        protected SimplexNoise strengthNoiseGen;
        ICoreAPI api;
        LCGRandom rand;

        public WeatherEventState State = new WeatherEventState();

        public float Strength;
        internal float hereChance;

        public bool AllowStop { get; set; } = true;

        public bool ShouldStop(float rainfall, float temperature)
        {
            return config.getWeight(rainfall, temperature) <= 0 && AllowStop;
        }

        public WeatherEvent(ICoreAPI api, WeatherEventConfig config, int index, LCGRandom rand, int seed)
        {
            this.rand = rand;
            this.config = config;
            this.api = api;
            this.State.Index = index;

            if (config.StrengthNoise != null)
            {
                strengthNoiseGen = new SimplexNoise(config.StrengthNoise.Amplitudes, config.StrengthNoise.Frequencies, seed + index);
            }
        }


        public virtual void OnBeginUse()
        {
            State.BaseStrength = Strength = config.Strength.nextFloat(1, rand);
            State.ActiveUntilTotalHours = api.World.Calendar.TotalHours + config.DurationHours.nextFloat(1, rand);

            State.PrecType = config.PrecType;
            State.NearThunderRate = config.Lightning?.NearThunderRate / 100f ?? 0;
            State.LightningRate = config.Lightning?.LightningRate / 100f ?? 0;
            State.DistantThunderRate = config.Lightning?.DistantThunderRate / 100f ?? 0;
            State.LightningMinTemp = config.Lightning?.MinTemperature ?? 0;
        }

        public virtual void Update(float dt)
        {
            if (strengthNoiseGen != null)
            {
                double timeAxis = api.World.Calendar.TotalDays / 10.0;
                Strength = State.BaseStrength + (float)GameMath.Clamp(strengthNoiseGen.Noise(0, timeAxis), 0, 1);
            }
        }

        public virtual string GetWindName()
        {
            return config.Name;
        }

        internal void updateHereChance(float rainfall, float temperature)
        {
            hereChance = config.getWeight(rainfall, temperature);
        }
    }
}
