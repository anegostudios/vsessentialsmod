using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods.NoObf;

namespace Vintagestory.ServerMods.NoObf
{
    public class BlockVariant
    {
        public string Code;

        public List<string> Codes;
        public List<string> Types;
    }

    public class ResolvedVariant
    {
        public OrderedDictionary<string, string> CodeParts = new OrderedDictionary<string, string>();

        public AssetLocation Code;

        internal void ResolveCode(AssetLocation baseCode)
        {
            Code = baseCode.Clone();
            foreach (string code in CodeParts.Values)
            {
                Code.Path += "-" + code;
            }
        }
    }


    public class ModRegistryObjectTypeLoader : ModSystem
    {
        // Dict Key is filename (with .json)
        Dictionary<AssetLocation, StandardWorldProperty> worldProperties;
        Dictionary<AssetLocation, BlockVariant[]> worldPropertiesVariants;

        Dictionary<AssetLocation, Shape> blockShapes;
        Dictionary<AssetLocation, BlockType> blockTypes;
        Dictionary<AssetLocation, ItemType> itemTypes;
        Dictionary<AssetLocation, EntityType> entityTypes;


        ICoreServerAPI api;



        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }


        public override double ExecuteOrder()
        {
            return 0.2;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;

            LoadWorldProperties();



            blockShapes = api.Assets.GetMany<Shape>(api.Server.Logger, "shapes/block/");

            blockTypes = new Dictionary<AssetLocation, BlockType>();
            foreach (KeyValuePair<AssetLocation, JObject> entry in api.Assets.GetMany<JObject>(api.Server.Logger, "blocktypes/"))
            {
                JToken property = null;
                JObject blockTypeObject = entry.Value;
                AssetLocation location = null;
                try
                {
                    location = blockTypeObject.GetValue("code").ToObject<AssetLocation>();
                    location.Domain = entry.Key.Domain;
                }
                catch (Exception e)
                {
                    api.World.Logger.Error("Block type {0} has no valid code property. Will ignore. Exception thrown: {1}", entry.Key, e);
                    continue;
                }


                blockTypes.Add(entry.Key, new BlockType()
                {
                    Code = location,
                    VariantGroups = blockTypeObject.TryGetValue("variantgroups", out property) ? property.ToObject<RegistryObjectVariantGroup[]>() : null,
                    SkipVariants = blockTypeObject.TryGetValue("skipVariants", out property) ? property.ToObject<AssetLocation[]>() : null,
                    AllowedVariants = blockTypeObject.TryGetValue("allowedVariants", out property) ? property.ToObject<AssetLocation[]>() : null,
                    Enabled = blockTypeObject.TryGetValue("enabled", out property) ? property.ToObject<bool>() : true,
                    jsonObject = blockTypeObject
                });
            }

            itemTypes = new Dictionary<AssetLocation, ItemType>();
            foreach (KeyValuePair<AssetLocation, JObject> entry in api.Assets.GetMany<JObject>(api.Server.Logger, "itemtypes/"))
            {
                JToken property = null;
                JObject itemTypeObject = entry.Value;

                AssetLocation location = itemTypeObject.GetValue("code").ToObject<AssetLocation>();
                location.Domain = entry.Key.Domain;

                itemTypes.Add(entry.Key, new ItemType()
                {
                    Code = location,
                    VariantGroups = itemTypeObject.TryGetValue("variantgroups", out property) ? property.ToObject<RegistryObjectVariantGroup[]>() : null,
                    SkipVariants = itemTypeObject.TryGetValue("skipVariants", out property) ? property.ToObject<AssetLocation[]>() : null,
                    AllowedVariants = itemTypeObject.TryGetValue("allowedVariants", out property) ? property.ToObject<AssetLocation[]>() : null,
                    Enabled = itemTypeObject.TryGetValue("enabled", out property) ? property.ToObject<bool>() : true,
                    jsonObject = itemTypeObject
                });
            }

            entityTypes = new Dictionary<AssetLocation, EntityType>();
            foreach (KeyValuePair<AssetLocation, JObject> entry in api.Assets.GetMany<JObject>(api.Server.Logger, "entities/"))
            {
                JToken property = null;
                JObject entityTypeObject = entry.Value;
                AssetLocation location = null;
                try
                {
                    location = entityTypeObject.GetValue("code").ToObject<AssetLocation>();
                    location.Domain = entry.Key.Domain;
                }
                catch (Exception e)
                {
                    api.World.Logger.Error("Entity type {0} has no valid code property. Will ignore. Exception thrown: {1}", entry.Key, e);
                    continue;
                }

                try
                {
                    entityTypes.Add(entry.Key, new EntityType()
                    {
                        Code = location,
                        VariantGroups = entityTypeObject.TryGetValue("variantgroups", out property) ? property.ToObject<RegistryObjectVariantGroup[]>() : null,
                        SkipVariants = entityTypeObject.TryGetValue("skipVariants", out property) ? property.ToObject<AssetLocation[]>() : null,
                        AllowedVariants = entityTypeObject.TryGetValue("allowedVariants", out property) ? property.ToObject<AssetLocation[]>() : null,
                        Enabled = entityTypeObject.TryGetValue("enabled", out property) ? property.ToObject<bool>() : true,
                        jsonObject = entityTypeObject
                    });
                } catch (Exception e)
                {
                    api.World.Logger.Error("Entity type {0} could not be loaded. Will ignore. Exception thrown: {1}", entry.Key, e);
                    continue;
                }
            }

            
            LoadEntities();
            LoadItems();
            LoadBlocks();

            api.Server.LogNotification("BlockLoader: Entities, Blocks and Items loaded");

            FreeRam();
        }

        private void LoadWorldProperties()
        {
            worldProperties = new Dictionary<AssetLocation, StandardWorldProperty>();

            foreach (var entry in api.Assets.GetMany<StandardWorldProperty>(api.Server.Logger, "worldproperties/"))
            {
                AssetLocation loc = entry.Key.Clone();
                loc.Path = loc.Path.Replace("worldproperties/", "");
                loc.RemoveEnding();
                
                entry.Value.Code.Domain = entry.Key.Domain;

                worldProperties.Add(loc, entry.Value);
            }


            worldPropertiesVariants = new Dictionary<AssetLocation, BlockVariant[]>();
            foreach (var val in worldProperties)
            {
                if (val.Value == null) continue;

                WorldPropertyVariant[] variants = val.Value.Variants;
                if (variants == null) continue;

                if (val.Value.Code == null)
                {
                    api.Server.LogError("Error in worldproperties {0}, code is null, so I won't load it", val.Key);
                    return;
                }

                worldPropertiesVariants[val.Value.Code] = new BlockVariant[variants.Length];

                for (int i = 0; i < variants.Length; i++)
                {
                    worldPropertiesVariants[val.Value.Code][i] = new BlockVariant() { Code = variants[i].Code.Path };
                }
            }
        }


        #region Entities
        void LoadEntities()
        {
            List<EntityType> entities = new List<EntityType>();

            foreach (var val in entityTypes)
            {
                if (!val.Value.Enabled) continue;

                foreach (EntityType type in GatherEntities(val.Key, val.Value))
                {
                    api.RegisterEntityClass(type.Class, type.CreateProperties());
                }
            }
        }


        List<EntityType> GatherEntities(AssetLocation location, EntityType entityType)
        {
            List<EntityType> entities = new List<EntityType>();

            List<ResolvedVariant> variants = null;
            try
            {
                variants = GatherVariants(entityType.Code, entityType.VariantGroups, entityType.Code, entityType.AllowedVariants, entityType.SkipVariants);
            }
            catch (Exception e)
            {
                api.Server.Logger.Error("Exception thrown while trying to gather all variants of the item type with code {0}. Will ignore most itemtype completly. Exception: {1}", entityType.Code, e);
                return new List<EntityType>();
            }


            // Single item type
            if (variants.Count == 0)
                entities.Add(baseEntityFromEntityType(entityType, entityType.Code.Clone(), new OrderedDictionary<string, string>()));
            else
            {
                // Multi item type
                foreach (ResolvedVariant variant in variants)
                {
                    EntityType typedEntity = baseEntityFromEntityType(entityType, variant.Code, variant.CodeParts);

                    entities.Add(typedEntity);
                }
            }

            entityType.jsonObject = null;
            return entities;
        }


        EntityType baseEntityFromEntityType(EntityType entityType, AssetLocation fullcode, OrderedDictionary<string, string> variant)
        {
            EntityType newEntityType = new EntityType()
            {
                Code = fullcode,
                VariantGroups = entityType.VariantGroups,
                Enabled = entityType.Enabled,
                jsonObject = entityType.jsonObject.DeepClone() as JObject,
                Variant = new OrderedDictionary<string, string>(variant)
            };

            solveByType(newEntityType.jsonObject, fullcode.Path, variant);

            try
            {
                JsonUtil.PopulateObject(newEntityType, newEntityType.jsonObject.ToString(), fullcode.Domain);
            }
            catch (Exception e)
            {
                api.Server.Logger.Error("Exception thrown while trying to populate/load json data of the typed item with code {0}. Will ignore most of the attributes. Exception: {1}", newEntityType.Code, e);
            }

            newEntityType.jsonObject = null;
            return newEntityType;
        }
        #endregion

        #region Items
        void LoadItems()
        {
            List<Item> items = new List<Item>();

            foreach (var val in itemTypes)
            {
                if (!val.Value.Enabled) continue;
                GatherItems(val.Key, val.Value, items);
            }

            foreach (Item item in items)
            {
                try
                {
                    api.RegisterItem(item);
                }
                catch (Exception e)
                {
                    api.Server.LogError("Failed registering item {0}: {1}", item.Code, e);
                }

            }

        }


        void GatherItems(AssetLocation location, ItemType itemType, List<Item> items)
        {
            List<ResolvedVariant> variants = null;
            try
            {
                variants = GatherVariants(itemType.Code, itemType.VariantGroups, itemType.Code, itemType.AllowedVariants, itemType.SkipVariants);
            }
            catch (Exception e)
            {
                api.Server.Logger.Error("Exception thrown while trying to gather all variants of the item type with code {0}. Will ignore most itemtype completly. Exception: {1}", itemType.Code, e);
                return;
            }


            // Single item type
            if (variants.Count == 0)
            {
                Item item = baseItemFromItemType(itemType, itemType.Code.Clone(), new OrderedDictionary<string, string>());
                items.Add(item);
            }
            else
            {
                // Multi item type
                foreach (ResolvedVariant variant in variants)
                {
                    Item typedItem = baseItemFromItemType(itemType, variant.Code, variant.CodeParts);

                    items.Add(typedItem);
                }
            }

            itemType.jsonObject = null;
        }


        Item baseItemFromItemType(ItemType itemType, AssetLocation fullcode, OrderedDictionary<string, string> variant)
        {
            ItemType typedItemType = new ItemType()
            {
                Code = itemType.Code,
                VariantGroups = itemType.VariantGroups,
                Variant = new OrderedDictionary<string, string>(variant),
                Enabled = itemType.Enabled,
                jsonObject = itemType.jsonObject.DeepClone() as JObject
            };

            solveByType(typedItemType.jsonObject, fullcode.Path, variant);

            try
            {
                JsonUtil.PopulateObject(typedItemType, typedItemType.jsonObject.ToString(), fullcode.Domain);
            }
            catch (Exception e)
            {
                api.Server.Logger.Error("Exception thrown while trying to populate/load json data of the typed item with code {0}. Will ignore most of the attributes. Exception: {1}", typedItemType.Code, e);
            }

            Item item;

            if (api.ClassRegistry.GetItemClass(typedItemType.Class) == null)
            {
                api.Server.Logger.Error("Item with code {0} has defined an item class {1}, but no such class registered. Will ignore.", typedItemType.Code, typedItemType.Class);
                item = new Item();
            }
            else
            {
                item = api.ClassRegistry.CreateItem(typedItemType.Class);
            }


            item.Code = fullcode;
            item.Variant = typedItemType.Variant;
            item.Class = typedItemType.Class;
            item.Textures = typedItemType.Textures;
            item.MaterialDensity = typedItemType.MaterialDensity;
            item.GuiTransform = typedItemType.GuiTransform;
            item.FpHandTransform = typedItemType.FpHandTransform;
            item.TpHandTransform = typedItemType.TpHandTransform;
            item.GroundTransform = typedItemType.GroundTransform;
            item.DamagedBy = (EnumItemDamageSource[])typedItemType.DamagedBy?.Clone();
            item.MaxStackSize = typedItemType.MaxStackSize;
            if (typedItemType.Attributes != null) item.Attributes = typedItemType.Attributes;
            item.CombustibleProps = typedItemType.CombustibleProps;
            item.NutritionProps = typedItemType.NutritionProps;
            item.TransitionableProps = typedItemType.TransitionableProps;
            item.GrindingProps = typedItemType.GrindingProps;
            item.Shape = typedItemType.Shape;
            item.Tool = typedItemType.Tool;
            item.AttackPower = typedItemType.AttackPower;
            item.LiquidSelectable = typedItemType.LiquidSelectable;
            item.MiningTier = typedItemType.MiningTier;
            item.Durability = typedItemType.Durability;
            item.MiningSpeed = typedItemType.MiningSpeed;
            item.AttackRange = typedItemType.AttackRange;
            item.StorageFlags = (EnumItemStorageFlags)typedItemType.StorageFlags;
            item.RenderAlphaTest = typedItemType.RenderAlphaTest;
            item.HeldTpHitAnimation = typedItemType.HeldTpHitAnimation;
            item.HeldRightTpIdleAnimation = typedItemType.HeldRightTpIdleAnimation;
            item.HeldLeftTpIdleAnimation = typedItemType.HeldLeftTpIdleAnimation;
            item.HeldTpUseAnimation = typedItemType.HeldTpUseAnimation;
            item.CreativeInventoryStacks = typedItemType.CreativeInventoryStacks == null ? null : (CreativeTabAndStackList[])typedItemType.CreativeInventoryStacks.Clone();
            item.MatterState = typedItemType.MatterState;

            typedItemType.InitItem(api.World.Logger, api.ClassRegistry, item, variant);

            typedItemType.jsonObject = null;


            return item;
        }
        #endregion

        #region Blocks
        void LoadBlocks()
        {
            List<Block> blocks = new List<Block>();

            foreach (var val in blockTypes)
            {
                if (!val.Value.Enabled) continue;
                GatherBlocks(val.Value.Code, val.Value, blocks);
            }

            foreach (Block block in blocks)
            {
                try
                {
                    api.RegisterBlock(block);
                }
                catch (Exception e)
                {
                    api.Server.LogError("Failed registering block {0}: {1}", block.Code, e);
                }
            }
        }

        void GatherBlocks(AssetLocation location, BlockType blockType, List<Block> blocks)
        {
            List<ResolvedVariant> variants = null;
            try
            {
                variants = GatherVariants(blockType.Code, blockType.VariantGroups, location, blockType.AllowedVariants, blockType.SkipVariants);
            }
            catch (Exception e)
            {
                api.Server.Logger.Error("Exception thrown while trying to gather all variants of the block type with code {0}. Will ignore most itemtype completly. Exception: {1}", blockType.Code, e);
                return;
            }


            // Single block type
            if (variants.Count == 0)
            {
                Block block = baseBlockFromBlockType(blockType, blockType.Code.Clone(), new OrderedDictionary<string, string>());
                blocks.Add(block);
            }
            else
            {
                foreach (ResolvedVariant variant in variants)
                {
                    Block block = baseBlockFromBlockType(blockType, variant.Code, variant.CodeParts);

                    blocks.Add(block);
                }
            }

            blockType.jsonObject = null;
        }


        Block baseBlockFromBlockType(BlockType blockType, AssetLocation fullcode, OrderedDictionary<string, string> variant)
        {
            BlockType typedBlockType = new BlockType()
            {
                Code = blockType.Code,
                VariantGroups = blockType.VariantGroups,
                Variant = new OrderedDictionary<string, string>(variant),
                Enabled = blockType.Enabled,
                jsonObject = blockType.jsonObject.DeepClone() as JObject
            };

            try
            {
                solveByType(typedBlockType.jsonObject, fullcode.Path, variant);
            }
            catch (Exception e)
            {
                api.Server.Logger.Error("Exception thrown while trying to resolve *byType properties of typed block {0}. Will ignore most of the attributes. Exception thrown: {1}", typedBlockType.Code, e);
            }

            try
            {
                JsonUtil.PopulateObject(typedBlockType, typedBlockType.jsonObject.ToString(), fullcode.Domain);
            }
            catch (Exception e)
            {
                api.Server.Logger.Error("Exception thrown while trying to populate/load json data of the typed block {0}. Will ignore most of the attributes. Exception thrown: {1}", typedBlockType.Code, e);
            }

            typedBlockType.jsonObject = null;
            Block block;

            if (api.ClassRegistry.GetBlockClass(typedBlockType.Class) == null)
            {
                api.Server.Logger.Error("Block with code {0} has defined a block class {1}, no such class registered. Will ignore.", typedBlockType.Code, typedBlockType.Class);
                block = new Block();
            }
            else
            {
                block = api.ClassRegistry.CreateBlock(typedBlockType.Class);
            }


            if (typedBlockType.EntityClass != null)
            {
                if (api.ClassRegistry.GetBlockEntity(typedBlockType.EntityClass) != null)
                {
                    block.EntityClass = typedBlockType.EntityClass;
                }
                else
                {
                    api.Server.Logger.Error("Block with code {0} has defined a block entity class {1}, no such class registered. Will ignore.", typedBlockType.Code, typedBlockType.EntityClass);
                }
            }



            block.Code = fullcode;
            block.Variant = typedBlockType.Variant;
            block.Class = typedBlockType.Class;
            block.LiquidSelectable = typedBlockType.LiquidSelectable;
            block.LiquidCode = typedBlockType.LiquidCode;
            block.BlockEntityBehaviors = (BlockEntityBehaviorType[])typedBlockType.EntityBehaviors?.Clone() ?? new BlockEntityBehaviorType[0];
            block.WalkSpeedMultiplier = typedBlockType.WalkspeedMultiplier;
            block.DragMultiplier = typedBlockType.DragMultiplier;
            block.Durability = typedBlockType.Durability;
            block.DamagedBy = (EnumItemDamageSource[])typedBlockType.DamagedBy?.Clone();
            block.Tool = typedBlockType.Tool;
            block.DrawType = typedBlockType.DrawType;
            block.Replaceable = typedBlockType.Replaceable;
            block.Fertility = typedBlockType.Fertility;
            block.LightAbsorption = typedBlockType.LightAbsorption;
            block.LightHsv = typedBlockType.LightHsv;
            block.VertexFlags = typedBlockType.VertexFlags?.Clone() ?? new VertexFlags(0);
            block.Resistance = typedBlockType.Resistance;
            block.BlockMaterial = typedBlockType.BlockMaterial;
            block.Shape = typedBlockType.Shape?.Clone();
            block.ShapeInventory = typedBlockType.ShapeInventory?.Clone();
            block.TexturesInventory = typedBlockType.TexturesInventory;
            block.Textures = typedBlockType.Textures;
            block.TintIndex = typedBlockType.TintIndex;
            block.Ambientocclusion = typedBlockType.Ambientocclusion;
            block.CollisionBoxes = typedBlockType.CollisionBoxes == null ? null : (Cuboidf[])typedBlockType.CollisionBoxes.Clone();
            block.SelectionBoxes = typedBlockType.SelectionBoxes == null ? null : (Cuboidf[])typedBlockType.SelectionBoxes.Clone();
            block.MaterialDensity = typedBlockType.MaterialDensity;
            block.GuiTransform = typedBlockType.GuiTransform;
            block.FpHandTransform = typedBlockType.FpHandTransform;
            block.TpHandTransform = typedBlockType.TpHandTransform;
            block.GroundTransform = typedBlockType.GroundTransform;
            block.RenderPass = typedBlockType.RenderPass;
            block.ParticleProperties = typedBlockType.ParticleProperties;
            block.Climbable = typedBlockType.Climbable;
            block.RainPermeable = typedBlockType.RainPermeable;
            block.SnowCoverage = typedBlockType.SnowCoverage;
            block.FaceCullMode = typedBlockType.FaceCullMode;
            block.Drops = typedBlockType.Drops;
            block.MaxStackSize = typedBlockType.MaxStackSize;
            block.MatterState = typedBlockType.MatterState;
            if (typedBlockType.Attributes != null)
            {
                block.Attributes = typedBlockType.Attributes.Clone();
            }
            block.NutritionProps = typedBlockType.NutritionProps;
            block.TransitionableProps = typedBlockType.TransitionableProps;
            block.GrindingProps = typedBlockType.GrindingProps;
            block.LiquidLevel = typedBlockType.LiquidLevel;
            block.AttackPower = typedBlockType.AttackPower;
            block.MiningSpeed = typedBlockType.MiningSpeed;
            block.MiningTier = typedBlockType.MiningTier;
            block.RequiredMiningTier = typedBlockType.RequiredMiningTier;
            block.AttackRange = typedBlockType.AttackRange;


            if (typedBlockType.Sounds != null)
            {
                block.Sounds = typedBlockType.Sounds.Clone();
            }
            block.RandomDrawOffset = typedBlockType.RandomDrawOffset;
            block.RandomizeAxes = typedBlockType.RandomizeAxes;
            block.CombustibleProps = typedBlockType.CombustibleProps;
            block.StorageFlags = (EnumItemStorageFlags)typedBlockType.StorageFlags;
            block.RenderAlphaTest = typedBlockType.RenderAlphaTest;
            block.HeldTpHitAnimation = typedBlockType.HeldTpHitAnimation;
            block.HeldRightTpIdleAnimation = typedBlockType.HeldRightTpIdleAnimation;
            block.HeldLeftTpIdleAnimation = typedBlockType.HeldLeftTpIdleAnimation;
            block.HeldTpUseAnimation = typedBlockType.HeldTpUseAnimation;
            block.CreativeInventoryStacks = typedBlockType.CreativeInventoryStacks == null ? null : (CreativeTabAndStackList[])typedBlockType.CreativeInventoryStacks.Clone();
            block.AllowSpawnCreatureGroups = (string[])typedBlockType.AllowSpawnCreatureGroups.Clone();

            if (block.CollisionBoxes != null)
            {
                for (int i = 0; i < block.CollisionBoxes.Length; i++)
                {
                    block.CollisionBoxes[i].RoundToFracsOf16();
                }
            }

            if (block.SelectionBoxes != null)
            {
                for (int i = 0; i < block.SelectionBoxes.Length; i++)
                {
                    block.SelectionBoxes[i].RoundToFracsOf16();
                }
            }

            typedBlockType.InitBlock(api.ClassRegistry, api.World.Logger, block, variant);

            return block;
        }



        void solveByType(JToken json, string codePath, OrderedDictionary<string, string> searchReplace)
        {
            List<string> propertiesToRemove = new List<string>();
            Dictionary<string, JToken> propertiesToAdd = new Dictionary<string, JToken>();

            if (json is JObject)
            {
                foreach (var entry in (json as JObject))
                {
                    if (entry.Key.EndsWith("byType", System.StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var byTypeProperty in entry.Value.ToObject<OrderedDictionary<string, JToken>>())
                        {
                            if (WildcardUtil.Match(byTypeProperty.Key, codePath))
                            //if (BlockType.WildCardMatch(byTypeProperty.Key, codePath))
                            {
                                JToken typedToken = byTypeProperty.Value;
                                solveByType(typedToken, codePath, searchReplace);
                                propertiesToAdd.Add(entry.Key.Substring(0, entry.Key.Length - "byType".Length), typedToken);
                                break;
                            }
                        }
                        propertiesToRemove.Add(entry.Key);
                    }
                }

                foreach (var property in propertiesToRemove)
                {
                    (json as JObject).Remove(property);
                }

                foreach (var property in propertiesToAdd)
                {
                    (json as JObject)[property.Key] = property.Value;
                }

                foreach (var entry in (json as JObject))
                {
                    solveByType(entry.Value, codePath, searchReplace);
                }
            }
            else if (json.Type == JTokenType.String)
            {
                string value = (string) (json as JValue).Value;
                if (value.Contains("{"))
                {
                    (json as JValue).Value = RegistryObject.FillPlaceHolder(value, searchReplace);
                }
            }
            else if (json is JArray)
            {
                foreach (var child in (json as JArray))
                {
                    solveByType(child, codePath, searchReplace);
                }
            }
        }
        #endregion


        public StandardWorldProperty GetWorldPropertyByCode(AssetLocation code)
        {
            StandardWorldProperty property = null;
            worldProperties.TryGetValue(code, out property);
            return property;
        }




        List<ResolvedVariant> GatherVariants(AssetLocation baseCode, RegistryObjectVariantGroup[] variantgroups, AssetLocation location, AssetLocation[] allowedVariants, AssetLocation[] skipVariants)
        {
            List<ResolvedVariant> variantsFinal = new List<ResolvedVariant>();

            if (variantgroups == null || variantgroups.Length == 0) return variantsFinal;

            OrderedDictionary<string, BlockVariant[]> blockvariantsMul = new OrderedDictionary<string, BlockVariant[]>();


            // 1. Collect all types
            for (int i = 0; i < variantgroups.Length; i++)
            {
                if (variantgroups[i].LoadFromProperties != null)
                {
                    CollectFromWorldProperties(variantgroups[i], variantgroups, blockvariantsMul, variantsFinal, location);
                }

                if (variantgroups[i].LoadFromPropertiesCombine != null)
                {
                    CollectFromWorldPropertiesCombine(variantgroups[i].LoadFromPropertiesCombine, variantgroups[i], variantgroups, blockvariantsMul, variantsFinal, location);
                }

                if (variantgroups[i].States != null)
                {
                    CollectFromStateList(variantgroups[i], variantgroups, blockvariantsMul, variantsFinal, location);
                }
            }

            // 2. Multiply multiplicative groups
            BlockVariant[,] variants = MultiplyProperties(blockvariantsMul.Values.ToArray());


            // 3. Add up multiplicative groups
            for (int i = 0; i < variants.GetLength(0); i++)
            {
                ResolvedVariant resolved = new ResolvedVariant();
                for (int j = 0; j < variants.GetLength(1); j++)
                {
                    BlockVariant variant = variants[i, j];

                    if (variant.Codes != null)
                    {
                        for (int k = 0; k < variant.Codes.Count; k++)
                        {
                            resolved.CodeParts.Add(variant.Types[k], variant.Codes[k]);
                        }
                    }
                    else
                    {
                        resolved.CodeParts.Add(blockvariantsMul.GetKeyAtIndex(j), variant.Code);
                    }
                }

                variantsFinal.Add(resolved);
            }

            foreach (ResolvedVariant var in variantsFinal)
            {
                var.ResolveCode(baseCode);
            }

            
            if (skipVariants != null)
            {
                List<ResolvedVariant> filteredVariants = new List<ResolvedVariant>();

                HashSet<AssetLocation> skipVariantsHash = new HashSet<AssetLocation>();
                List<AssetLocation> skipVariantsWildCards = new List<AssetLocation>();
                foreach(var val in skipVariants)
                {
                    if (val.IsWildCard)
                    {
                        skipVariantsWildCards.Add(val);
                    } else
                    {
                        skipVariantsHash.Add(val);
                    }
                }

                foreach (ResolvedVariant var in variantsFinal)
                {
                    if (skipVariantsHash.Contains(var.Code)) continue;
                    if (skipVariantsWildCards.FirstOrDefault(v => WildcardUtil.Match(v, var.Code)) != null) continue;

                    filteredVariants.Add(var);
                }

                variantsFinal = filteredVariants;
            }


            if (allowedVariants != null)
            {
                List<ResolvedVariant> filteredVariants = new List<ResolvedVariant>();

                HashSet<AssetLocation> allowVariantsHash = new HashSet<AssetLocation>();
                List<AssetLocation> allowVariantsWildCards = new List<AssetLocation>();
                foreach (var val in allowedVariants)
                {
                    if (val.IsWildCard)
                    {
                        allowVariantsWildCards.Add(val);
                    }
                    else
                    {
                        allowVariantsHash.Add(val);
                    }
                }

                foreach (ResolvedVariant var in variantsFinal)
                {
                    if (allowVariantsHash.Contains(var.Code) || allowVariantsWildCards.FirstOrDefault(v => WildcardUtil.Match(v, var.Code)) != null)
                    {
                        filteredVariants.Add(var);
                    }
                }

                variantsFinal = filteredVariants;
            }
            
            return variantsFinal;
        }

        private void CollectFromStateList(RegistryObjectVariantGroup variantGroup, RegistryObjectVariantGroup[] variantgroups, OrderedDictionary<string, BlockVariant[]> blockvariantsMul, List<ResolvedVariant> blockvariantsFinal, AssetLocation filename)
        {
            if (variantGroup.Code == null)
            {
                api.Server.LogError(
                    "Error in itemtype {0}, a variantgroup using a state list must have a code. Ignoring.",
                    filename
                );
                return;
            }

            string[] states = variantGroup.States;
            string type = variantGroup.Code;

            // Additive state list
            if (variantGroup.Combine == EnumCombination.Add)
            {
                for (int j = 0; j < states.Length; j++)
                {
                    ResolvedVariant resolved = new ResolvedVariant();
                    resolved.CodeParts.Add(type, states[j]);
                    blockvariantsFinal.Add(resolved);
                }
            }

            // Multiplicative state list
            if (variantGroup.Combine == EnumCombination.Multiply)
            {
                List<BlockVariant> stateList = new List<BlockVariant>();

                for (int j = 0; j < states.Length; j++)
                {
                    stateList.Add(new BlockVariant() { Code = states[j] });
                }


                for (int i = 0; i < variantgroups.Length; i++)
                {
                    RegistryObjectVariantGroup cvg = variantgroups[i];
                    if (cvg.Combine == EnumCombination.SelectiveMultiply && cvg.OnVariant == variantGroup.Code)
                    {
                        for (int k = 0; k < stateList.Count; k++)
                        {
                            if (cvg.Code != stateList[k].Code) continue;

                            BlockVariant old = stateList[k];

                            stateList.RemoveAt(k);

                            for (int j = 0; j < cvg.States.Length; j++)
                            {
                                List<string> codes = old.Codes == null ? new List<string>() { old.Code } : old.Codes;
                                List<string> types = old.Types == null ? new List<string>() { variantGroup.Code } : old.Types;

                                codes.Add(cvg.States[j]);
                                types.Add(cvg.Code);

                                stateList.Insert(k, new BlockVariant()
                                {
                                    Code = old.Code + "-" + cvg.States[j],
                                    Codes = codes,
                                    Types = types
                                });
                            }
                        }
                    }
                }

                if (blockvariantsMul.ContainsKey(type))
                {
                    stateList.AddRange(blockvariantsMul[type]);
                    blockvariantsMul[type] = stateList.ToArray();
                }
                else
                {
                    blockvariantsMul.Add(type, stateList.ToArray());
                }

            }
        }


        private void CollectFromWorldProperties(RegistryObjectVariantGroup variantGroup, RegistryObjectVariantGroup[] variantgroups, OrderedDictionary<string, BlockVariant[]> blockvariantsMul, List<ResolvedVariant> blockvariantsFinal, AssetLocation location)
        {
            CollectFromWorldPropertiesCombine(new AssetLocation[] { variantGroup.LoadFromProperties }, variantGroup, variantgroups, blockvariantsMul, blockvariantsFinal, location);
        }

        private void CollectFromWorldPropertiesCombine(AssetLocation[] propList, RegistryObjectVariantGroup variantGroup, RegistryObjectVariantGroup[] variantgroups, OrderedDictionary<string, BlockVariant[]> blockvariantsMul, List<ResolvedVariant> blockvariantsFinal, AssetLocation location)
        {
            if (propList.Length > 1 && variantGroup.Code == null)
            {
                api.Server.LogError(
                    "Error in item or block {0}, defined a variantgroup with loadFromPropertiesCombine (first element: {1}), but did not explicitly declare a code for this variant group, hence I do not know which code to use. Ignoring.",
                    location, propList[0]
                );
                return;
            }

            foreach (var val in propList)
            {
                StandardWorldProperty property = GetWorldPropertyByCode(val);

                if (property == null)
                {
                    api.Server.LogError(
                        "Error in item or block {0}, worldproperty {1} does not exist (or is empty). Ignoring.",
                        location, variantGroup.LoadFromProperties
                    );
                    return;
                }

                string typename = variantGroup.Code == null ? property.Code.Path : variantGroup.Code;

                if (variantGroup.Combine == EnumCombination.Add)
                {

                    foreach (WorldPropertyVariant variant in property.Variants)
                    {
                        ResolvedVariant resolved = new ResolvedVariant();
                        resolved.CodeParts.Add(typename, variant.Code.Path);
                        blockvariantsFinal.Add(resolved);
                    }
                }

                if (variantGroup.Combine == EnumCombination.Multiply)
                {
                    BlockVariant[] variants = null;
                    if (blockvariantsMul.TryGetValue(typename, out variants))
                    {
                        blockvariantsMul[typename] = variants.Append(worldPropertiesVariants[property.Code]);
                    } else
                    {
                        blockvariantsMul.Add(typename, worldPropertiesVariants[property.Code]);
                    }
                    
                }
            }
        }


        // Takes n lists of properties and returns every unique n-tuple 
        // through a 2 dimensional array blockvariants[i, ni] 
        // where i = n-tuple index and ni = index of current element in the n-tuple
        BlockVariant[,] MultiplyProperties(BlockVariant[][] blockVariants)
        {
            int resultingQuantiy = 1;

            for (int i = 0; i < blockVariants.Length; i++)
            {
                resultingQuantiy *= blockVariants[i].Length;
            }

            BlockVariant[,] multipliedProperties = new BlockVariant[resultingQuantiy, blockVariants.Length];

            for (int i = 0; i < resultingQuantiy; i++)
            {
                int div = 1;

                for (int j = 0; j < blockVariants.Length; j++)
                {
                    BlockVariant variant = blockVariants[j][(i / div) % blockVariants[j].Length];

                    multipliedProperties[i, j] = new BlockVariant() { Code = variant.Code, Codes = variant.Codes, Types = variant.Types };

                    div *= blockVariants[j].Length;
                }
            }

            return multipliedProperties;
        }



        private void FreeRam()
        {
            blockShapes = null;
            blockTypes = null;
            itemTypes = null;
            entityTypes = null;
            worldProperties = null;
            worldPropertiesVariants = null;
        }
    }
}