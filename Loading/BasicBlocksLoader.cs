using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Vintagestory.ServerMods
{
    public class ModBasicBlocksLoader : ModSystem
    {
        ICoreServerAPI api;

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override double ExecuteOrder()
        {
            return 0.1;
        }


        public override void Start(ICoreAPI manager)
        {
            if (!(manager is ICoreServerAPI sapi)) return;
            api = sapi;

            #region Block types
            Block block = new Block()
            {
                Code = new AssetLocation("mantle"),
                Textures = new FastSmallDictionary<string, CompositeTexture>("all", new CompositeTexture(new AssetLocation("block/mantle"))),
                DrawType = EnumDrawType.Cube,
                MatterState = EnumMatterState.Solid,
                BlockMaterial = EnumBlockMaterial.Mantle,
                Replaceable = 0,
                Resistance = 31337,
                RequiredMiningTier = 196,
                Sounds = new BlockSounds()
                {
                    Walk = new AssetLocation("sounds/walk/stone"),
                    ByTool = new Dictionary<EnumTool, BlockSounds>()
                    {
                        { EnumTool.Pickaxe, new BlockSounds() { Hit = new AssetLocation("sounds/block/rock-hit-pickaxe"), Break = new AssetLocation("sounds/block/rock-hit-pickaxe") } }
                    }
                },
                CreativeInventoryTabs = new string[] { "general" }
            };

            api.RegisterBlock(block);

            #endregion

        }

    }
}
