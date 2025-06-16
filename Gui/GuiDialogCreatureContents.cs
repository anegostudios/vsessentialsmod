using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public interface ICustomDialogPositioning
    {
        Vec3d GetDialogPosition();
    }
    public class GuiDialogCreatureContents : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;

        InventoryGeneric inv;
        Entity owningEntity;
        public int packetIdOffset;

        protected double FloatyDialogPosition => 0.6;
        protected double FloatyDialogAlign => 0.8;

        public override bool UnregisterOnClose => true;

        EnumPosFlag screenPos;
        string title;

        ICustomDialogPositioning icdp;

        public GuiDialogCreatureContents(InventoryGeneric inv, Entity owningEntity, ICoreClientAPI capi, string code, string title = null, ICustomDialogPositioning icdp = null) : base(capi)
        {
            this.inv = inv;
            this.title = title;
            this.owningEntity = owningEntity;
            this.icdp = icdp;

            Compose(code);
        }

        public void Compose(string code)
        {
            double pad = GuiElementItemSlotGrid.unscaledSlotPadding;

            int rows = (int)Math.Ceiling(inv.Count / 4f);

            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, pad, 40 + pad, 4, rows).FixedGrow(2 * pad, 2 * pad);

            screenPos = GetFreePos("smallblockgui");
            float elemToDlgPad = 10;

            ElementBounds dialogBounds = slotBounds
                .ForkBoundingParent(elemToDlgPad, elemToDlgPad + 30, elemToDlgPad, elemToDlgPad)
                .WithFixedAlignmentOffset(IsRight(screenPos) ? -GuiStyle.DialogToScreenPadding : GuiStyle.DialogToScreenPadding, 0)
                .WithAlignment(IsRight(screenPos) ? EnumDialogArea.RightMiddle : EnumDialogArea.LeftMiddle)
            ;

            if (!capi.Settings.Bool["immersiveMouseMode"])
            {
                dialogBounds.fixedOffsetY += (dialogBounds.fixedHeight + 10) * YOffsetMul(screenPos);
                dialogBounds.fixedOffsetX += (dialogBounds.fixedWidth + 10) * XOffsetMul(screenPos);
            }


            SingleComposer =
                capi.Gui
                .CreateCompo(code + owningEntity.EntityId, dialogBounds)
                .AddShadedDialogBG(ElementBounds.Fill, true)
                .AddDialogTitleBar(Lang.Get(title ?? code), OnTitleBarClose)
                .AddItemSlotGrid(inv, DoSendPacket, 4, slotBounds, "slots")
                .Compose()
            ;
        }

        private void DoSendPacket(object p)
        {
             capi.Network.SendEntityPacketWithOffset(owningEntity.EntityId, packetIdOffset, p);
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();

            if (capi.Gui.GetDialogPosition(SingleComposer.DialogName) == null)
            {
                OccupyPos("smallblockgui", screenPos);
            }
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();

            capi.World.Player.InventoryManager.CloseInventoryAndSync(inv);
            SingleComposer.GetSlotGrid("slots").OnGuiClosed(capi);

            FreePos("smallblockgui", screenPos);
        }


        /// <summary>
        /// Render's the object in Orthographic mode.
        /// </summary>
        /// <param name="deltaTime">The time elapsed.</param>
        public override void OnRenderGUI(float deltaTime)
        {
            if (capi.Settings.Bool["immersiveMouseMode"])
            {
                double offX = owningEntity.SelectionBox.X2 - owningEntity.OriginSelectionBox.X2;
                double offZ = owningEntity.SelectionBox.Z2 - owningEntity.OriginSelectionBox.Z2;

                Vec3d aboveHeadPos = new Vec3d(owningEntity.Pos.X + offX, owningEntity.Pos.Y + FloatyDialogPosition, owningEntity.Pos.Z + offZ);
                if (icdp != null) aboveHeadPos = icdp.GetDialogPosition();

                Vec3d pos = MatrixToolsd.Project(aboveHeadPos, capi.Render.PerspectiveProjectionMat, capi.Render.PerspectiveViewMat, capi.Render.FrameWidth, capi.Render.FrameHeight);

                // Z negative seems to indicate that the name tag is behind us \o/
                if (pos.Z < 0) return;

                SingleComposer.Bounds.Alignment = EnumDialogArea.None;
                SingleComposer.Bounds.fixedOffsetX = 0;
                SingleComposer.Bounds.fixedOffsetY = 0;
                SingleComposer.Bounds.absFixedX = pos.X - SingleComposer.Bounds.OuterWidth / 2;
                SingleComposer.Bounds.absFixedY = capi.Render.FrameHeight - pos.Y - SingleComposer.Bounds.OuterHeight * FloatyDialogAlign;
                SingleComposer.Bounds.absMarginX = 0;
                SingleComposer.Bounds.absMarginY = 0;
            }

            base.OnRenderGUI(deltaTime);
        }

        Vec3d entityPos = new Vec3d();

        public override void OnFinalizeFrame(float dt)
        {
            base.OnFinalizeFrame(dt);

            entityPos.Set(owningEntity.Pos.X, owningEntity.Pos.Y, owningEntity.Pos.Z);
            entityPos.Add(owningEntity.SelectionBox.X2 - owningEntity.OriginSelectionBox.X2, 0, owningEntity.SelectionBox.Z2 - owningEntity.OriginSelectionBox.Z2);

            if (!IsInRangeOfBlock())
            {
                // Because we cant do it in here
                capi.Event.EnqueueMainThreadTask(() => TryClose(), "closedlg");
            }
        }

        public override bool TryClose()
        {
            return base.TryClose();
        }

        /// <summary>
        /// Checks if the player is in range of the block.
        /// </summary>
        /// <param name="pos">The block's position.</param>
        /// <returns>In range or no?</returns>
        public virtual bool IsInRangeOfBlock()
        {
            Vec3d playerEye = capi.World.Player.Entity.Pos.XYZ.Add(capi.World.Player.Entity.LocalEyePos);
            double dist = GameMath.Sqrt(playerEye.SquareDistanceTo(entityPos));

            return dist <= capi.World.Player.WorldData.PickingRange;
        }


        public override bool PrefersUngrabbedMouse => false;
    }

}
