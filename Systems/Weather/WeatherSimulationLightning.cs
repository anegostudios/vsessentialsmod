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

    public class WeatherSimulationLightning
    {
        WeatherSystemClient weatherSys;
        ICoreClientAPI capi;

        float lightningTime;
        float lightningIntensity;
        public AmbientModifier LightningAmbient;
        public AmbientModifier actualSunGlowAmb = new AmbientModifier().EnsurePopulated();
        float nearLightningCoolDown = 0f;


        public WeatherSimulationLightning(ICoreClientAPI capi, WeatherSystemClient weatherSys)
        {
            this.weatherSys = weatherSys;
            this.capi = capi;
            LightningAmbient = new AmbientModifier().EnsurePopulated();

            capi.Ambient.CurrentModifiers["lightningambient"] = LightningAmbient;
        }

        public void ClientTick(float dt) {

            Random rnd = capi.World.Rand;
            WeatherDataSnapshot weatherData = weatherSys.BlendedWeatherData;

            if (weatherSys.clientClimateCond.Temperature >= weatherData.lightningMinTemp)
            {
                float deepnessSub = GameMath.Clamp(1 - (float)capi.World.Player.Entity.Pos.Y / capi.World.SeaLevel, 0, 1);

                double rndval = capi.World.Rand.NextDouble();
                rndval -= weatherData.distantLightningRate * weatherSys.clientClimateCond.RainCloudOverlay;
                if (rndval <= 0)
                {
                    lightningTime = 0.07f + (float)rnd.NextDouble() * 0.17f;
                    lightningIntensity = 0.25f + (float)rnd.NextDouble();



                    float pitch = GameMath.Clamp((float)rnd.NextDouble() * 0.3f + lightningTime / 2 + lightningIntensity / 2 - deepnessSub / 2, 0.6f, 1.15f);
                    float volume = GameMath.Clamp(Math.Min(1, 0.25f + lightningTime + lightningIntensity / 2) - 2f * deepnessSub, 0, 1);
                    
                    capi.World.PlaySoundAt(new AssetLocation("sounds/weather/lightning-distant.ogg"), 0, 0, 0, null, EnumSoundType.Ambient, pitch, 32, volume);
                }
                else if (nearLightningCoolDown <= 0)
                {
                    rndval -= weatherData.nearLightningRate * weatherSys.clientClimateCond.RainCloudOverlay;
                    if (rndval <= 0)
                    {
                        lightningTime = 0.07f + (float)rnd.NextDouble() * 0.17f;
                        lightningIntensity = 1 + (float)rnd.NextDouble() * 0.9f;
                        
                        float pitch = GameMath.Clamp(0.75f + (float)rnd.NextDouble() * 0.3f - deepnessSub/2, 0.5f, 1.2f);
                        float volume = GameMath.Clamp(0.5f + (float)rnd.NextDouble() * 0.5f - 2f * deepnessSub, 0, 1);
                        AssetLocation loc;

                        if (rnd.NextDouble() > 0.25)
                        {
                            loc = new AssetLocation("sounds/weather/lightning-near.ogg");
                            nearLightningCoolDown = 5;
                        }
                        else
                        {
                            loc = new AssetLocation("sounds/weather/lightning-verynear.ogg");
                            nearLightningCoolDown = 10;
                        }

                        
                        capi.World.PlaySoundAt(loc, 0, 0, 0, null, EnumSoundType.Ambient, pitch, 32, volume);
                    }
                }
            }
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (stage == EnumRenderStage.Done)
            {
                AmbientModifier sunGlowAmb = capi.Ambient.CurrentModifiers["sunglow"];
                actualSunGlowAmb.FogColor.Weight = sunGlowAmb.FogColor.Weight;
                //actualSunGlowAmb.AmbientColor.Weight = sunGlowAmb.AmbientColor.Weight;

                dt = Math.Min(0.5f, dt);

                if (nearLightningCoolDown > 0)
                {
                    nearLightningCoolDown -= dt;
                }

                return;
            }

            if (lightningTime > 0)
            {
                float mul = Math.Min(10 * lightningIntensity * lightningTime, 1.5f);

                WeatherDataSnapshot weatherData = weatherSys.BlendedWeatherData;

                LightningAmbient.CloudBrightness.Value = Math.Max(weatherData.Ambient.SceneBrightness.Value, mul);
                LightningAmbient.FogBrightness.Value = Math.Max(weatherData.Ambient.FogBrightness.Value, mul);

                LightningAmbient.CloudBrightness.Weight = Math.Min(1, mul);
                LightningAmbient.FogBrightness.Weight = Math.Min(1, mul);

                float sceneBrightIncrease = GameMath.Min(mul, GameMath.Max(0, lightningIntensity - 0.75f));

                if (sceneBrightIncrease > 0)
                {
                    LightningAmbient.SceneBrightness.Weight = Math.Min(1, sceneBrightIncrease);
                    LightningAmbient.SceneBrightness.Value = 1;

                    AmbientModifier sunGlowAmb = capi.Ambient.CurrentModifiers["sunglow"];

                    float nowWeight = GameMath.Clamp(1 - sceneBrightIncrease, 0, 1);

                    sunGlowAmb.FogColor.Weight = Math.Min(sunGlowAmb.FogColor.Weight, nowWeight);
                    sunGlowAmb.AmbientColor.Weight = Math.Min(sunGlowAmb.AmbientColor.Weight, nowWeight);
                }


                lightningTime -= dt / 1.7f;

                if (lightningTime <= 0)
                {
                    // Restore previous values
                    AmbientModifier sunGlowAmb = capi.Ambient.CurrentModifiers["sunglow"];
                    sunGlowAmb.FogColor.Weight = actualSunGlowAmb.FogColor.Weight;
                    sunGlowAmb.AmbientColor.Weight = actualSunGlowAmb.AmbientColor.Weight;

                    LightningAmbient.CloudBrightness.Weight = 0;
                    LightningAmbient.FogBrightness.Weight = 0;
                    LightningAmbient.SceneBrightness.Weight = 0;
                }
            }
        }


    }

}
