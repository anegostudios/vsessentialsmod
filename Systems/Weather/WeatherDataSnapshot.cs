using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class WeatherDataSnapshot
    {
        public AmbientModifier Ambient = new AmbientModifier().EnsurePopulated();
        public EnumPrecipitationType BlendedPrecType;
        public float PrecIntensity;
        public float PrecParticleSize;
        //public EnumPrecipitationType nowPrecType;

        public float nearLightningRate;
        public float distantLightningRate;
        public float lightningMinTemp;

        public ClimateCondition climateCond = new ClimateCondition();
        public Vec3f curWindSpeed = new Vec3f();

        public float snowThresholdTemp = 4f;

        public void SetAmbientLerped(WeatherPattern left, WeatherPattern right, float w, float addFogDensity = 0)
        {
            // 1-(1.1-x)^4
            // http://fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIxLSgxLjEteCleNCIsImNvbG9yIjoiIzAwMDAwMCJ9LHsidHlwZSI6MTAwMCwid2luZG93IjpbIjAiLCIxIiwiMCIsIjEiXX1d
            float drynessMultiplier = GameMath.Clamp(1 - (float)Math.Pow(1.1 - climateCond.Rainfall, 4), 0, 1);
            float fogMultiplier = drynessMultiplier;


            Ambient.FlatFogDensity.Value = (right.State.nowMistDensity * w + left.State.nowMistDensity * (1 - w)) / 250f;
            Ambient.FlatFogDensity.Weight = 1;
            Ambient.FlatFogDensity.Weight *= fogMultiplier;

            Ambient.FlatFogYPos.Value = right.State.nowMistYPos * w + left.State.nowMistYPos * (1 - w);
            Ambient.FlatFogYPos.Weight = 1;

            Ambient.FogDensity.Value = (addFogDensity + right.State.nowFogDensity * w + left.State.nowFogDensity * (1 - w)) / 1000f;
            Ambient.FogDensity.Weight = fogMultiplier;

            Ambient.CloudBrightness.Value = right.State.nowCloudBrightness * w + left.State.nowCloudBrightness * (1 - w);
            Ambient.CloudBrightness.Weight = 1;

            //if (Weight > 0.5) weatherData.BlendedPrecType = NewWePattern.State.nowPrecType;
            //else weatherData.BlendedPrecType = OldWePattern.State.nowPrecType;

            /*weatherData.nowPrecType = weatherData.BlendedPrecType;
            if (weatherData.nowPrecType == EnumPrecipitationType.Auto)
            {
                weatherData.nowPrecType = weatherData.climateCond.Temperature < weatherData.snowThresholdTemp ? EnumPrecipitationType.Snow : EnumPrecipitationType.Rain;
            }*/

            PrecParticleSize = right.State.nowPrecParticleSize * w + left.State.nowPrecParticleSize * (1 - w);
            PrecIntensity = drynessMultiplier * (right.State.nowPrecIntensity * w + left.State.nowPrecIntensity * (1 - w));

            Ambient.CloudDensity.Value = right.State.nowbaseThickness * w + left.State.nowbaseThickness * (1 - w);
            Ambient.CloudDensity.Weight = 1;

            Ambient.SceneBrightness.Value = right.State.nowSceneBrightness * w + left.State.nowSceneBrightness * (1 - w);
            Ambient.SceneBrightness.Weight = 1f;

            Ambient.FogBrightness.Value = right.State.nowFogBrightness * w + left.State.nowFogBrightness * (1 - w);
            Ambient.FogBrightness.Weight = 1f;
        }

        public void SetAmbient(WeatherPattern left, float addFogDensity = 0)
        {
            // 1-(1.1-x)^4
            // http://fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIxLSgxLjEteCleNCIsImNvbG9yIjoiIzAwMDAwMCJ9LHsidHlwZSI6MTAwMCwid2luZG93IjpbIjAiLCIxIiwiMCIsIjEiXX1d
            float drynessMultiplier = GameMath.Clamp(1 - (float)Math.Pow(1.1 - climateCond.Rainfall, 4), 0, 1);
            float fogMultiplier = drynessMultiplier;


            Ambient.FlatFogDensity.Value = left.State.nowMistDensity / 250f;
            Ambient.FlatFogDensity.Weight = 1;
            Ambient.FlatFogDensity.Weight *= fogMultiplier;

            Ambient.FlatFogYPos.Value = left.State.nowMistYPos;
            Ambient.FlatFogYPos.Weight = 1;

            Ambient.FogDensity.Value = (addFogDensity + left.State.nowFogDensity) / 1000f;
            Ambient.FogDensity.Weight = fogMultiplier;

            Ambient.CloudBrightness.Value = left.State.nowCloudBrightness;
            Ambient.CloudBrightness.Weight = 1;

            PrecParticleSize = left.State.nowPrecParticleSize;
            PrecIntensity = drynessMultiplier * left.State.nowPrecIntensity;

            Ambient.CloudDensity.Value = left.State.nowbaseThickness;
            Ambient.CloudDensity.Weight = 1;

            Ambient.SceneBrightness.Value = left.State.nowSceneBrightness;
            Ambient.SceneBrightness.Weight = 1f;

            Ambient.FogBrightness.Value = left.State.nowFogBrightness;
            Ambient.FogBrightness.Weight = 1f;
        }

        public void SetLerped(WeatherDataSnapshot left, WeatherDataSnapshot right, float w)
        {
            Ambient.SetLerped(left.Ambient, right.Ambient, w);
            BlendedPrecType = w < 0.5 ? left.BlendedPrecType : right.BlendedPrecType;
            PrecIntensity = left.PrecIntensity * (1 - w) + right.PrecIntensity * w;
            PrecParticleSize = left.PrecParticleSize * (1 - w) + right.PrecParticleSize * w;
            //nowPrecType = w < 0.5 ? left.nowPrecType : right.nowPrecType;

            nearLightningRate = left.nearLightningRate * (1 - w) + right.nearLightningRate * w;
            distantLightningRate = left.distantLightningRate * (1 - w) + right.distantLightningRate * w;
            lightningMinTemp = left.lightningMinTemp * (1 - w) + right.lightningMinTemp * w;
            climateCond.SetLerped(left.climateCond, right.climateCond, w);
            curWindSpeed.X = left.curWindSpeed.X * (1 - w) + right.curWindSpeed.X * w;
            snowThresholdTemp = left.snowThresholdTemp * (1 - w) + right.snowThresholdTemp * w;
        }


        public void BiLerp(WeatherDataSnapshot topLeft, WeatherDataSnapshot topRight, WeatherDataSnapshot botLeft, WeatherDataSnapshot botRight, float lerpleftRight, float lerptopBot)
        {
            WeatherDataSnapshot top = new WeatherDataSnapshot();
            top.SetLerped(topLeft, topRight, lerpleftRight);

            WeatherDataSnapshot bot = new WeatherDataSnapshot();
            bot.SetLerped(botLeft, botRight, lerptopBot);

            SetLerped(top, bot, lerptopBot);
        }
    }

}
