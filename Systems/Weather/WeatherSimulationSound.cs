using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{

    public class WeatherSimulationSound
    {
        WeatherSystemClient weatherSys;

        ILoadedSound[] rainSoundsLeafless;
        ILoadedSound[] rainSoundsLeafy;
        ILoadedSound lowTrembleSound;
        ILoadedSound hailSound;
        ILoadedSound windSoundLeafy;
        ILoadedSound windSoundLeafless;


        ICoreClientAPI capi;

        bool windSoundsOn;
        bool rainSoundsOn;
        bool hailSoundsOn;

        
        float curWindVolumeLeafy = 0f;
        float curWindVolumeLeafless = 0f;
        float curRainVolumeLeafy = 0f;
        float curRainVolumeLeafless = 0f;

        float curRainPitch = 1f;
        float curHailVolume = 0f;
        float curHailPitch = 1f;
        float curTrembleVolume = 0f;

        float quarterSecAccum;

        bool searchComplete = true;
        public static float roomVolumePitchLoss;


        BlockPos plrPos = new BlockPos();

        public WeatherSimulationSound(ICoreClientAPI capi, WeatherSystemClient weatherSys)
        {
            this.weatherSys = weatherSys;
            this.capi = capi;
        }


        internal void Initialize()
        {
            lowTrembleSound = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation("sounds/weather/tracks/verylowtremble.ogg"),
                ShouldLoop = true,
                DisposeOnFinish = false,
                Position = new Vec3f(0, 0, 0),
                RelativePosition = true,
                Range = 16,
                SoundType = EnumSoundType.Weather,
                Volume = 1
            });

            hailSound = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation("sounds/weather/tracks/hail.ogg"),
                ShouldLoop = true,
                DisposeOnFinish = false,
                Position = new Vec3f(0, 0, 0),
                RelativePosition = true,
                Range = 16,
                SoundType = EnumSoundType.Weather,
                Volume = 1
            });


            rainSoundsLeafless = new ILoadedSound[1];
            rainSoundsLeafless[0] = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation("sounds/weather/tracks/rain-leafless.ogg"),
                ShouldLoop = true,
                DisposeOnFinish = false,
                Position = new Vec3f(0, 0, 0),
                RelativePosition = true,
                Range = 16,
                SoundType = EnumSoundType.Weather,
                Volume = 1
            });

            rainSoundsLeafy = new ILoadedSound[1];
            rainSoundsLeafy[0] = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation("sounds/weather/tracks/rain-leafy.ogg"),
                ShouldLoop = true,
                DisposeOnFinish = false,
                Position = new Vec3f(0, 0, 0),
                RelativePosition = true,
                Range = 16,
                SoundType = EnumSoundType.Weather,
                Volume = 1
            });


            windSoundLeafy = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation("sounds/weather/wind-leafy.ogg"),
                ShouldLoop = true,
                DisposeOnFinish = false,
                Position = new Vec3f(0, 0, 0),
                RelativePosition = true,
                Range = 16,
                SoundType = EnumSoundType.Weather,
                Volume = 1
            });

            windSoundLeafless = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation("sounds/weather/wind-leafless.ogg"),
                ShouldLoop = true,
                DisposeOnFinish = false,
                Position = new Vec3f(0, 0, 0),
                RelativePosition = true,
                Range = 16,
                SoundType = EnumSoundType.Weather,
                Volume = 1
            });
        }

        public void Update(float dt)
        {
            dt = Math.Min(0.5f, dt);

            quarterSecAccum += dt;

            if (quarterSecAccum > 0.25f)
            {
                updateSounds(dt);
            }

            /*secAccum += dt;
            if (secAccum > 1f)
            {
                EntityPlayer eplr = capi.World.Player.Entity;
                plrPos.Set((int)eplr.Pos.X, (int)eplr.Pos.Y, (int)eplr.Pos.Z);

                float ownRainy = capi.World.BlockAccessor.GetRainMapHeightAt(plrPos);
                windSoundIntensity = Math.Max(0, 1 - capi.World.BlockAccessor.GetHorDistanceToRainFall(plrPos) / 5f - Math.Max(0, (ownRainy - plrPos.Y - 15) / 10));
                secAccum = 0;
            }*/
        }


        private void updateSounds(float dt)
        {
            float targetRainVolumeLeafy=0;
            float targetRainVolumeLeafless = 0;
            float targetHailVolume=0;
            float targetTrembleVolume=0;

            float targetRainPitch=1;
            float targetHailPitch=1;

            WeatherDataSnapshot weatherData = weatherSys.BlendedWeatherData;

            if (searchComplete)
            {
                EntityPlayer eplr = capi.World.Player.Entity;
                plrPos.Set((int)eplr.Pos.X, (int)eplr.Pos.Y, (int)eplr.Pos.Z);
                searchComplete = false;

                TyronThreadPool.QueueTask(() =>
                {
                    float val = (float)Math.Pow(Math.Max(0, (capi.World.BlockAccessor.GetDistanceToRainFall(plrPos, 12, 4) - 2) / 10f), 2);
                    roomVolumePitchLoss = GameMath.Clamp(val, 0, 1);
                    searchComplete = true;
                }, "weathersimulationsound");
            }

            EnumPrecipitationType precType = weatherData.BlendedPrecType;
            if (precType == EnumPrecipitationType.Auto)
            {
                precType = weatherSys.clientClimateCond?.Temperature < weatherData.snowThresholdTemp ? EnumPrecipitationType.Snow : EnumPrecipitationType.Rain;
            }

            float nearbyLeaviness = GameMath.Clamp(GlobalConstants.CurrentNearbyRelLeavesCountClient * 60, 0, 1);

            ClimateCondition conds = weatherSys.clientClimateCond;

            if (conds.Rainfall > 0)
            {
                if (precType == EnumPrecipitationType.Rain || weatherSys.clientClimateCond.Temperature < weatherData.snowThresholdTemp)
                {
                    targetRainVolumeLeafy = nearbyLeaviness * GameMath.Clamp(conds.Rainfall * 2f - Math.Max(0, 2f * (weatherData.snowThresholdTemp - weatherSys.clientClimateCond.Temperature)), 0, 1);
                    targetRainVolumeLeafy = GameMath.Max(0, targetRainVolumeLeafy - roomVolumePitchLoss);

                    targetRainVolumeLeafless = Math.Max(0.3f, 1 - nearbyLeaviness) * GameMath.Clamp(conds.Rainfall * 2f - Math.Max(0, 2f * (weatherData.snowThresholdTemp - weatherSys.clientClimateCond.Temperature)), 0, 1);
                    targetRainVolumeLeafless = GameMath.Max(0, targetRainVolumeLeafless - roomVolumePitchLoss);

                    targetRainPitch = Math.Max(0.7f, 1.25f - conds.Rainfall * 0.7f);
                    targetRainPitch = Math.Max(0, targetRainPitch - roomVolumePitchLoss/4f);

                    targetTrembleVolume = GameMath.Clamp(conds.Rainfall * 1.6f - 0.8f - roomVolumePitchLoss*0.25f, 0, 1);

                    if (!rainSoundsOn && (targetRainVolumeLeafy > 0.01 || targetRainVolumeLeafless > 0.01))
                    {
                        for (int i = 0; i < rainSoundsLeafless.Length; i++) { rainSoundsLeafless[i]?.Start(); }
                        for (int i = 0; i < rainSoundsLeafy.Length; i++) { rainSoundsLeafy[i]?.Start(); }

                        lowTrembleSound?.Start();
                        rainSoundsOn = true;

                        curRainPitch = targetRainPitch;
                    }

                    if (capi.World.Player.Entity.IsEyesSubmerged()) { 
                        curRainPitch = targetRainPitch / 2;
                        targetRainVolumeLeafy *= 0.75f;
                        targetRainVolumeLeafless *= 0.75f;
                    }

                }

                if (precType == EnumPrecipitationType.Hail)
                {
                    targetHailVolume = GameMath.Clamp(conds.Rainfall * 2f - roomVolumePitchLoss, 0, 1);
                    targetHailVolume = GameMath.Max(0, targetHailVolume - roomVolumePitchLoss);

                    targetHailPitch = Math.Max(0.7f, 1.25f - conds.Rainfall * 0.7f);
                    targetHailPitch = Math.Max(0, targetHailPitch - roomVolumePitchLoss / 4f);

                    if (!hailSoundsOn && targetHailVolume > 0.01)
                    {
                        hailSound?.Start();
                        hailSoundsOn = true;
                        curHailPitch = targetHailPitch;
                    }

                }
            }

            
            curRainVolumeLeafy += (targetRainVolumeLeafy - curRainVolumeLeafy) * dt / 2;
            curRainVolumeLeafless += (targetRainVolumeLeafless - curRainVolumeLeafless) * dt / 2;

            curTrembleVolume += (targetTrembleVolume - curTrembleVolume) * dt;
            curHailVolume += (targetHailVolume - curHailVolume) * dt;

            curHailPitch += (targetHailPitch - curHailPitch) * dt;
            curRainPitch += (targetRainPitch - curRainPitch) * dt;


            


            if (rainSoundsOn)
            {
                for (int i = 0; i < rainSoundsLeafless.Length; i++)
                {
                    rainSoundsLeafless[i]?.SetVolume(curRainVolumeLeafless);
                    rainSoundsLeafless[i]?.SetPitch(curRainPitch);
                }
                for (int i = 0; i < rainSoundsLeafy.Length; i++)
                {
                    rainSoundsLeafy[i]?.SetVolume(curRainVolumeLeafy);
                    rainSoundsLeafy[i]?.SetPitch(curRainPitch);
                }

                lowTrembleSound?.SetVolume(curTrembleVolume);
            }
            if (hailSoundsOn)
            {
                hailSound?.SetVolume(curHailVolume);
                hailSound?.SetPitch(curHailPitch);
            }


            if (curRainVolumeLeafless < 0.01 && curRainVolumeLeafy < 0.01)
            {
                for (int i = 0; i < rainSoundsLeafless.Length; i++) rainSoundsLeafless[i]?.Stop();
                for (int i = 0; i < rainSoundsLeafy.Length; i++) rainSoundsLeafy[i]?.Stop();
                rainSoundsOn = false;
            }

            if (curHailVolume < 0.01)
            {
                hailSound?.Stop();
                hailSoundsOn = false;
            }




            float wstr = (1 - roomVolumePitchLoss) * weatherData.curWindSpeed.X - 0.3f;
            if (wstr > 0.03f || curWindVolumeLeafy > 0.01f || curWindVolumeLeafless > 0.01f)
            {
                if (!windSoundsOn)
                {
                    windSoundLeafy?.Start();
                    windSoundLeafless?.Start();
                    windSoundsOn = true;
                }

                float targetVolumeLeafy = nearbyLeaviness * 1.2f * wstr;
                float targetVolumeLeafless = (1 - nearbyLeaviness) * 1.2f * wstr;

                curWindVolumeLeafy += (targetVolumeLeafy - curWindVolumeLeafy) * dt;
                curWindVolumeLeafless += (targetVolumeLeafless - curWindVolumeLeafless) * dt;

                windSoundLeafy?.SetVolume(curWindVolumeLeafy);
                windSoundLeafless?.SetVolume(curWindVolumeLeafless);

            }
            else
            {
                if (windSoundsOn)
                {
                    windSoundLeafy?.Stop();
                    windSoundLeafless?.Stop();
                    windSoundsOn = false;
                }
            }

        }

        public void Dispose()
        {
            if (rainSoundsLeafless != null)
            {
                foreach (var val in rainSoundsLeafless)
                {
                    val?.Dispose();
                }
            }
            if (rainSoundsLeafy != null)
            {
                foreach (var val in rainSoundsLeafy)
                {
                    val?.Dispose();
                }
            }

            hailSound?.Dispose();
            lowTrembleSound?.Dispose();
            windSoundLeafy?.Dispose();
            windSoundLeafless?.Dispose();
        }
    }

}
