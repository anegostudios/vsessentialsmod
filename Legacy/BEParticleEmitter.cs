using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent
{
    public class BlockEntityParticleEmitter : BlockEntity
    {
        Block block = null;
        long listenerId;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api.World is IClientWorldAccessor)
            {
                listenerId = api.Event.RegisterGameTickListener(OnGameTick, 25);
                block = api.World.BlockAccessor.GetBlock(Pos);
            }
        }

        public override void OnBlockRemoved()
        {
            Api.World.UnregisterGameTickListener(listenerId);
        }

        private void OnGameTick(float dt)
        {
            IClientWorldAccessor clientWorld = (IClientWorldAccessor)Api.World;
            if (!clientWorld.Player.Entity.Pos.InRangeOf(Pos, 128 * 128)) return;
            if (block == null || block.ParticleProperties == null) return;
            
            for (int i = 0; i < block.ParticleProperties.Length; i++)
            {
                AdvancedParticleProperties bps = block.ParticleProperties[i];

                bps.basePos.X = Pos.X + block.TopMiddlePos.X;
                bps.basePos.Y = Pos.Y + block.TopMiddlePos.Y;
                bps.basePos.Z = Pos.Z + block.TopMiddlePos.Z;

                Api.World.SpawnParticles(bps);
            }
        }
    }
}
