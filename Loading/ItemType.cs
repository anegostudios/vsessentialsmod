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


        public void InitItem(IClassRegistryAPI instancer, ILogger logger, Item item, OrderedDictionary<string, string> searchReplace)
        {
            if (Shape != null && !Shape.VoxelizeTexture && jsonObject["guiTransform"]?["rotate"] == null)
            {
                GuiTransform.Rotate = true;
            }

            item.CreativeInventoryTabs = BlockType.GetCreativeTabs(item.Code, CreativeInventory, searchReplace);


            CollectibleBehaviorType[] behaviorTypes = Behaviors;

            if (behaviorTypes != null)
            {
                List<CollectibleBehavior> collbehaviors = new List<CollectibleBehavior>();

                for (int i = 0; i < behaviorTypes.Length; i++)
                {
                    CollectibleBehaviorType behaviorType = behaviorTypes[i];
                    CollectibleBehavior behavior;

                    if (instancer.GetCollectibleBehaviorClass(behaviorType.name) != null)
                    {
                        behavior = instancer.CreateCollectibleBehavior(item, behaviorType.name);
                    } else 
                    { 
                        logger.Warning(Lang.Get("Collectible behavior {0} for item {1} not found", behaviorType.name, item.Code));
                        continue;
                    }

                    if (behaviorType.properties == null) behaviorType.properties = new JsonObject(new JObject());

                    try
                    {
                        behavior.Initialize(behaviorType.properties);
                    }
                    catch (Exception e)
                    {
                        logger.Error("Failed calling Initialize() on collectible behavior {0} for item {1}, using properties {2}. Will continue anyway. Exception: {3}", behaviorType.name, item.Code, behaviorType.properties.ToString(), e);
                    }

                    collbehaviors.Add(behavior);
                }

                item.CollectibleBehaviors = collbehaviors.ToArray();
            }
        }
    }
}
