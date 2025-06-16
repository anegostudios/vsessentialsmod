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
