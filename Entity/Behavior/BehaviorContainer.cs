using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{
    public class StepParentElementTo : ModelTransform
    {
        [JsonProperty]
        public string ElementName;
    }

    public interface IAttachableToEntity
    {
        bool IsAttachable(Entity toEntity, ItemStack itemStack);

        /// <summary>
        /// Add textures necessary to correctly tesselate this attachable. Be aware, if you put stuff in intoDict, you must prefix it correctly
        /// </summary>
        /// <param name="stack"></param>
        /// <param name="shape"></param>
        /// <param name="texturePrefixCode"></param>
        /// <param name="intoDict"></param>
        void CollectTextures(ItemStack stack, Shape shape, string texturePrefixCode, Dictionary<string, CompositeTexture> intoDict);
        string GetCategoryCode(ItemStack stack);
        CompositeShape GetAttachedShape(ItemStack stack, string slotCode);
        string[] GetDisableElements(ItemStack stack);
        string[] GetKeepElements(ItemStack stack);
        string GetTexturePrefixCode(ItemStack stack);

        int RequiresBehindSlots { get; set; }

        public static IAttachableToEntity FromCollectible(CollectibleObject cobj)
        {
            var iate = cobj.GetCollectibleInterface<IAttachableToEntity>();
            if (iate != null) return iate;
            return FromAttributes(cobj);
        }

        public static IAttachableToEntity FromAttributes(CollectibleObject cobj)
        {
            var iattr = cobj.Attributes?["attachableToEntity"].AsObject<AttributeAttachableToEntity>(null, cobj.Code.Domain);
            if (iattr == null && cobj.Attributes?["wearableAttachment"].Exists == true)
            {
                return new AttributeAttachableToEntity()
                {
                    CategoryCode = cobj.Attributes["clothescategory"].AsString() ?? cobj.Attributes?["attachableToEntity"]["categoryCode"].AsString(),
                    KeepElements = cobj.Attributes["keepElements"].AsArray<string>(),
                    DisableElements = cobj.Attributes["disableElements"].AsArray<string>()
                };
            }

            return iattr;
        }

        public static void CollectTexturesFromCollectible(ItemStack stack, string texturePrefixCode, Shape gearShape, Dictionary<string, CompositeTexture> intoDict)
        {
            if (gearShape.Textures == null) gearShape.Textures = new Dictionary<string, AssetLocation>();
            var collectibleDict = stack.Class == EnumItemClass.Block ? stack.Block.Textures : stack.Item.Textures;
            if (collectibleDict != null)
            {
                foreach (var val in collectibleDict)
                {
                    gearShape.Textures[val.Key] = val.Value.Base;
                }
            }
        }
    }

    public class AttributeAttachableToEntity : IAttachableToEntity
    {
        public string CategoryCode { get; set; }
        public CompositeShape AttachedShape { get; set; }
        public string[] DisableElements { get; set; }
        public string[] KeepElements { get; set; }
        public string TexturePrefixCode { get; set; }
        public string GetTexturePrefixCode(ItemStack stack) => TexturePrefixCode;
        public API.Datastructures.OrderedDictionary<string, CompositeShape> AttachedShapeBySlotCode { get; set; }
        public void CollectTextures(ItemStack stack, Shape gearShape, string texturePrefixCode, Dictionary<string, CompositeTexture> intoDict) => IAttachableToEntity.CollectTexturesFromCollectible(stack, texturePrefixCode, gearShape, intoDict);

        /// <summary>
        /// Occupy additional space behind this slot
        /// </summary>
        public int RequiresBehindSlots { get; set; }


        public CompositeShape GetAttachedShape(ItemStack stack, string slotCode)
        {
            if (AttachedShape != null)
            {
                return AttachedShape;
            }

            if (AttachedShapeBySlotCode != null)
            {
                foreach (var val in AttachedShapeBySlotCode)
                {
                    if (WildcardUtil.Match(val.Key, slotCode))
                    {
                        return val.Value;
                    }
                }
            }

            return stack.Class == EnumItemClass.Item ? stack.Item.Shape : stack.Block.Shape;
        }

        public string GetCategoryCode(ItemStack stack) => CategoryCode;
        public string[] GetDisableElements(ItemStack stack) => DisableElements;
        public string[] GetKeepElements(ItemStack stack) => KeepElements;
        public bool IsAttachable(Entity toEntity, ItemStack itemStack) => true;
    }

    public abstract class EntityBehaviorContainer : EntityBehavior
    {
        protected ICoreAPI Api;
        public abstract InventoryBase Inventory { get; }
        public abstract string InventoryClassName { get; }

        InWorldContainer container;
        public bool hideClothing;
        bool eventRegistered;
        bool dropContentsOnDeath;

        protected EntityBehaviorContainer(Entity entity) : base(entity)
        {
            container = new InWorldContainer(() => Inventory, InventoryClassName);
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            Api = entity.World.Api;

            container.Init(Api, () => entity.Pos.AsBlockPos, () => entity.WatchedAttributes.MarkPathDirty(InventoryClassName));

            if (Api.Side == EnumAppSide.Client)
            {
                entity.WatchedAttributes.RegisterModifiedListener(InventoryClassName, inventoryModified);
            }

            dropContentsOnDeath = attributes?.IsTrue("dropContentsOnDeath") == true;
        }

        private void inventoryModified()
        {
            loadInv();
            entity.MarkShapeModified();
        }

        public override void OnGameTick(float deltaTime)
        {
            if (!eventRegistered && Inventory != null)
            {
                eventRegistered = true;
                Inventory.SlotModified += Inventory_SlotModified;
            }
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (Inventory != null)
            {
                Inventory.SlotModified -= Inventory_SlotModified;
            }
        }

        protected void Inventory_SlotModifiedBackpack(int slotid)
        {
            if (entity is EntityPlayer player)
            {
                var ownInventory = player.Player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
                var itemSlot = ownInventory[slotid];
                if (itemSlot is ItemSlotBackpack)
                {
                    entity.MarkShapeModified();
                }
            }
        }
        protected virtual void Inventory_SlotModified(int slotid)
        {
            entity.MarkShapeModified();
        }

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            addGearToShape(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);

            base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);

            if (Inventory != null)
            {
                var brightestSlot = Inventory.MaxBy(slot => slot.Empty ? 0 : slot.Itemstack.Collectible.LightHsv[2]);
                if (!brightestSlot.Empty)
                {
                    entity.LightHsv = brightestSlot.Itemstack.Collectible.GetLightHsv(entity.World.BlockAccessor, null, brightestSlot.Itemstack);
                }
                else
                {
                    entity.LightHsv = null;
                }
            }
        }



        protected Shape addGearToShape(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            IInventory inv = Inventory;

            if (inv == null || (!(entity is EntityPlayer) && inv.Empty)) return entityShape;

            foreach (var slot in inv)
            {
                if (slot.Empty || hideClothing) continue;

                entityShape = addGearToShape(entityShape, slot, "default", shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);
            }

            // The texture definition in the entity type override all shape specific textures
            if (shapeIsCloned && Api is ICoreClientAPI capi)
            {
                var etype = Api.World.GetEntityType(entity.Code);
                if (etype != null)
                {
                    foreach (var val in etype.Client.Textures)
                    {
                        var cmpt = val.Value;
                        cmpt.Bake(Api.Assets);
                        capi.EntityTextureAtlas.GetOrInsertTexture(cmpt.Baked.TextureFilenames[0], out int textureSubid, out _);
                        cmpt.Baked.TextureSubId = textureSubid;

                        entity.Properties.Client.Textures[val.Key] = val.Value;
                    }
                }
            }

            return entityShape;
        }


        protected virtual Shape addGearToShape(Shape entityShape, ItemSlot gearslot, string slotCode, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements, Dictionary<string, StepParentElementTo> overrideStepParent = null)
        {
            if (gearslot.Empty || entityShape == null) return entityShape;
            var iatta = IAttachableToEntity.FromCollectible(gearslot.Itemstack.Collectible);
            if (iatta == null || !iatta.IsAttachable(entity, gearslot.Itemstack) || !entity.HasBehavior("dressable")) return entityShape;

            if (!shapeIsCloned)
            {
                // Make a full copy so we don't mess up the original
                Shape newShape = entityShape.Clone();
                entityShape = newShape;
                shapeIsCloned = true;
            }

            return addGearToShape(entityShape, gearslot.Itemstack, iatta, slotCode, shapePathForLogging, ref willDeleteElements, overrideStepParent);
        }

        protected virtual Shape addGearToShape(Shape entityShape, ItemStack stack, IAttachableToEntity iatta, string slotCode, string shapePathForLogging, ref string[] willDeleteElements, Dictionary<string, StepParentElementTo> overrideStepParent = null)
        {
            if (stack == null || iatta == null) return entityShape;

            float damageEffect = 0;
            if (stack.ItemAttributes?["visibleDamageEffect"].AsBool() == true)
            {
                damageEffect = Math.Max(0, 1 - (float)stack.Collectible.GetRemainingDurability(stack) / stack.Collectible.GetMaxDurability(stack) * 1.1f);
            }

            entityShape.RemoveElements(iatta.GetDisableElements(stack));

            var keepEles = iatta.GetKeepElements(stack);
            if (keepEles != null && willDeleteElements != null)
            {
                foreach (var val in keepEles) willDeleteElements = willDeleteElements.Remove(val);
            }


            var textures = entity.Properties.Client.Textures;
            string texturePrefixCode = iatta.GetTexturePrefixCode(stack);

            Shape gearShape = null;
            AssetLocation shapePath = null;
            CompositeShape compGearShape = null;
            if (stack.Collectible is IWearableShapeSupplier iwss)
            {
                gearShape = iwss.GetShape(stack, entity, texturePrefixCode);
            }

            if (gearShape == null)
            {
                compGearShape = iatta.GetAttachedShape(stack, slotCode);
                shapePath = compGearShape.Base.CopyWithPath("shapes/" + compGearShape.Base.Path + ".json");

                gearShape = Shape.TryGet(Api, shapePath);
                if (gearShape == null)
                {
                    Api.World.Logger.Warning("Entity attachable shape {0} defined in {1} {2} not found or errored, was supposed to be at {3}. Shape will be invisible.", compGearShape.Base, stack.Class, stack.Collectible.Code, shapePath);
                    return null;
                }

                gearShape.SubclassForStepParenting(texturePrefixCode, damageEffect);
                gearShape.ResolveReferences(entity.World.Logger, shapePath);
            }

            var capi = Api as ICoreClientAPI;

            Dictionary<string, CompositeTexture> intoDict = null;
            if (capi != null)
            {
                intoDict = new Dictionary<string, CompositeTexture>();
                // Item stack textures take precedence over shape textures
                iatta.CollectTextures(stack, gearShape, texturePrefixCode, intoDict);
            }


            applyStepParentOverrides(overrideStepParent, gearShape);
            entityShape.StepParentShape(
                gearShape,
                (compGearShape?.Base.ToString() ?? "Custom texture from ItemWearableShapeSupplier") + string.Format(" defined in {0} {1}", stack.Class, stack.Collectible.Code),
                shapePathForLogging,
                Api.World.Logger,
                (texcode, tloc) => addTexture(texcode, tloc, textures, texturePrefixCode, capi)
            );


            if (compGearShape?.Overlays != null)
            {
                foreach (var overlay in compGearShape.Overlays)
                {
                    Shape oshape = Shape.TryGet(Api, overlay.Base.CopyWithPath("shapes/" + overlay.Base.Path + ".json"));
                    if (oshape == null)
                    {
                        Api.World.Logger.Warning("Entity attachable shape {0} overlay {4} defined in {1} {2} not found or errored, was supposed to be at {3}. Shape will be invisible.", compGearShape.Base, stack.Class, stack.Collectible.Code, shapePath, overlay.Base);
                        continue;
                    }

                    oshape.SubclassForStepParenting(texturePrefixCode, damageEffect);

                    if (capi != null)
                    {
                        // Item stack textures take precedence over shape textures
                        iatta.CollectTextures(stack, oshape, texturePrefixCode, intoDict);
                    }

                    applyStepParentOverrides(overrideStepParent, oshape);
                    entityShape.StepParentShape(oshape, overlay.Base.ToShortString(), shapePathForLogging, Api.Logger, (texcode, tloc) => addTexture(texcode, tloc, textures, texturePrefixCode, capi));
                }
            }

            if (capi != null)
            {
                foreach (var val in intoDict)
                {
                    var cmpt = textures[val.Key] = val.Value.Clone();
                    capi.EntityTextureAtlas.GetOrInsertTexture(cmpt, out int textureSubid, out _);
                    cmpt.Baked.TextureSubId = textureSubid;
                }
            }


            return entityShape;
        }

        private static void applyStepParentOverrides(Dictionary<string, StepParentElementTo> overrideStepParent, Shape gearShape)
        {
            if (overrideStepParent != null)
            {
                overrideStepParent.TryGetValue("", out var noparentoverride);
                foreach (var ele in gearShape.Elements)
                {
                    if (ele.StepParentName == null || ele.StepParentName.Length == 0)
                    {
                        ele.StepParentName = noparentoverride.ElementName;
                    }
                    else
                    {
                        if (overrideStepParent.TryGetValue(ele.StepParentName, out var parentovr))
                        {
                            ele.StepParentName = parentovr.ElementName;
                        }
                    }
                }
            }
        }

        private void addTexture(string texcode, AssetLocation tloc, IDictionary<string, CompositeTexture> textures, string texturePrefixCode, ICoreClientAPI capi)
        {
            if (capi != null)
            {
                var cmpt = textures[texturePrefixCode + texcode] = new CompositeTexture(tloc);
                cmpt.Bake(Api.Assets);
                capi.EntityTextureAtlas.GetOrInsertTexture(cmpt.Baked.TextureFilenames[0], out int textureSubid, out _);
                cmpt.Baked.TextureSubId = textureSubid;
            }
        }

        public override void OnLoadCollectibleMappings(IWorldAccessor worldForNewMappings, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, bool resolveImports)
        {
            container.OnLoadCollectibleMappings(worldForNewMappings, oldBlockIdMapping, oldItemIdMapping, 0, resolveImports);
        }



        public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
        {
            container.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        }


        public override void FromBytes(bool isSync)
        {
            loadInv();
        }

        protected virtual void loadInv()
        {
            if (Inventory == null) return;

            container.FromTreeAttributes(entity.WatchedAttributes, entity.World);

            entity.MarkShapeModified();
        }

        public override void ToBytes(bool forClient)
        {
            storeInv();
        }

        public virtual void storeInv()
        {
            container.ToTreeAttributes(entity.WatchedAttributes);
            entity.WatchedAttributes.MarkPathDirty(InventoryClassName);
            // Tell server to save this chunk to disk again
            entity.World.BlockAccessor.GetChunkAtBlockPos(entity.ServerPos.AsBlockPos)?.MarkModified();
        }



        public override bool TryGiveItemStack(ItemStack itemstack, ref EnumHandling handling)
        {
            ItemSlot dummySlot = new DummySlot(null);
            dummySlot.Itemstack = itemstack.Clone();

            ItemStackMoveOperation op = new ItemStackMoveOperation(entity.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, itemstack.StackSize);

            if (Inventory != null)
            {
                WeightedSlot wslot = Inventory.GetBestSuitedSlot(dummySlot, null, new List<ItemSlot>());
                if (wslot.weight > 0)
                {
                    dummySlot.TryPutInto(wslot.slot, ref op);
                    itemstack.StackSize -= op.MovedQuantity;
                    entity.WatchedAttributes.MarkAllDirty();
                    return op.MovedQuantity > 0;
                }
            }

            if ((entity as EntityAgent)?.LeftHandItemSlot?.Inventory != null)
            {
                WeightedSlot wslot = (entity as EntityAgent)?.LeftHandItemSlot.Inventory.GetBestSuitedSlot(dummySlot, null, new List<ItemSlot>());
                if (wslot.weight > 0)
                {
                    dummySlot.TryPutInto(wslot.slot, ref op);
                    itemstack.StackSize -= op.MovedQuantity;
                    entity.WatchedAttributes.MarkAllDirty();
                    return op.MovedQuantity > 0;
                }
            }

            return false;
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            base.OnEntityDeath(damageSourceForDeath);

            if (dropContentsOnDeath)
            {
                Inventory.DropAll(entity.ServerPos.XYZ);
            }
        }

    }
}
