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
    public class WeatherPattern
    {
        public WeatherPatternConfig config;
        public SimplexNoise LocationalCloudThicknessGen;

        public WeatherPatternState State = new WeatherPatternState();


        protected SimplexNoise TimeBasePrecIntenstityGen;
        WeatherSystemBase ws;
        ICoreAPI api;
        int lastTileX, lastTileZ;
        public double[,] CloudDensityNoiseCache;
        //public event API.Common.Action BeginUse;
        LCGRandom rand;
        public float hereChance;

        public void updateHereChance(float rainfall, float temperature)
        {
            hereChance = config.getWeight(rainfall, temperature);
        }

        public WeatherPattern(WeatherSystemBase ws, WeatherPatternConfig config, LCGRandom rand)
        {
            this.ws = ws;
            this.rand = rand;
            this.config = config;
            this.api = ws.api;

        }

        public virtual void Initialize(int index, int seed)
        {
            State.Index = index;

            if (config.Clouds?.LocationalThickness != null)
            {
                this.LocationalCloudThicknessGen = new SimplexNoise(config.Clouds.LocationalThickness.Amplitudes, config.Clouds.LocationalThickness.Frequencies, seed + index + 1512);
            }
            if (config.Precipitation?.IntensityNoise != null)
            {
                this.TimeBasePrecIntenstityGen = new SimplexNoise(config.Precipitation.IntensityNoise.Amplitudes, config.Precipitation.IntensityNoise.Frequencies, seed + index + 19987986);
            }

            EnsureNoiseCacheIsFresh();
        }

        
        public virtual void EnsureNoiseCacheIsFresh()
        {
            if (api.Side == EnumAppSide.Server) return;

            bool unfresh =
                CloudDensityNoiseCache == null ||
                lastTileX != ws.CloudTileX || lastTileZ != ws.CloudTileZ ||
                ws.CloudTileLength != CloudDensityNoiseCache.GetLength(1)
            ;

            if (unfresh)
            {
                RegenNoiseCache();
                return;
            }
        }

        public virtual void RegenNoiseCache()
        {
            int len = ws.CloudTileLength;

            CloudDensityNoiseCache = new double[len, len];

            lastTileX = ws.CloudTileX;
            lastTileZ = ws.CloudTileZ;

            double timeAxis = api.World.Calendar.TotalDays / 10.0;

            if (LocationalCloudThicknessGen == null)
            {
                for (int dx = 0; dx < len; dx++)
                {
                    for (int dz = 0; dz < len; dz++)
                    {
                        CloudDensityNoiseCache[dx, dz] = 0;
                    }
                }

            }
            else
            {

                for (int dx = 0; dx < len; dx++)
                {
                    for (int dz = 0; dz < len; dz++)
                    {
                        double x = (lastTileX + dx - len / 2) / 20.0;
                        double z = (lastTileZ + dz - len / 2) / 20.0;

                        CloudDensityNoiseCache[dx, dz] = GameMath.Clamp(LocationalCloudThicknessGen.Noise(x, z, timeAxis), 0, 1);
                    }
                }
            }
        }

        public virtual void OnBeginUse()
        {
            //BeginUse?.Invoke();
            State.BeginUseExecuted = true;
            State.ActiveUntilTotalHours = api.World.Calendar.TotalHours + config.DurationHours.nextFloat(1, rand);

            State.nowThinCloudModeness = config.Clouds?.ThinCloudMode?.nextFloat(1, rand) ?? 0;
            State.nowUndulatingCloudModeness = config.Clouds?.UndulatingCloudMode?.nextFloat(1, rand) ?? 0;
            State.nowbaseThickness = config.Clouds?.BaseThickness?.nextFloat(1, rand) ?? 0;
            State.nowThicknessMul = config.Clouds?.ThicknessMul?.nextFloat(1, rand) ?? 1;
            State.nowbaseOpaqueness = config.Clouds?.Opaqueness?.nextFloat(1, rand) ?? 0;
            State.nowBrightness = config.Clouds?.Brightness.nextFloat(1, rand) ?? 0;
            State.nowHeightMul = config.Clouds?.HeightMul?.nextFloat(1, rand) ?? 0;
            State.nowSceneBrightness = config.SceneBrightness.nextFloat(1, rand);

            State.nowFogDensity = config.Fog?.Density?.nextFloat(1, rand) ?? 0;
            State.nowMistDensity = config.Fog?.MistDensity?.nextFloat(1, rand) ?? 0;
            State.nowMistYPos = config.Fog?.MistYPos?.nextFloat(1, rand) ?? 0;
            State.nowFogBrightness = config.Fog?.FogBrightness?.nextFloat(1, rand) ?? 1;

            State.nowBasePrecIntensity = (config.Precipitation?.BaseIntensity?.nextFloat(1, rand) ?? 0);
            State.nowPrecParticleSize = config.Precipitation?.ParticleSize ?? 1;
            State.nowPrecType = config.Precipitation?.Type ?? EnumPrecipitationType.Auto;

            State.nowNearLightningRate = config.Lightning?.NearRate / 100f ?? 0;
            State.nowDistantLightningRate = config.Lightning?.DistantRate / 100f ?? 0;
            State.nowLightningMinTempature = config.Lightning?.MinTemperature ?? 0;


            State.nowPrecIntensity = State.nowBasePrecIntensity;

            RegenNoiseCache();
        }

        public virtual void Update(float dt)
        {
            if (!State.BeginUseExecuted)
            {
                int a = 1;
            }

            EnsureNoiseCacheIsFresh();

            double timeAxis = api.World.Calendar.TotalDays / 10.0;
            State.nowPrecIntensity = State.nowBasePrecIntensity + (float)GameMath.Clamp(TimeBasePrecIntenstityGen?.Noise(0, timeAxis) ?? 0, 0, 1);
        }

        public virtual double GetCloudDensityAt(int dx, int dz)
        {
            return GameMath.Clamp(State.nowbaseThickness + CloudDensityNoiseCache[dx, dz], 0, 1) * State.nowThicknessMul;
        }


        public virtual string GetWeatherName()
        {
            return config.Name;
        }

    }
}
