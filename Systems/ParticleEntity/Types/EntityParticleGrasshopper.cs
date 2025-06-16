using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{

    public class EntityParticleGrasshopper : EntityParticleInsect
    {
        public override string Type => "grassHopper";

        public EntityParticleGrasshopper(ICoreClientAPI capi, double x, double y, double z) : base(capi, x,y , z) {
            var block = capi.World.BlockAccessor.GetBlock((int)x, (int)y, (int)z);
            if (block.BlockMaterial == EnumBlockMaterial.Plant)
            {
                var col = block.GetColor(capi, new BlockPos((int)x, (int)y, (int)z));
                ColorRed = (byte)((col >> 16) & 0xff);
                ColorGreen = (byte)((col >> 8) & 0xff);
                ColorBlue = (byte)((col >> 0) & 0xff);
            }
            else
            {
                ColorRed = 31;
                ColorGreen = 178;
                ColorBlue = 144;
            }

            sound = new AssetLocation("sounds/creature/grasshopper");
        }

        protected override bool shouldPlaySound()
        {
            if (rand.NextDouble() < 0.01 && capi.World.BlockAccessor.GetLightLevel(Position.AsBlockPos, EnumLightLevelType.TimeOfDaySunLight) > 7)
            {
                var season = capi.World.Calendar.GetSeasonRel(Position.AsBlockPos);

                // 3 times less often outside the summer season
                if ((season > 0.48 && season < 0.63) || rand.NextDouble() < 0.33)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
