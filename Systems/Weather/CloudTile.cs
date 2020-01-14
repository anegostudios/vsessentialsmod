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
        public short XOffset; // Grid position
        public short ZOffset; // Grid position


        public short NorthThickness;
        public short EastThickness;
        public short SouthThickness;
        public short WestThickness;

        public short SelfThickness;
        public short Brightness;


        public LCGRandom brightnessRand;
        public short MaxThickness;
    }
}
