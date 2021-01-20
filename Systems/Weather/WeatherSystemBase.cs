using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public abstract class WeatherSystemBase : ModSystem
    {
        public ICoreAPI api;
        public WeatherSystemConfig GeneralConfig;
        public WeatherPatternConfig[] WeatherConfigs;
        public WindPatternConfig[] WindConfigs;
        public WeatherEventConfig[] WeatherEventConfigs;

        public bool autoChangePatterns = true;
        public Dictionary<long, WeatherSimulationRegion> weatherSimByMapRegion = new Dictionary<long, WeatherSimulationRegion>();
        protected SimplexNoise precipitationNoise;
        protected SimplexNoise precipitationNoiseSub;

        public virtual float? OverridePrecipitation { get; set; }
        public virtual double RainCloudDaysOffset { get; set; }

        public WeatherSimulationRegion dummySim;

        public WeatherDataReader WeatherDataSlowAccess;

        public WeatherPattern rainOverlayPattern;
        public WeatherDataSnapshot rainOverlaySnap;



        public virtual int CloudTileSize { get; set; } = 50;


        
        public override void Start(ICoreAPI api)
        {
            this.api = api;

            api.Network
               .RegisterChannel("weather")
               .RegisterMessageType(typeof(WeatherState))
               .RegisterMessageType(typeof(WeatherConfigPacket))
               .RegisterMessageType(typeof(WeatherPatternAssetsPacket))
            ;

            api.Event.OnGetWindSpeed += Event_OnGetWindSpeed;
        }

        private void Event_OnGetWindSpeed(Vec3d pos, ref Vec3d windSpeed)
        {
            windSpeed.X = WeatherDataSlowAccess.GetWindSpeed(pos);
        }

        public void Initialize()
        {
            precipitationNoise = SimplexNoise.FromDefaultOctaves(4, 0.02 / 3, 0.95, api.World.Seed - 18971121);
            precipitationNoiseSub = SimplexNoise.FromDefaultOctaves(3, 0.004 / 3, 0.95, api.World.Seed - 1717121);
        }

        public void InitDummySim()
        {
            dummySim = new WeatherSimulationRegion(this, 0, 0);
            dummySim.IsDummy = true;
            dummySim.Initialize();

            var rand = new LCGRandom(api.World.Seed);
            rand.InitPositionSeed(3, 3);

            rainOverlayPattern = new WeatherPattern(this, GeneralConfig.RainOverlayPattern, rand, 0, 0);
            rainOverlayPattern.Initialize(0, api.World.Seed);
            rainOverlayPattern.OnBeginUse();

            rainOverlaySnap = new WeatherDataSnapshot();
            
        }



        


    public PrecipitationState GetPrecipitationState(Vec3d pos)
        {
            return GetPrecipitationState(pos, api.World.Calendar.TotalDays);
        }

        public PrecipitationState GetPrecipitationState(Vec3d pos, double totalDays)
        {
            float level = GetPrecipitation(pos.X, pos.Y, pos.Z, totalDays);

            return new PrecipitationState()
            {
                Level = Math.Max(0, level - 0.5f),
                ParticleSize = Math.Max(0, level - 0.5f),
                Type = level > 0 ? WeatherDataSlowAccess.GetPrecType(pos) : EnumPrecipitationType.Auto
            };
        }


        public float GetPrecipitation(Vec3d pos)
        {
            return GetPrecipitation(pos.X, pos.Y, pos.Z, api.World.Calendar.TotalDays);
        }

        public float GetPrecipitation(double posX, double posY, double posZ)
        {
            return GetPrecipitation(posX, posY, posZ, api.World.Calendar.TotalDays);
        }

        public float GetPrecipitation(double posX, double posY, double posZ, double totalDays)
        {
            ClimateCondition conds = api.World.BlockAccessor.GetClimateAt(new BlockPos((int)posX, (int)posY, (int)posZ), EnumGetClimateMode.WorldGenValues, totalDays);
            return Math.Max(0, GetRainCloudness(conds, posX, posZ, totalDays) - 0.5f);
        }



        protected void Event_OnGetClimate(ref ClimateCondition climate, BlockPos pos, EnumGetClimateMode mode = EnumGetClimateMode.WorldGenValues, double totalDays = 0)
        {
            if (mode == EnumGetClimateMode.WorldGenValues) return;

            float rainCloudness = GetRainCloudness(climate, pos.X + 0.5, pos.Z + 0.5, totalDays);

            climate.Rainfall = GameMath.Clamp(rainCloudness - 0.5f, 0, 1);
            climate.RainCloudOverlay = GameMath.Clamp(rainCloudness, 0, 1);
        }

        public float GetRainCloudness(ClimateCondition conds, double posX, double posZ, double totalDays)
        {
            if (OverridePrecipitation != null)
            {
                return (float)OverridePrecipitation + 0.5f;
            }

            float offset = 0;
            if (conds != null)
            {
                offset = GameMath.Clamp((conds.Rainfall - 0.6f) * 2f, -1, 1f);
            }

            float value = getPrecipNoise(posX, posZ, totalDays + RainCloudDaysOffset, offset);

            return value;
        }

        public ClimateCondition GetClimateFast(BlockPos pos, int climate)
        {
            return api.World.BlockAccessor.GetClimateAt(pos, climate);
        }


        float getPrecipNoise(double posX, double posZ, double totalDays, float wgenRain)
        {
            return (float)GameMath.Max(
                precipitationNoise.Noise(posX / 9 / 2 + totalDays * 18, posZ / 9 / 2, totalDays * 4) * 1.6f -
                GameMath.Clamp(precipitationNoiseSub.Noise(posX / 4 / 2 + totalDays * 24, posZ / 4 / 2, totalDays * 6) * 5 - 1 - wgenRain, 0, 1)
                + wgenRain,
                0
            );
        }


        public WeatherDataReader getWeatherDataReader()
        {
            return new WeatherDataReader(api, this);
        }

        public WeatherDataReaderPreLoad getWeatherDataReaderPreLoad()
        {
            return new WeatherDataReaderPreLoad(api, this);
        }


        public WeatherSimulationRegion getOrCreateWeatherSimForRegion(int regionX, int regionZ)
        {
            long index2d = MapRegionIndex2D(regionX, regionZ);
            IMapRegion mapregion = api.World.BlockAccessor.GetMapRegion(regionX, regionZ);

            if (mapregion == null)
            {
                return null;
            }

            return getOrCreateWeatherSimForRegion(index2d, mapregion);
        }

        object weatherSimByMapRegionLock = new object();

        public WeatherSimulationRegion getOrCreateWeatherSimForRegion(long index2d, IMapRegion mapregion)
        {
            Vec3i regioncoord = MapRegionPosFromIndex2D(index2d);
            WeatherSimulationRegion weatherSim;

            lock (weatherSimByMapRegionLock)
            {
                if (weatherSimByMapRegion.TryGetValue(index2d, out weatherSim))
                {
                    return weatherSim;
                }
            }

            weatherSim = new WeatherSimulationRegion(this, regioncoord.X, regioncoord.Z);
            weatherSim.Initialize();

            byte[] data;
            if (mapregion.ModData.TryGetValue("weather", out data))
            {
                try
                {
                    weatherSim.FromBytes(data);
                    //api.World.Logger.Notification("{2}: Loaded weather pattern @{0}/{1}", regioncoord.X, regioncoord.Z, api.Side);
                }
                catch (Exception)
                {
                    //api.World.Logger.Warning("Unable to load weather pattern from region {0}/{1}, will load a random one. Likely due to game version change.", regioncoord.X, regioncoord.Z);
                    weatherSim.LoadRandomPattern();
                    weatherSim.NewWePattern.OnBeginUse();
                }
            } else
            {
                //api.World.Logger.Notification("{2}: Random weather pattern @{0}/{1}", regioncoord.X, regioncoord.Z, api.Side);
                weatherSim.LoadRandomPattern();
                weatherSim.NewWePattern.OnBeginUse();
                mapregion.ModData["weather"] = weatherSim.ToBytes();
            }

            weatherSim.MapRegion = mapregion;

            lock (weatherSimByMapRegionLock)
            {
                weatherSimByMapRegion[index2d] = weatherSim;
            }

            return weatherSim;
        }



        public long MapRegionIndex2D(int regionX, int regionZ)
        {
            return ((long)regionZ) * api.World.BlockAccessor.RegionMapSizeX + regionX;
        }


        public Vec3i MapRegionPosFromIndex2D(long index)
        {
            return new Vec3i(
                (int)(index % api.World.BlockAccessor.RegionMapSizeX),
                0,
                (int)(index / api.World.BlockAccessor.RegionMapSizeX)
            );
        }



    }
}

