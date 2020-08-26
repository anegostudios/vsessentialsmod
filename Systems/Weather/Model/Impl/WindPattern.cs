using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WindPatternState
    {
        public int Index;
        public float BaseStrength;
        public double ActiveUntilTotalHours;
    }

    public class WindPattern
    {
        public WindPatternConfig config;

        protected SimplexNoise strengthNoiseGen;
        ICoreAPI api;
        LCGRandom rand;

        public WindPatternState State = new WindPatternState();

        public float Strength;

        public WindPattern(ICoreAPI api, WindPatternConfig config, int index, LCGRandom rand, int seed)
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
        }

        public virtual void Update(float dt)
        {
            if (strengthNoiseGen != null)
            {
                double timeAxis = api.World.Calendar.TotalDays * 10;
                Strength = State.BaseStrength + (float)GameMath.Clamp(strengthNoiseGen.Noise(0, timeAxis), 0, 1);
            }
        }

        public virtual string GetWindName()
        {
            return config.Name;
        }

    }
}
