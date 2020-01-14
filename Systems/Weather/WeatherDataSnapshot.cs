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
        public EnumPrecipitationType nowPrecType;

        public float nearLightningRate;
        public float distantLightningRate;
        public float lightningMinTemp;

        public ClimateCondition climateCond = new ClimateCondition();
        public Vec3f curWindSpeed = new Vec3f();

        public float snowThresholdTemp = 4f;

        public void SetLerped(WeatherDataSnapshot left, WeatherDataSnapshot right, float w)
        {
            Ambient.SetLerped(left.Ambient, right.Ambient, w);
            BlendedPrecType = w < 0.5 ? left.BlendedPrecType : right.BlendedPrecType;
            PrecIntensity = left.PrecIntensity * (1 - w) + right.PrecIntensity * w;
            PrecParticleSize = left.PrecParticleSize * (1 - w) + right.PrecParticleSize * w;
            

            nowPrecType = w < 0.5 ? left.nowPrecType : right.nowPrecType;
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
