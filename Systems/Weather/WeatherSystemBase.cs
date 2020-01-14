﻿using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class PrecipitationState
    {
        public double Level;
        public double ParticleSize;
        public EnumPrecipitationType Type;
    }

    public abstract class WeatherSystemBase : ModSystem
    {
        public ICoreAPI api;
        public WeatherPatternConfig[] weatherConfigs;
        public WindPatternConfig[] windConfigs;

        public bool autoChangePatterns = true;
        public Dictionary<long, WeatherSimulation> weatherSimByMapRegion = new Dictionary<long, WeatherSimulation>();
        protected NormalizedSimplexNoise windNoise;

        public NormalizedSimplexNoise weatherPatternNoise;

        public WeatherDataSnapshot blendedWeatherData = new WeatherDataSnapshot();
        protected WeatherDataSnapshot topBlendedWeatherData = new WeatherDataSnapshot();
        protected WeatherDataSnapshot botBlendedWeatherData = new WeatherDataSnapshot();
        
        public WeatherSimulation[] adjacentSims
        {
            get
            {
                return adjacentSimsTL.Value;
            }
            set
            {
                adjacentSimsTL.Value = value;
            }
        }

        public double lerpLeftRight
        {
            get
            {
                return lerpLeftRightTL.Value;
            }
            set
            {
                lerpLeftRightTL.Value = value;
            }
        }

        public double lerpTopBot
        {
            get
            {
                return lerpTopBotTL.Value;
            }
            set
            {
                lerpTopBotTL.Value = value;
            }
        }

        public ThreadLocal<WeatherSimulation[]> adjacentSimsTL = new ThreadLocal<WeatherSimulation[]>(() => new WeatherSimulation[4]);
        public ThreadLocal<double> lerpLeftRightTL = new ThreadLocal<double>(() => 0);
        public ThreadLocal<double> lerpTopBotTL = new ThreadLocal<double>(() => 0);

        public WeatherSimulation dummySim;

        public virtual int CloudTileLength { get; set; }

        public virtual int CloudTileX { get; set; }
        public virtual int CloudTileZ { get; set; }


        public override void Start(ICoreAPI api)
        {
            this.api = api;
            LoadConfigs();
        }

        public void Initialize()
        {
            windNoise = NormalizedSimplexNoise.FromDefaultOctaves(6, 0.1, 0.8, api.World.Seed + 2323182);

            weatherPatternNoise = NormalizedSimplexNoise.FromDefaultOctaves(6, 0.1, 0.8, api.World.Seed + 9867910);

            dummySim = new WeatherSimulation(this, 0, 0);
            dummySim.IsDummy = true;
        }



        public void ReloadConfigs()
        {
            api.Assets.Reload(new AssetLocation("config/"));
            LoadConfigs();
        }

        public void LoadConfigs() { 
            var dictWeatherPatterns = api.Assets.GetMany<WeatherPatternConfig[]>(api.World.Logger, "config/weatherpatterns/");
            var orderedWeatherPatterns = dictWeatherPatterns.OrderBy(val => val.Key.ToString()).Select(val => val.Value).ToArray();

            weatherConfigs = new WeatherPatternConfig[0];
            foreach (var val in orderedWeatherPatterns)
            {
                weatherConfigs = weatherConfigs.Append(val);
            }

            var dictWindPatterns = api.Assets.GetMany<WindPatternConfig[]>(api.World.Logger, "config/windpatterns/");
            var orderedWindPatterns = dictWindPatterns.OrderBy(val => val.Key.ToString()).Select(val => val.Value).ToArray();

            windConfigs = new WindPatternConfig[0];
            foreach (var val in orderedWindPatterns)
            {
                windConfigs = windConfigs.Append(val);
            }

            api.World.Logger.Notification("Reloaded {0} weather patterns and {1} wind patterns", weatherConfigs.Length, windConfigs.Length);
        }


        public PrecipitationState GetPrecipitationState(Vec3d pos)
        {
            LoadAdjacentSimsAndLerpValues(pos);

            double level = GameMath.BiLerp(
                adjacentSims[0].weatherData.PrecIntensity,
                adjacentSims[1].weatherData.PrecIntensity,
                adjacentSims[2].weatherData.PrecIntensity,
                adjacentSims[3].weatherData.PrecIntensity,
                lerpLeftRight,
                lerpTopBot
            );

            double size = GameMath.BiLerp(
                adjacentSims[0].weatherData.PrecParticleSize,
                adjacentSims[1].weatherData.PrecParticleSize,
                adjacentSims[2].weatherData.PrecParticleSize,
                adjacentSims[3].weatherData.PrecParticleSize,
                lerpLeftRight,
                lerpTopBot
            );

            EnumPrecipitationType top = lerpLeftRight < 0.5 ? adjacentSims[0].weatherData.nowPrecType : adjacentSims[1].weatherData.nowPrecType;
            EnumPrecipitationType bot = lerpLeftRight < 0.5 ? adjacentSims[2].weatherData.nowPrecType : adjacentSims[3].weatherData.nowPrecType;

            EnumPrecipitationType type = lerpTopBot < 0.5 ? top : bot;

            return new PrecipitationState()
            {
                Level = level,
                ParticleSize = size,
                Type = type
            };
        }

        public double GetRainFall(Vec3d pos)
        {
            LoadAdjacentSimsAndLerpValues(pos);

            return GameMath.BiLerp(
                adjacentSims[0].weatherData.PrecIntensity,
                adjacentSims[1].weatherData.PrecIntensity,
                adjacentSims[2].weatherData.PrecIntensity,
                adjacentSims[3].weatherData.PrecIntensity,
                lerpLeftRight,
                lerpTopBot
            );
        }

        public double GetWindSpeed(Vec3d pos)
        {
            LoadAdjacentSimsAndLerpValues(pos);

            return GameMath.BiLerp(
                adjacentSims[0].GetWindSpeed(pos.Y),
                adjacentSims[1].GetWindSpeed(pos.Y),
                adjacentSims[2].GetWindSpeed(pos.Y),
                adjacentSims[3].GetWindSpeed(pos.Y),
                lerpLeftRight,
                lerpTopBot
            );
        }


        public void LoadAdjacentSimsAndLerpValues(Vec3d pos)
        {
            int regSize = api.World.BlockAccessor.RegionSize;

            int topLeftRegX = (int)Math.Round(pos.X / regSize) - 1;
            int topLeftRegZ = (int)Math.Round(pos.Z / regSize) - 1;

            int i = 0;
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dz = 0; dz <= 1; dz++)
                {
                    int regX = topLeftRegX + dx;
                    int regZ = topLeftRegZ + dz;

                    WeatherSimulation weatherSim = getOrCreateWeatherSimForRegion(regX, regZ);
                    if (weatherSim == null)
                    {
                        weatherSim = dummySim;
                    }

                    adjacentSims[i++] = weatherSim;
                }
            }


            double plrRegionRelX = (pos.X / regSize) - (int)Math.Round(pos.X / regSize);
            double plrRegionRelZ = (pos.Z / regSize) - (int)Math.Round(pos.Z / regSize);

            lerpTopBot = plrRegionRelX + 0.5; // (pos.X / regSize) - (topLeftRegX + 1);
            lerpLeftRight = plrRegionRelZ + 0.5; // (pos.Z / regSize) - (topLeftRegZ + 1);

            //lerpLeftRight = GameMath.Smootherstep(lerpLeftRight);
            //lerpTopBot = GameMath.Smootherstep(lerpTopBot);
        }


        public void updateAdjacentAndBlendWeatherData()
        {
            adjacentSims[0].UpdateWeatherData();
            adjacentSims[1].UpdateWeatherData();
            adjacentSims[2].UpdateWeatherData();
            adjacentSims[3].UpdateWeatherData();
            topBlendedWeatherData.SetLerped(adjacentSims[0].weatherData, adjacentSims[1].weatherData, (float)lerpLeftRight);
            botBlendedWeatherData.SetLerped(adjacentSims[2].weatherData, adjacentSims[3].weatherData, (float)lerpLeftRight);

            blendedWeatherData.SetLerped(topBlendedWeatherData, botBlendedWeatherData, (float)lerpTopBot);

        }


        public WeatherSimulation getOrCreateWeatherSimForRegion(int regionX, int regionZ)
        {
            long index2d = MapRegionIndex2D(regionX, regionZ);
            IMapRegion mapregion = api.World.BlockAccessor.GetMapRegion(regionX, regionZ);
            if (mapregion == null)
            {
                return null;
            }
            return getOrCreateWeatherSimForRegion(index2d, mapregion);
        }


        public WeatherSimulation getOrCreateWeatherSimForRegion(int regionX, int regionZ, IMapRegion mapregion)
        {
            long index2d = MapRegionIndex2D(regionX, regionZ);
            return getOrCreateWeatherSimForRegion(index2d, mapregion);
        }

        public WeatherSimulation getOrCreateWeatherSimForRegion(long index2d, IMapRegion mapregion)
        {
            Vec3i regioncoord = MapRegionPosFromIndex2D(index2d);

            WeatherSimulation weatherSim;
            if (weatherSimByMapRegion.TryGetValue(index2d, out weatherSim))
            {
                return weatherSim;
            }


            weatherSim = new WeatherSimulation(this, regioncoord.X, regioncoord.Z);
            weatherSim.Initialize();

            byte[] data;
            if (mapregion.ModData.TryGetValue("weather", out data))
            {
                try
                {
                    weatherSim.FromBytes(data);
                    api.World.Logger.Notification("{2}: Loaded weather pattern @{0}/{1}", regioncoord.X, regioncoord.Z, api.Side);
                }
                catch (Exception)
                {
                    api.World.Logger.Warning("Unable to load weather pattern from region {0}/{1}, will load a random one", regioncoord.X, regioncoord.Z);
                    weatherSim.LoadRandomPattern();
                    weatherSim.NewWePattern.OnBeginUse();
                }
            } else
            {
                api.World.Logger.Notification("{2}: Random weather pattern @{0}/{1}", regioncoord.X, regioncoord.Z, api.Side);
                weatherSim.LoadRandomPattern();
                weatherSim.NewWePattern.OnBeginUse();
                mapregion.ModData["weather"] = weatherSim.ToBytes();
            }

            weatherSimByMapRegion[index2d] = weatherSim;

            return weatherSim;
        }



        public long MapRegionIndex2D(int regionX, int regionZ)
        {
            return ((long)regionZ) * RegionMapSizeX + regionX;
        }

        internal int RegionMapSizeX
        {
            get { return api.World.BlockAccessor.MapSizeX / api.World.BlockAccessor.RegionSize; }
        }

        public Vec3i MapRegionPosFromIndex2D(long index)
        {
            if (RegionMapSizeX == 0) // For maps smaller than RegionSize
            {
                return new Vec3i(0, 0, 0);
            }

            return new Vec3i(
                (int)(index % RegionMapSizeX),
                0,
                (int)(index / RegionMapSizeX)
            );
        }

        public override void Dispose()
        {
            adjacentSimsTL.Dispose();
            lerpLeftRightTL.Dispose();
            lerpTopBotTL.Dispose();
            base.Dispose();
        }

    }
}
