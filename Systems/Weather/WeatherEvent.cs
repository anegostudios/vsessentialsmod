using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class WeatherEvent
    {
        public string Code;

        public bool HailMode;

        public LightningConfig Lightning;
    }
}
