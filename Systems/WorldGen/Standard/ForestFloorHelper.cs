using Vintagestory.API.Common;

#nullable disable

namespace Vintagestory.ServerMods
{
    /// <summary>
    /// Helper class for initializing forest floor blocks in worldgen
    /// </summary>
    public static class ForestFloorHelper
    {
        private static int[] forestBlockIds;
        private static int maxStage = 8;

        /// <summary>
        /// Maximum stage for forest floor blocks
        /// </summary>
        public static int MaxStage => maxStage;

        /// <summary>
        /// Initialize forest floor block IDs. Should be called during worldgen initialization.
        /// </summary>
        public static int[] InitialiseForestBlocks(IWorldAccessor world, string blockCodePrefix = "forestfloor-", int stages = 8)
        {
            maxStage = stages;
            forestBlockIds = new int[stages];

            for (int i = 0; i < stages; i++)
            {
                var block = world.GetBlock(new AssetLocation(blockCodePrefix + i));
                forestBlockIds[i] = block?.Id ?? 0;
            }

            return forestBlockIds;
        }

        /// <summary>
        /// Get cached forest block IDs
        /// </summary>
        public static int[] GetForestBlockIds() => forestBlockIds;
    }
}
