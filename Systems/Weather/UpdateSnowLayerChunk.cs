using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class UpdateSnowLayerChunk : IEquatable<UpdateSnowLayerChunk>
    {
        public Vec2i Coords;
        public double LastSnowAccumUpdateTotalHours;
        public Dictionary<BlockPos, BlockIdAndSnowLevel> SetBlocks = new Dictionary<BlockPos, BlockIdAndSnowLevel>();

        public bool Equals(UpdateSnowLayerChunk other)
        {
            return other.Coords.Equals(Coords);
        }

        public override bool Equals(object obj)
        {
            Vec2i pos;
            if (obj is UpdateSnowLayerChunk uplc)
            {
                pos = uplc.Coords;
            }
            else
            {
                return false;
            }

            return Coords.X == pos.X && Coords.Y == pos.Y;
        }

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 23 + Coords.X.GetHashCode();
            hash = hash * 23 + Coords.Y.GetHashCode();
            return hash;
        }
    }

    public struct BlockIdAndSnowLevel
    {
        public Block Block;
        public float SnowLevel;

        public BlockIdAndSnowLevel(Block block, float snowLevel)
        {
            Block = block;
            SnowLevel = snowLevel;
        }
    }

}
