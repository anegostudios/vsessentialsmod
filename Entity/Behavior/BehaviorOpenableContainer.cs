using System.Text;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Adds inventory to entity that is opened upon interacting with it.
    /// <br/>Uses the "openablecontainer" code
    /// </summary>
    /// <example><code lang="json">
    ///"behaviors": [
    /// {
    ///     "code": "openablecontainer",
    ///     "quantitySlots": 8
    /// },
    ///],
    /// </code></example>
    [DocumentAsJson]
    [AddDocumentationProperty("quantitySlots", "The amount of slots the inventory should have", "System.Int32", "Optional", "8", false)]
    public class EntityBehaviorOpenableContainer : EntityBehavior
    {
        protected InventoryGeneric inv;
        protected GuiDialogCreatureContents dlg;

        public EntityBehaviorOpenableContainer(Entity entity) : base(entity)
        {
        }

        public override void OnGameTick(float deltaTime)
        {
            base.OnGameTick(deltaTime);
        }

        private void Inv_SlotModified(int slotid)
        {
            TreeAttribute tree = new TreeAttribute();
            inv.ToTreeAttributes(tree);
            entity.WatchedAttributes["harvestableInv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("harvestableInv");
        }



        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            inv = new InventoryGeneric(typeAttributes["quantitySlots"].AsInt(8), "contents-" + entity.EntityId, entity.Api);
            TreeAttribute tree = entity.WatchedAttributes["harvestableInv"] as TreeAttribute;
            if (tree != null) inv.FromTreeAttributes(tree);
            inv.PutLocked = false;

            if (entity.World.Side == EnumAppSide.Server)
            {
                inv.SlotModified += Inv_SlotModified;
            }

            base.Initialize(properties, typeAttributes);
        }


        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            bool inRange = (byEntity.World.Side == EnumAppSide.Client && byEntity.Pos.SquareDistanceTo(entity.Pos) <= 5) || (byEntity.World.Side == EnumAppSide.Server && byEntity.Pos.SquareDistanceTo(entity.Pos) <= 14);

            if (!inRange || !byEntity.Controls.ShiftKey)
            {
                return;
            }

            EntityPlayer entityplr = byEntity as EntityPlayer;
            IPlayer player = entity.World.PlayerByUid(entityplr.PlayerUID);
            player.InventoryManager.OpenInventory(inv);

            if (entity.World.Side == EnumAppSide.Client && dlg == null)
            {
                dlg = new GuiDialogCreatureContents(inv, entity, entity.Api as ICoreClientAPI, "invcontents");
                if (dlg.TryOpen())
                {
                    (entity.World.Api as ICoreClientAPI).Network.SendPacketClient(inv.Open(player));
                }

                dlg.OnClosed += () =>
                {
                    dlg.Dispose();
                    dlg = null;
                };
            }
        }


        public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled)
        {
            if (packetid < 1000)
            {
                var perms = new Entity.CachedAccessPerms(this.entity, player);
                if(!perms.IsInteractingPlayerAllowedTo(EnumBlockAccessFlags.Use, true, "entity behavior openable container"))
                {
                    inv.InvNetworkUtil.SendInventoryRollback(player, packetid, data);
                    return;
                }

                inv.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                handled = EnumHandling.PreventSubsequent;
                return;
            }

            if (packetid == 1012)
            {
                var perms = new Entity.CachedAccessPerms(this.entity, player);
                if(!perms.IsInteractingPlayerAllowedTo(EnumBlockAccessFlags.Use, true, "entity behavior openable container"))
                {
                    // No need to revert this, just discard the request.
                    return;
                }

                player.InventoryManager.OpenInventory(inv);
            }
        }



        WorldInteraction[] interactions = null;

        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
        {
            interactions = ObjectCacheUtil.GetOrCreate(world.Api, "entityContainerInteractions", () =>
            {
                return new WorldInteraction[] {
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-open",
                        MouseButton = EnumMouseButton.Right,
                        HotKeyCode = "shift"
                    }
                };
            });

            return interactions;
        }


        public override void GetInfoText(StringBuilder infotext)
        {


            base.GetInfoText(infotext);
        }




        public override string PropertyName()
        {
            return "openablecontainer";
        }

    }
}
