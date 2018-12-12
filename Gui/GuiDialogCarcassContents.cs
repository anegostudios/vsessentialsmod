using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class GuiDialogCarcassContents : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;

        InventoryGeneric inv;
        EntityAgent owningEntity;
        

        public GuiDialogCarcassContents(InventoryGeneric inv, EntityAgent owningEntity, ICoreClientAPI capi) : base(capi)
        {
            this.inv = inv;
            this.owningEntity = owningEntity;
            

            double pad = GuiElementItemSlotGrid.unscaledSlotPadding;

            int rows = (int)Math.Ceiling(inv.Count / 4f);

            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, pad, 40 + pad, 4, rows).FixedGrow(2 * pad, 2 * pad);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            ElementBounds dialogBounds = ElementStdBounds
                .AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);


            SingleComposer =
                capi.Gui
                .CreateCompo("carcasscontents" + owningEntity.EntityId, dialogBounds)
                .AddDialogBG(bgBounds, true)
                .AddDialogTitleBar("Contents", OnTitleBarClose)
                .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(inv, DoSendPacket, 4, slotBounds, "slots")
                .EndChildElements()
                .Compose()
            ;
        }
        
        
        private void DoSendPacket(object p)
        {
            capi.Network.SendEntityPacket(owningEntity.EntityId, p);
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();

            capi.Network.SendPacketClient(capi.World.Player.InventoryManager.CloseInventory(inv));
            SingleComposer.GetSlotGrid("slots").OnGuiClosed(capi);
        }
        
    }
}
