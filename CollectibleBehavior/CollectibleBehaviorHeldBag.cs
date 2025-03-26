using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{

    public interface IAttachedListener
    {
        void OnAttached(ItemSlot itemslot, int slotIndex, Entity toEntity, EntityAgent byEntity);
        void OnDetached(ItemSlot itemslot, int slotIndex, Entity fromEntity, EntityAgent byEntity);
    }
    public interface IAttachedInteractions : IAttachedListener
    {
        bool OnTryAttach(ItemSlot itemslot, int slotIndex, Entity toEntity);
        bool OnTryDetach(ItemSlot itemslot, int slotIndex, Entity toEntity);
        void OnInteract(ItemSlot itemslot, int slotIndex, Entity onEntity, EntityAgent byEntity, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled, Action onRequireSave);
        void OnEntityDespawn(ItemSlot itemslot, int slotIndex, Entity onEntity, EntityDespawnData despawn);
        void OnEntityDeath(ItemSlot itemslot, int slotIndex, Entity onEntity, DamageSource damageSourceForDeath);
        void OnReceivedClientPacket(ItemSlot itemslot, int slotIndex, Entity onEntity, IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled, Action onRequireSave);
    }


    public class CollectibleBehaviorHeldBag : CollectibleBehavior, IHeldBag, IAttachedInteractions
    {
        public const int PacketIdBitShift = 11;    // magic number; see also IClientNetworkAPI.SendEntityPacketWithOffset() which enables such tricks

        public CollectibleBehaviorHeldBag(CollectibleObject collObj) : base(collObj)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
        }

        public void Clear(ItemStack backPackStack)
        {
            ITreeAttribute stackBackPackTree = backPackStack.Attributes.GetTreeAttribute("backpack");
            stackBackPackTree["slots"] = new TreeAttribute();
        }

        public ItemStack[] GetContents(ItemStack bagstack, IWorldAccessor world)
        {
            ITreeAttribute backPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
            if (backPackTree == null) return null;

            List<ItemStack> contents = new List<ItemStack>();
            ITreeAttribute slotsTree = backPackTree.GetTreeAttribute("slots");

            foreach (var val in slotsTree.SortedCopy())
            {
                ItemStack cstack = (ItemStack)val.Value?.GetValue();

                if (cstack != null)
                {
                    cstack.ResolveBlockOrItem(world);
                }

                contents.Add(cstack);
            }

            return contents.ToArray();
        }

        public virtual bool IsEmpty(ItemStack bagstack)
        {
            ITreeAttribute backPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
            if (backPackTree == null) return true;
            ITreeAttribute slotsTree = backPackTree.GetTreeAttribute("slots");

            foreach (var val in slotsTree)
            {
                IItemStack stack = (IItemStack)val.Value?.GetValue();
                if (stack != null && stack.StackSize > 0) return false;
            }

            return true;
        }

        public virtual int GetQuantitySlots(ItemStack bagstack)
        {
            if (bagstack == null || bagstack.Collectible.Attributes == null) return 0;
            return bagstack.Collectible.Attributes["backpack"]["quantitySlots"].AsInt();
        }

        public void Store(ItemStack bagstack, ItemSlotBagContent slot)
        {
            ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
            ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

            slotsTree["slot-" + slot.SlotIndex] = new ItemstackAttribute(slot.Itemstack);
        }

        public virtual string GetSlotBgColor(ItemStack bagstack)
        {
            return bagstack.ItemAttributes["backpack"]["slotBgColor"].AsString(null);
        }

        const int defaultFlags = (int)(EnumItemStorageFlags.General | EnumItemStorageFlags.Agriculture | EnumItemStorageFlags.Alchemy | EnumItemStorageFlags.Jewellery | EnumItemStorageFlags.Metallurgy | EnumItemStorageFlags.Outfit);
        public virtual EnumItemStorageFlags GetStorageFlags(ItemStack bagstack)
        {
            return (EnumItemStorageFlags)bagstack.ItemAttributes["backpack"]["storageFlags"].AsInt(defaultFlags);
        }

        public List<ItemSlotBagContent> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv, int bagIndex, IWorldAccessor world)
        {
            var bagContents = new List<ItemSlotBagContent>();

            string bgcolhex = GetSlotBgColor(bagstack);
            var flags = GetStorageFlags(bagstack);
            int quantitySlots = GetQuantitySlots(bagstack);

            ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
            if (stackBackPackTree == null)
            {
                stackBackPackTree = new TreeAttribute();
                ITreeAttribute slotsTree = new TreeAttribute();

                for (int slotIndex = 0; slotIndex < quantitySlots; slotIndex++)
                {
                    ItemSlotBagContent slot = new ItemSlotBagContent(parentinv, bagIndex, slotIndex, flags);
                    slot.HexBackgroundColor = bgcolhex;
                    bagContents.Add(slot);
                    slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                }

                stackBackPackTree["slots"] = slotsTree;
                bagstack.Attributes["backpack"] = stackBackPackTree;
            }
            else
            {
                ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

                foreach (var val in slotsTree)
                {
                    int slotIndex = val.Key.Split("-")[1].ToInt();
                    ItemSlotBagContent slot = new ItemSlotBagContent(parentinv, bagIndex, slotIndex, flags);
                    slot.HexBackgroundColor = bgcolhex;

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        slot.Itemstack = attr.value;
                        slot.Itemstack.ResolveBlockOrItem(world);
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = slot;
                }
            }

            return bagContents;
        }






        public void OnAttached(ItemSlot itemslot, int slotIndex, Entity toEntity, EntityAgent byEntity)
        {

        }

        public void OnDetached(ItemSlot itemslot, int slotIndex, Entity fromEntity, EntityAgent byEntity)
        {
            getOrCreateContainerWorkspace(slotIndex, fromEntity, null).Close((byEntity as EntityPlayer).Player);
        }


        public AttachedContainerWorkspace getOrCreateContainerWorkspace(int slotIndex, Entity onEntity, Action onRequireSave)
        {
            return ObjectCacheUtil.GetOrCreate(onEntity.Api, "att-cont-workspace-" + slotIndex + "-" + onEntity.EntityId + "-" + collObj.Id, () => new AttachedContainerWorkspace(onEntity, onRequireSave));
        }

        public AttachedContainerWorkspace getContainerWorkspace(int slotIndex, Entity onEntity)
        {
            return ObjectCacheUtil.TryGet<AttachedContainerWorkspace>(onEntity.Api, "att-cont-workspace-" + slotIndex + "-" + onEntity.EntityId + "-" + collObj.Id);
        }


        public virtual void OnInteract(ItemSlot bagSlot, int slotIndex, Entity onEntity, EntityAgent byEntity, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled, Action onRequireSave)
        {
            var controls = byEntity.MountedOn?.Controls ?? byEntity.Controls;
            if (!controls.CtrlKey)
            {
                handled = EnumHandling.PreventDefault;
                if (onEntity.Api.Side == EnumAppSide.Client)
                {
                    var workspace = getOrCreateContainerWorkspace(slotIndex, onEntity, onRequireSave);
                    workspace.OnInteract(bagSlot, slotIndex, onEntity, byEntity, hitPosition);
                }
            }
        }

        public void OnReceivedClientPacket(ItemSlot bagSlot, int slotIndex, Entity onEntity, IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled, Action onRequireSave)
        {
            int targetSlotIndex = packetid >> PacketIdBitShift;

            if (slotIndex != targetSlotIndex) return;

            int first10Bits = (1 << PacketIdBitShift) - 1;
            packetid = packetid & first10Bits;

            getOrCreateContainerWorkspace(slotIndex, onEntity, onRequireSave).OnReceivedClientPacket(player, packetid, data, bagSlot, slotIndex, ref handled);
        }

        public bool OnTryAttach(ItemSlot itemslot, int slotIndex, Entity toEntity)
        {
            return true;
        }

        public bool OnTryDetach(ItemSlot itemslot, int slotIndex, Entity fromEntity)
        {
            return IsEmpty(itemslot.Itemstack);
        }

        public void OnEntityDespawn(ItemSlot itemslot, int slotIndex, Entity onEntity, EntityDespawnData despawn)
        {
            if (despawn.Reason == EnumDespawnReason.Death)
            {
                var contents = GetContents(itemslot.Itemstack, onEntity.World);
                foreach (var stack in contents)
                {
                    if (stack == null) continue;
                    onEntity.World.SpawnItemEntity(stack, onEntity.Pos.XYZ);
                }
            }

            getContainerWorkspace(slotIndex, onEntity)?.OnDespawn(despawn);
        }

        public void OnEntityDeath(ItemSlot itemslot, int slotIndex, Entity onEntity, DamageSource damageSourceForDeath)
        {
            
        }
    }

    public class DlgPositioner : ICustomDialogPositioning
    {
        public Entity entity;
        public int slotIndex;

        public DlgPositioner(Entity entity, int slotIndex)
        {
            this.entity = entity;
            this.slotIndex = slotIndex;
        }

        public Vec3d GetDialogPosition()
        {
            return entity.GetBehavior<EntityBehaviorSelectionBoxes>().GetCenterPosOfBox(slotIndex)?.Add(0, 1, 0);
        }
    }

    public class AttachedContainerWorkspace
    {
        /// <summary>
        /// Corresponds with MouseInWorldInteractions.BuildRepeatDelay(), but here expressed in milliseconds not fractions of a second
        /// </summary>
        private const int mouseBuildRepeatDelayMs = 250;

        public Entity entity;
        protected BagInventory bagInv;
        protected GuiDialogCreatureContents dlg;
        protected InventoryGeneric wrapperInv;
        protected Action onRequireSave;
        public BagInventory BagInventory => bagInv;
        public InventoryGeneric WrapperInv => wrapperInv;
        private long lastMouseActionTime = 0;

        public AttachedContainerWorkspace(Entity entity, Action onRequireSave)
        {
            this.entity = entity;
            this.onRequireSave = onRequireSave;
            bagInv = new BagInventory(entity.Api, null);
        }

        public void OnInteract(ItemSlot bagSlot, int slotIndex, Entity onEntity, EntityAgent byEntity, Vec3d hitPosition)
        {
            if (!TryLoadInv(bagSlot, slotIndex, onEntity))
            {
                return;
            }

            EntityPlayer entityplr = byEntity as EntityPlayer;
            IPlayer player = onEntity.World.PlayerByUid(entityplr.PlayerUID);

            bool opened = false;
            if (onEntity.World.Side == EnumAppSide.Client) // Let the client decide when to open/close inventories
            {
                long timeNow = Environment.TickCount64;
                if (timeNow - lastMouseActionTime < mouseBuildRepeatDelayMs) return;    // Prevents immediate re-opening of just closed inventory
                lastMouseActionTime = timeNow;

                if (player.InventoryManager.OpenedInventories.FirstOrDefault(inv => inv.InventoryID == wrapperInv.InventoryID) != null)
                {
                    Close(player);
                    return;
                }

                player.InventoryManager.OpenInventory(wrapperInv);

                dlg = new GuiDialogCreatureContents(wrapperInv, onEntity, onEntity.Api as ICoreClientAPI, "attachedcontainer-" + slotIndex, bagSlot.GetStackName(), new DlgPositioner(entity, slotIndex));
                dlg.packetIdOffset = slotIndex << CollectibleBehaviorHeldBag.PacketIdBitShift;

                if (dlg.TryOpen())
                {
                    var capi = onEntity.World.Api as ICoreClientAPI;
                    wrapperInv.Open(player);
                    capi.Network.SendEntityPacket(onEntity.EntityId, (int)EntityClientPacketId.OpenAttachedInventory + dlg.packetIdOffset, null);
                    opened = true;
                }

                dlg.OnClosed += () => Close(player);
            }
            else
            {
                player.InventoryManager?.OpenInventory(wrapperInv);
                opened = true;
            }

            if (opened) entity.World.Logger.Audit("{0} opened held bag inventory ({3}) on entity {1}/{2}", player?.PlayerName, entity.EntityId, entity.GetName(), wrapperInv.InventoryID);
        }

        public void Close(IPlayer player)
        {
            if (dlg != null && dlg.IsOpened())
            {
                dlg?.TryClose();
            }
            dlg?.Dispose();
            dlg = null;
            if (player != null && wrapperInv != null)
            {
                (player.Entity.Api as ICoreClientAPI)?.Network.SendPacketClient(wrapperInv.Close(player));
                player.InventoryManager.CloseInventory(wrapperInv);
                entity.World.Logger.Audit("{0} closed held bag inventory {3} on entity {1}/{2}", player?.PlayerName, entity.EntityId, entity.GetName(), wrapperInv.InventoryID);
            }
        }

        public bool TryLoadInv(ItemSlot bagSlot, int slotIndex, Entity entity)
        {
            if (bagSlot.Empty) return false;
            var bag = bagSlot.Itemstack.Collectible.GetCollectibleInterface<IHeldBag>();
            if (bag == null || bag.GetQuantitySlots(bagSlot.Itemstack) <= 0) return false;
            List<ItemSlot> bagslots = new List<ItemSlot> { bagSlot };

            if (wrapperInv != null)
            {
                bagInv.ReloadBagInventory(wrapperInv, bagslots.ToArray());
                return true;
            }

            wrapperInv = new InventoryGeneric(entity.Api);
            bagInv.ReloadBagInventory(wrapperInv, bagslots.ToArray());
            wrapperInv.Init(bagInv.Count, "mountedbaginv", slotIndex + "-" + entity.EntityId, onNewSlot);

            if (entity.World.Side == EnumAppSide.Server)
            {
                wrapperInv.SlotModified += Inv_SlotModified;
            }

            return true;
        }

        private ItemSlot onNewSlot(int slotId, InventoryGeneric self)
        {
            return bagInv[slotId];
        }

        private void Inv_SlotModified(int slotid)
        {
            var slot = wrapperInv[slotid];
            bagInv.SaveSlotIntoBag((ItemSlotBagContent)slot);
            onRequireSave?.Invoke();
        }


        public void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data, ItemSlot bagSlot, int slotIndex, ref EnumHandling handled)
        {
            if (packetid < 1000)
            {
                if (wrapperInv != null && wrapperInv.HasOpened(player))
                {
                    wrapperInv.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                    handled = EnumHandling.PreventSubsequent;
                }

                return;
            }

            if (packetid == (int)EntityClientPacketId.OpenAttachedInventory)       // radfast 3.3.2025:  Compare BEOpenableContainer.OnReceivedClientPacket(), this is similar
            {
                OnInteract(bagSlot, slotIndex, entity, player.Entity, null);   // null hitPosition here is ok, it is never used in the OnInteract() method. If we need to use it in future, it would need to be encoded in the packet data byte[]
            }
        }

        public void OnDespawn(EntityDespawnData despawn)
        {
            dlg?.TryClose();
            if (wrapperInv == null) return;
            foreach (var uid in wrapperInv.openedByPlayerGUIds)
            {
                var plr = entity.Api.World.PlayerByUid(uid);
                plr?.InventoryManager.CloseInventory(wrapperInv);
                if (plr != null) entity.World.Logger.Audit("{0} closed held bag inventory {3} on entity {1}/{2}", plr?.PlayerName, entity.EntityId, entity.GetName(), wrapperInv.InventoryID);
            }
        }

        public enum EntityClientPacketId
        {
            OpenAttachedInventory = 1001
        }
    }
}
