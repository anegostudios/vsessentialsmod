using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WeatherPatternAssetsPacket {
        public string Data;
    }

    public class WeatherPatternAssets
    {
        public WeatherSystemConfig GeneralConfig;
        public WeatherPatternConfig[] WeatherConfigs;
        public WindPatternConfig[] WindConfigs;
        public WeatherEventConfig[] WeatherEventConfigs;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WeatherState
    {
        public int RegionX;
        public int RegionZ;

        public WeatherPatternState NewPattern;
        public WeatherPatternState OldPattern;

        public WindPatternState WindPattern;
        public WeatherEventState WeatherEvent;

        public float TransitionDelay;
        public float Weight;
        public bool Transitioning;
        public bool updateInstant;

        public double LastUpdateTotalHours;

        public long LcgWorldSeed;
        public long LcgMapGenSeed;
        public long LcgCurrentSeed;

        public SnowAccumSnapshot[] SnowAccumSnapshots;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WeatherConfigPacket
    {
        public float? OverridePrecipitation;
    }
    

    [ProtoContract]
    public class SnowAccumSnapshot
    {
        [ProtoMember(1)]
        public double TotalHours;
        [ProtoMember(2)]
        public FloatDataMap3D SumTemperatureByRegionCorner;
        [ProtoMember(3)]
        public int Checks;
        /// <summary>
        /// Snow accumulation
        /// above 0 = Temp below zero and precipitation happened by given amount
        /// 0 = no change 
        /// below 0 = Temp above zero and snow was able to melt by given amount, if there was any
        /// </summary>
        [ProtoMember(4)]
        public FloatDataMap3D SnowAccumulationByRegionCorner;


        public float GetAvgTemperatureByRegionCorner(float x, float y, float z)
        {
            return SumTemperatureByRegionCorner.GetLerped(x, y, z) / Checks;
        }

        public float GetAvgSnowAccumByRegionCorner(float x, float y, float z)
        {
            return SnowAccumulationByRegionCorner.GetLerped(x, y, z);
        }
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
        public float nowCloudBrightness;
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
        
        //public float nowPrecParticleSize = 0.1f;

        public float nowBasePrecIntensity;
    }

}
