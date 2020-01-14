using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WeatherState
    {
        public int RegionX;
        public int RegionZ;

        public WeatherPatternState NewPattern;
        public WeatherPatternState OldPattern;

        public WindPatternState WindPattern;

        public float TransitionDelay;
        public float Weight;
        public bool Transitioning;
        public bool updateInstant;

        public double LastUpdateTotalHours;

        public long LcgWorldSeed;
        public long LcgMapGenSeed;
        public long LcgCurrentSeed;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WeatherPatternState
    {
        public bool BeginUseExecuted;

        public int Index = 0;

        public float nowSceneBrightness = 1;
        public float nowThicknessMul;
        public float nowbaseThickness;
        public float nowbaseOpaqueness;
        public float nowThinCloudModeness;
        public float nowUndulatingCloudModeness;
        public float nowBrightness;
        public float nowHeightMul;
        public double ActiveUntilTotalHours;
        public float nowFogBrightness = 1;
        public float nowFogDensity;
        public float nowMistDensity;
        public float nowMistYPos;

        public float nowNearLightningRate;
        public float nowDistantLightningRate;
        public float nowLightningMinTempature;

        public float nowPrecIntensity = 0;
        public EnumPrecipitationType nowPrecType = EnumPrecipitationType.Auto;
        public float nowPrecParticleSize = 0.1f;

        public float nowBasePrecIntensity;
    }

}
