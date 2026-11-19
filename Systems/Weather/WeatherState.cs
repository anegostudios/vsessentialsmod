using System.ComponentModel;
using ProtoBuf;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class LightningFlashPacket
    {
        public Vec3d Pos;
        public int Seed;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WeatherPatternAssetsPacket {
        public string Data;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WeatherCloudYposPacket
    {
        public float CloudYRel;
    }

    public class WeatherPatternAssets
    {
        public WeatherSystemConfig GeneralConfig;
        public WeatherPatternConfig[] WeatherConfigs;
        public WindPatternConfig[] WindConfigs;
        public WeatherEventConfig[] WeatherEventConfigs;
    }

    [ProtoContract]
    public class WeatherState
    {
        [ProtoMember(1)]
        public int RegionX;

        [ProtoMember(2)]
        public int RegionZ;

        [ProtoMember(3)]
        public WeatherPatternState NewPattern;

        [ProtoMember(4)]
        public WeatherPatternState OldPattern;

        [ProtoMember(5)]
        public WindPatternState WindPattern;

        [ProtoMember(6)]
        public WeatherEventState WeatherEvent;

        [ProtoMember(7)]
        public float TransitionDelay;

        [ProtoMember(8)]
        public float Weight;

        [ProtoMember(9)]
        public bool Transitioning;

        [ProtoMember(10)]
        public bool updateInstant;

        [ProtoMember(11)]
        public double LastUpdateTotalHours;

        [ProtoMember(12)]
        public long LcgWorldSeed;

        [ProtoMember(13)]
        public long LcgMapGenSeed;

        [ProtoMember(14)]
        public long LcgCurrentSeed;

        [ProtoMember(15)]
        public SnowAccumSnapshot[] SnowAccumSnapshots;

        [ProtoMember(16)]
        public int Ringarraycursor;

    }

    [ProtoContract]
    public class WeatherConfigPacket
    {
        [ProtoMember(1)]
        public float? OverridePrecipitation;

        [ProtoMember(2)]
        public double RainCloudDaysOffset;
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

    [ProtoContract]
    public class WeatherPatternState
    {
        [ProtoMember(2)]
        public bool BeginUseExecuted;

        [ProtoMember(3)]
        public int Index = 0;

        [ProtoMember(16), DefaultValue(1)]
        public float nowSceneBrightness = 1;
        [ProtoMember(17)]
        public float nowThicknessMul;
        [ProtoMember(21)]
        public float nowbaseThickness;
        [ProtoMember(20)]
        public float nowbaseOpaqueness;
        [ProtoMember(18)]
        public float nowThinCloudModeness;
        [ProtoMember(19)]
        public float nowUndulatingCloudModeness;
        [ProtoMember(5)]
        public float nowCloudBrightness;
        [ProtoMember(9)]
        public float nowHeightMul;
        [ProtoMember(1)]
        public double ActiveUntilTotalHours;
        [ProtoMember(7), DefaultValue(1)]
        public float nowFogBrightness = 1;
        [ProtoMember(8)]
        public float nowFogDensity;
        [ProtoMember(11)]
        public float nowMistDensity;
        [ProtoMember(12)]
        public float nowMistYPos;

        [ProtoMember(13)]
        public float nowNearLightningRate;
        [ProtoMember(6)]
        public float nowDistantLightningRate;
        [ProtoMember(10)]
        public float nowLightningMinTempature;

        [ProtoMember(14)]
        public float nowPrecIntensity = 0;
        [ProtoMember(15), DefaultValue(EnumPrecipitationType.Auto)]
        public EnumPrecipitationType nowPrecType = EnumPrecipitationType.Auto;

        //public float nowPrecParticleSize = 0.1f;

        [ProtoMember(4)]
        public float nowBasePrecIntensity;
    }

}
