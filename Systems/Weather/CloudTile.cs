using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class CloudTile
    {
        public byte MaxDensity;
        public byte SelfDensity;

        public byte NorthDensity;
        public byte EastDensity;
        public byte SouthDensity;
        public byte WestDensity;
        public byte Brightness;
        
        public int XOffset; // Grid position
        public float YOffset;
        public int ZOffset; // Grid position

        public LCGRandom brightnessRand;
    }
}
