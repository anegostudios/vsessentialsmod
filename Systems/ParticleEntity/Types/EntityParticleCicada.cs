using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent
{
    public class EntityParticleCicada : EntityParticleGrasshopper
    {
        public override string Type => "cicada";
        float pitch;

        protected override float soundRange => 24;
        protected override float despawnDistanceSq => 24*24;

        public EntityParticleCicada(ICoreClientAPI capi, double x, double y, double z) : base(capi, x, y, z)
        {
            ColorRed = 42;
            ColorGreen = 72;
            ColorBlue = 96;
            jumpHeight = 0f;
            sound = new AssetLocation("sounds/creature/cicada");
            doubleJump = false;
            soundCoolDown = 12f + (float)rand.NextDouble() * 3f;
            pitch = (float)capi.World.Rand.NextDouble() * 0.2f + 0.85f;
            Size = 1f;
            GravityStrength = 0f;
        }

        protected override float RandomPitch()
        {
            return pitch;
        }
    }
}
