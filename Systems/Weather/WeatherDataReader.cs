using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class WeatherDataReader : WeatherDataReaderBase
    {
        public WeatherDataReader(ICoreAPI api, WeatherSystemBase ws) : base(api, ws)
        {
        }

        public double GetBlendedCloudBrightness(Vec3d pos, float bMul = 1)
        {
            loadAdjacentSimsAndLerpValues(pos);

            return pgetBlendedCloudBrightness(bMul);
        }

        public double GetBlendedCloudOpaqueness(Vec3d pos)
        {
            loadAdjacentSimsAndLerpValues(pos);

            return pgetBlendedCloudOpaqueness();
        }

        public double GetBlendedCloudThicknessAt(Vec3d pos, int cloudTileX, int cloudTileZ)
        {
            loadAdjacentSimsAndLerpValues(pos);

            return pgetBlendedCloudThicknessAt(cloudTileX, cloudTileZ);
        }

        public double GetBlendedThinCloudModeness(Vec3d pos)
        {
            loadAdjacentSimsAndLerpValues(pos);

            return pgetBlendedThinCloudModeness();
        }

        public double GetBlendedUndulatingCloudModeness(Vec3d pos)
        {
            loadAdjacentSimsAndLerpValues(pos);

            return pgetBlendedUndulatingCloudModeness();
        }

        public double GetWindSpeed(Vec3d pos)
        {
            loadAdjacentSimsAndLerpValues(pos);

            return pgetWindSpeed(pos.Y);
        }
    }

    public class WeatherDataReaderPreLoad : WeatherDataReaderBase
    {        
        public WeatherDataReaderPreLoad(ICoreAPI api, WeatherSystemBase ws) : base(api, ws)
        {
        }

        public void LoadAdjacentSimsAndLerpValues(Vec3d pos)
        {
            loadAdjacentSimsAndLerpValues(pos);
        }


        public void LoadLerp(Vec3d pos)
        {
            loadLerp(pos);
        }

        public void UpdateAdjacentAndBlendWeatherData()
        {
            updateAdjacentAndBlendWeatherData();
        }


        public void EnsureCloudTileCacheIsFresh(Vec3i tilePos)
        {
            ensureCloudTileCacheIsFresh(tilePos);
        }



        public double GetWindSpeed(double posY)
        {
            return pgetWindSpeed(posY);
        }

        public double GetBlendedCloudThicknessAt(int cloudTileX, int cloudTileZ)
        {
            return pgetBlendedCloudThicknessAt(cloudTileX, cloudTileZ);
        }

        public double GetBlendedCloudOpaqueness()
        {
            return pgetBlendedCloudOpaqueness();
        }

        public double GetBlendedCloudBrightness(float b)
        {
            return pgetBlendedCloudBrightness(b);
        }

        public double GetBlendedThinCloudModeness()
        {
            return pgetBlendedThinCloudModeness();
        }

        public double GetBlendedUndulatingCloudModeness()
        {
            return pgetBlendedUndulatingCloudModeness();
        }

    }





    public abstract class WeatherDataReaderBase
    {
        public WeatherDataSnapshot BlendedWeatherData = new WeatherDataSnapshot();

        protected WeatherDataSnapshot blendedWeatherDataNoPrec = new WeatherDataSnapshot();

        protected WeatherDataSnapshot topBlendedWeatherData = new WeatherDataSnapshot();
        protected WeatherDataSnapshot botBlendedWeatherData = new WeatherDataSnapshot();


        public WeatherSimulationRegion[] AdjacentSims = new WeatherSimulationRegion[4];
        public double LerpLeftRight = 0;
        public double LerpTopBot = 0;

        ICoreAPI api;
        WeatherSystemBase ws;

        WeatherPattern rainOverlayData;
        WeatherDataSnapshot rainSnapData;

        public WeatherDataReaderBase(ICoreAPI api, WeatherSystemBase ws)
        {
            this.api = api;
            this.ws = ws;
            BlendedWeatherData.Ambient = new AmbientModifier().EnsurePopulated();
            blendedWeatherDataNoPrec.Ambient = new AmbientModifier().EnsurePopulated();

            AdjacentSims[0] = ws.dummySim;
            AdjacentSims[1] = ws.dummySim;
            AdjacentSims[2] = ws.dummySim;
            AdjacentSims[3] = ws.dummySim;
        }

        protected void loadAdjacentSimsAndLerpValues(Vec3d pos)
        {
            int regSize = api.World.BlockAccessor.RegionSize;

            int hereRegX = (int)pos.X / regSize;
            int hereRegZ = (int)pos.Z / regSize;

            int topLeftRegX = (int)Math.Round(pos.X / regSize) - 1;
            int topLeftRegZ = (int)Math.Round(pos.Z / regSize) - 1;

            int i = 0;
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dz = 0; dz <= 1; dz++)
                {
                    int regX = topLeftRegX + dx;
                    int regZ = topLeftRegZ + dz;

                    WeatherSimulationRegion weatherSim = ws.getOrCreateWeatherSimForRegion(regX, regZ);
                    if (weatherSim == null)
                    {
                        weatherSim = ws.dummySim;
                    }

                    AdjacentSims[i++] = weatherSim;

                    if (regX == hereRegX && regZ == hereRegZ)
                    {
                        hereMapRegion = weatherSim.MapRegion;
                    }
                }
            }

            loadLerp(pos);
        }


        float lerpRainCloudOverlay;
        float lerpRainOverlay;
        BlockPos tmpPos = new BlockPos();
        IMapRegion hereMapRegion;

        protected void loadLerp(Vec3d pos)
        {
            int regSize = api.World.BlockAccessor.RegionSize;

            double plrRegionRelX = (pos.X / regSize) - (int)Math.Round(pos.X / regSize);
            double plrRegionRelZ = (pos.Z / regSize) - (int)Math.Round(pos.Z / regSize);

            LerpTopBot = GameMath.Smootherstep(plrRegionRelX + 0.5);
            LerpLeftRight = GameMath.Smootherstep(plrRegionRelZ + 0.5);

            rainOverlayData = ws.rainOverlayPattern;
            rainSnapData = ws.rainOverlaySnap;


            if (hereMapRegion == null)
            {
                lerpRainCloudOverlay = 0;
                lerpRainOverlay = 0;
            }
            else
            {
                tmpPos.Set((int)pos.X, (int)pos.Y, (int)pos.Z);

                int noiseSizeClimate = hereMapRegion.ClimateMap.InnerSize;
                double posXInRegionClimate = (pos.X / regSize - pos.X / regSize) * noiseSizeClimate;
                double posZInRegionClimate = (pos.Z / regSize - pos.Z / regSize) * noiseSizeClimate;
                int climate = hereMapRegion.ClimateMap.GetUnpaddedColorLerped((float)posXInRegionClimate, (float)posZInRegionClimate);
                

                ClimateCondition conds = ws.GetClimateFast(tmpPos, climate);
                lerpRainCloudOverlay = conds.RainCloudOverlay;
                lerpRainOverlay = conds.Rainfall;
            }
        }

        protected void updateAdjacentAndBlendWeatherData()
        {
            AdjacentSims[0].UpdateWeatherData();
            AdjacentSims[1].UpdateWeatherData();
            AdjacentSims[2].UpdateWeatherData();
            AdjacentSims[3].UpdateWeatherData();
            topBlendedWeatherData.SetLerped(AdjacentSims[0].weatherData, AdjacentSims[1].weatherData, (float)LerpLeftRight);
            botBlendedWeatherData.SetLerped(AdjacentSims[2].weatherData, AdjacentSims[3].weatherData, (float)LerpLeftRight);

            blendedWeatherDataNoPrec.SetLerped(topBlendedWeatherData, botBlendedWeatherData, (float)LerpTopBot);
            blendedWeatherDataNoPrec.Ambient.CloudBrightness.Weight = 0;

            BlendedWeatherData.SetLerped(blendedWeatherDataNoPrec, rainSnapData, lerpRainOverlay);
        }

        protected void ensureCloudTileCacheIsFresh(Vec3i tilePos)
        {
            AdjacentSims[0].EnsureCloudTileCacheIsFresh(tilePos);
            AdjacentSims[1].EnsureCloudTileCacheIsFresh(tilePos);
            AdjacentSims[2].EnsureCloudTileCacheIsFresh(tilePos);
            AdjacentSims[3].EnsureCloudTileCacheIsFresh(tilePos);
        }


        protected double pgetWindSpeed(double posY)
        {
            return GameMath.BiLerp(
                AdjacentSims[0].GetWindSpeed(posY),
                AdjacentSims[1].GetWindSpeed(posY),
                AdjacentSims[2].GetWindSpeed(posY),
                AdjacentSims[3].GetWindSpeed(posY),
                LerpLeftRight,
                LerpTopBot
            );
        }

        protected double pgetBlendedCloudThicknessAt(int cloudTileX, int cloudTileZ)
        {
            double thick = GameMath.BiLerp(
                AdjacentSims[0].GetBlendedCloudThicknessAt(cloudTileX, cloudTileZ),
                AdjacentSims[1].GetBlendedCloudThicknessAt(cloudTileX, cloudTileZ),
                AdjacentSims[2].GetBlendedCloudThicknessAt(cloudTileX, cloudTileZ),
                AdjacentSims[3].GetBlendedCloudThicknessAt(cloudTileX, cloudTileZ),
                LerpLeftRight, LerpTopBot
            );

            double rainThick = rainOverlayData.State.nowbaseThickness;

            return GameMath.Lerp(thick, rainThick, lerpRainCloudOverlay);
        }

        protected double pgetBlendedCloudOpaqueness()
        {
            double opaque = GameMath.BiLerp(
                AdjacentSims[0].GetBlendedCloudOpaqueness(),
                AdjacentSims[1].GetBlendedCloudOpaqueness(),
                AdjacentSims[2].GetBlendedCloudOpaqueness(),
                AdjacentSims[3].GetBlendedCloudOpaqueness(),
                LerpLeftRight, LerpTopBot
            );

            double rainopaque = rainOverlayData.State.nowbaseOpaqueness;

            return GameMath.Lerp(opaque, rainopaque, lerpRainCloudOverlay);
        }

        protected double pgetBlendedCloudBrightness(float b)
        {
            double bright = GameMath.BiLerp(
                AdjacentSims[0].GetBlendedCloudBrightness(b),
                AdjacentSims[1].GetBlendedCloudBrightness(b),
                AdjacentSims[2].GetBlendedCloudBrightness(b),
                AdjacentSims[3].GetBlendedCloudBrightness(b),
                LerpLeftRight, LerpTopBot
            );

            double rainbright = rainOverlayData.State.nowCloudBrightness;

            return GameMath.Lerp(bright, rainbright, lerpRainCloudOverlay);
        }

        protected double pgetBlendedThinCloudModeness()
        {
            return GameMath.BiLerp(
                AdjacentSims[0].GetBlendedThinCloudModeness(),
                AdjacentSims[1].GetBlendedThinCloudModeness(),
                AdjacentSims[2].GetBlendedThinCloudModeness(),
                AdjacentSims[3].GetBlendedThinCloudModeness(),
                LerpLeftRight, LerpTopBot
            );
        }

        protected double pgetBlendedUndulatingCloudModeness()
        {
            return GameMath.BiLerp(
                AdjacentSims[0].GetBlendedUndulatingCloudModeness(),
                AdjacentSims[1].GetBlendedUndulatingCloudModeness(),
                AdjacentSims[2].GetBlendedUndulatingCloudModeness(),
                AdjacentSims[3].GetBlendedUndulatingCloudModeness(),
                LerpLeftRight, LerpTopBot
            );
        }
    }

}
