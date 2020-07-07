using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class BehaviorClearSnowAccumOnRemove : BlockBehavior
    {
        public BehaviorClearSnowAccumOnRemove(Block block) : base(block)
        {
        }


        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            if (world.Side == EnumAppSide.Server)
            {
                int chunksize = world.BlockAccessor.ChunkSize;
                IServerMapChunk mc = (world.Api as ICoreServerAPI).WorldManager.GetMapChunk(pos.X / chunksize, pos.Z / chunksize);
                mc.SnowAccum.TryRemove(pos, out _);
            }
            
        }
    }
}
