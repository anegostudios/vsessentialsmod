using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vintagestory.ServerMods.NoObf
{
    [JsonObject(MemberSerialization.OptIn)]
    public class ItemType : CollectibleType
    {
        public ItemType()
        {
            Class = "Item";
            GuiTransform = ModelTransform.ItemDefaultGui();
            FpHandTransform = ModelTransform.ItemDefaultFp();
            TpHandTransform = ModelTransform.ItemDefaultTp();
            GroundTransform = ModelTransform.ItemDefaultGround();
        }

        [JsonProperty]
        public int Durability;
        [JsonProperty]
        public EnumItemDamageSource[] DamagedBy;
        [JsonProperty]
        public EnumTool? Tool = null;

        public void InitItem(ILogger logger, IClassRegistryAPI instancer, Item item, Dictionary<string, string> searchReplace)
        {
            if (Shape != null && !Shape.VoxelizeTexture && jsonObject["guiTransform"]?["rotate"] == null)
            {
                GuiTransform.Rotate = true;
            }

            item.CreativeInventoryTabs = BlockType.GetCreativeTabs(item.Code, CreativeInventory, searchReplace);

            List<string> toRemove = new List<string>();
            int i = 0;
            foreach (var val in Textures)
            {
                if (val.Value.Base == null)
                {
                    logger.Error("The texture definition #{0} in item with code {1} is invalid. The base property is null. Will skip.", i, item.Code);
                    toRemove.Add(val.Key);
                }
                i++;
            }

            foreach (var val in toRemove)
            {
                Textures.Remove(val);
            }
        }
    }
}
