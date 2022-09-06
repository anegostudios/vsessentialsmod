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
using Vintagestory.API.Server;
using Vintagestory.API.Util;

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
            TpOffHandTransform = null;
            GroundTransform = ModelTransform.ItemDefaultGround();
        }

        internal override RegistryObjectType CreateAndPopulate(ICoreServerAPI api, AssetLocation fullcode, JObject jobject, JsonSerializer deserializer, OrderedDictionary<string, string> variant)
        {
            ItemType resolvedType = CreateResolvedType<ItemType>(api, fullcode, jobject, deserializer, variant);

            // Special code to rotate our 3D items in inventory by default, unless they expressly set GuiTransform.Rotate to false
            if (resolvedType.Shape != null && !resolvedType.Shape.VoxelizeTexture)
            {
                if (jobject["guiTransform"]?["rotate"] == null) GuiTransform.Rotate = true;
            }
            return resolvedType;
        }

        public void InitItem(IClassRegistryAPI instancer, ILogger logger, Item item, OrderedDictionary<string, string> searchReplace)
        {

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

        public Item CreateItem(ICoreServerAPI api)
        {
            Item item;

            if (api.ClassRegistry.GetItemClass(this.Class) == null)
            {
                api.Server.Logger.Error("Item with code {0} has defined an item class {1}, but no such class registered. Will ignore.", this.Code, this.Class);
                item = new Item();
            }
            else
            {
                item = api.ClassRegistry.CreateItem(this.Class);
            }


            item.Code = this.Code;
            item.VariantStrict = this.Variant;
            item.Variant = new RelaxedReadOnlyDictionary<string, string>(this.Variant);
            item.Class = this.Class;
            item.Textures = this.Textures;
            item.MaterialDensity = this.MaterialDensity;


            item.GuiTransform = this.GuiTransform?.Clone();
            item.FpHandTransform = this.FpHandTransform?.Clone();
            item.TpHandTransform = this.TpHandTransform?.Clone();
            item.TpOffHandTransform = this.TpOffHandTransform?.Clone();
            item.GroundTransform = this.GroundTransform?.Clone();

            item.LightHsv = this.LightHsv;
            item.DamagedBy = (EnumItemDamageSource[])this.DamagedBy?.Clone();
            item.MaxStackSize = this.MaxStackSize;
            if (this.Attributes != null) item.Attributes = this.Attributes;
            item.CombustibleProps = this.CombustibleProps;
            item.NutritionProps = this.NutritionProps;
            item.TransitionableProps = this.TransitionableProps;
            item.GrindingProps = this.GrindingProps;
            item.CrushingProps = this.CrushingProps;
            item.Shape = this.Shape;
            item.Tool = this.Tool;
            item.AttackPower = this.AttackPower;
            item.LiquidSelectable = this.LiquidSelectable;
            item.ToolTier = this.ToolTier;
            item.HeldSounds = this.HeldSounds?.Clone();
            item.Durability = this.Durability;
            item.Dimensions = this.Dimensions?.Clone();
            item.MiningSpeed = this.MiningSpeed;
            item.AttackRange = this.AttackRange;
            item.StorageFlags = (EnumItemStorageFlags)this.StorageFlags;
            item.RenderAlphaTest = this.RenderAlphaTest;
            item.HeldTpHitAnimation = this.HeldTpHitAnimation;
            item.HeldRightTpIdleAnimation = this.HeldRightTpIdleAnimation;
            item.HeldLeftTpIdleAnimation = this.HeldLeftTpIdleAnimation;
            item.HeldTpUseAnimation = this.HeldTpUseAnimation;
            item.CreativeInventoryStacks = this.CreativeInventoryStacks == null ? null : (CreativeTabAndStackList[])this.CreativeInventoryStacks.Clone();
            item.MatterState = this.MatterState;
            item.ParticleProperties = this.ParticleProperties;

            this.InitItem(api.ClassRegistry, api.World.Logger, item, this.Variant);

            return item;
        }
    }
}
