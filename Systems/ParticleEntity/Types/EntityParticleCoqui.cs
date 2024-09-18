using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent
{
    public class EntityParticleCoqui : EntityParticleGrasshopper
    {
        public override string Type => "coqui";

        static long lastCoquiSound = 0;

        long soundWaitMs;
        float pitch;

        public EntityParticleCoqui(ICoreClientAPI capi, double x, double y, double z) : base(capi, x, y, z)
        {
            ColorRed = 86;
            ColorGreen = 144;
            ColorBlue = 193;
            jumpHeight = 0.8f;
            sound = new AssetLocation("sounds/creature/coqui");
            doubleJump = false;
            soundCoolDown = 4f + (float)rand.NextDouble() * 3f;
            soundWaitMs = 250 + rand.Next(250);
            pitch = (float)capi.World.Rand.NextDouble() * 0.2f + 0.89f;
        }

        protected override float RandomPitch()
        {
            return pitch;
        }

        protected override bool shouldPlaySound()
        {
            bool play = rand.NextDouble() < 0.015 && capi.World.ElapsedMilliseconds - lastCoquiSound > soundWaitMs && capi.World.BlockAccessor.GetLightLevel(Position.AsBlockPos, EnumLightLevelType.TimeOfDaySunLight) < 14;
            if (play) lastCoquiSound = capi.World.ElapsedMilliseconds;
            return play;
        }
    }
}
