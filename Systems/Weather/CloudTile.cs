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
        public short GridXOffset; // Grid position
        public short GridZOffset; // Grid position


        public short NorthTileThickness;
        public short EastTileThickness;
        public short SouthTileThickness;
        public short WestTileThickness;

        public short TargetThickness;
        public short TargetBrightnes;
        public short TargetThinCloudMode;
        public short TargetUndulatingCloudMode;
        public short TargetCloudOpaquenes;

        public short SelfThickness;
        public short Brightness;
        public short ThinCloudMode;
        public short UndulatingCloudMode;
        public short CloudOpaqueness;

        public LCGRandom brightnessRand;

        internal bool rainValuesSet;
        internal float lerpRainCloudOverlay;
        internal float lerpRainOverlay;
    }
}
