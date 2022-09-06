using Cairo;
using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class GuiDialogEditWayPoint : GuiDialogGeneric
    {
        public override bool PrefersUngrabbedMouse => true;

        EnumDialogType dialogType = EnumDialogType.Dialog;
        public override EnumDialogType DialogType => dialogType;

        int[] colors;
        string[] icons;

        Waypoint waypoint;
        int wpIndex;

        internal Vec3d WorldPos;

        public override double DrawOrder => 0.2;


        public GuiDialogEditWayPoint(ICoreClientAPI capi, WaypointMapLayer wml, Waypoint waypoint, int index) : base("", capi)
        {
            icons = wml.WaypointIcons.ToArray();
            colors = wml.WaypointColors.ToArray();

            this.wpIndex = index;
            this.waypoint = waypoint;

            ComposeDialog();
        }

        public override bool TryOpen()
        {
            ComposeDialog();
            return base.TryOpen();
        }

        private void ComposeDialog()
        {
            ElementBounds leftColumn = ElementBounds.Fixed(0, 28, 120, 25);
            ElementBounds rightColumn = leftColumn.RightCopy();

            ElementBounds buttonRow = ElementBounds.Fixed(0, 28, 360, 25);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(leftColumn, rightColumn);

            ElementBounds dialogBounds =
                ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);


            if (SingleComposer != null) SingleComposer.Dispose();

            int colorIconSize = 22;
            

            int iconIndex = icons.IndexOf(waypoint.Icon);
            if (iconIndex < 0) iconIndex = 0;

            int colorIndex = colors.IndexOf(waypoint.Color);
            if (colorIndex < 0)
            {
                colors = colors.Append(waypoint.Color);
                colorIndex = colors.Length - 1;
            }

            SingleComposer = capi.Gui
                .CreateCompo("worldmap-modwp", dialogBounds)
                .AddShadedDialogBG(bgBounds, false)
                .AddDialogTitleBar(Lang.Get("Modify waypoint"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("Name"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.FlatCopy())
                    .AddTextInput(rightColumn = rightColumn.FlatCopy().WithFixedWidth(200), onNameChanged, CairoFont.TextInput(), "nameInput")

                    .AddStaticText(Lang.Get("Pinned"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 9))
                    .AddSwitch(onPinnedToggled, rightColumn = rightColumn.BelowCopy(0, 5).WithFixedWidth(200), "pinnedSwitch")

                    .AddRichtext(Lang.Get("waypoint-color"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 5))
                    .AddColorListPicker(colors, onColorSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize, colorIconSize), 270, "colorpicker")

                    .AddStaticText(Lang.Get("Icon"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.WithFixedPosition(0, leftColumn.fixedY + leftColumn.fixedHeight).BelowCopy(0, 0))
                    .AddIconListPicker(icons, onIconSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize+5, colorIconSize+5), 270, "iconpicker")

                    .AddSmallButton(Lang.Get("Cancel"), onCancel, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100), EnumButtonStyle.Normal)
                    .AddSmallButton(Lang.Get("Delete"), onDelete, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.CenterFixed), EnumButtonStyle.Normal)
                    .AddSmallButton(Lang.Get("Save"), onSave, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal, key: "saveButton")
                .EndChildElements()
                .Compose()
            ;

            var col = System.Drawing.Color.FromArgb(255, ColorUtil.ColorR(waypoint.Color), ColorUtil.ColorG(waypoint.Color), ColorUtil.ColorB(waypoint.Color));

            SingleComposer.ColorListPickerSetValue("colorpicker", colorIndex);
            SingleComposer.IconListPickerSetValue("iconpicker", iconIndex);

            SingleComposer.GetTextInput("nameInput").SetValue(waypoint.Title);
            SingleComposer.GetSwitch("pinnedSwitch").SetValue(waypoint.Pinned);

        }

        private void onIconSelected(int index)
        {
            waypoint.Icon = icons[index];
        }

        private void onColorSelected(int index)
        {
            waypoint.Color = colors[index];
        }

        private void onPinnedToggled(bool t1)
        {

        }

        private void onIconSelectionChanged(string code, bool selected)
        {

        }

        private bool onDelete()
        {
            capi.SendChatMessage(string.Format("/waypoint remove {0}", wpIndex));
            TryClose();
            return true;
        }

        private bool onSave()
        {
            string name = SingleComposer.GetTextInput("nameInput").GetText();
            bool pinned = SingleComposer.GetSwitch("pinnedSwitch").On;

            capi.SendChatMessage(string.Format("/waypoint modify {0} {1} {2} {3} {4}", wpIndex, ColorUtil.Int2Hex(waypoint.Color), waypoint.Icon, pinned, name));
            TryClose();
            return true;
        }

        private bool onCancel()
        {
            TryClose();
            return true;
        }

        private void onNameChanged(string t1)
        {
            SingleComposer.GetButton("saveButton").Enabled = (t1.Trim() != "");
        }


        public override bool CaptureAllInputs()
        {
            return IsOpened();
        }
        public override bool DisableMouseGrab => true;

        public override void OnMouseDown(MouseEvent args)
        {
            base.OnMouseDown(args);

            args.Handled = true;
        }

        public override void OnMouseUp(MouseEvent args)
        {
            base.OnMouseUp(args);
            args.Handled = true;
        }

        public override void OnMouseMove(MouseEvent args)
        {
            base.OnMouseMove(args);
            args.Handled = true;
        }

        public override void OnMouseWheel(MouseWheelEventArgs args)
        {
            base.OnMouseWheel(args);
            args.SetHandled(true);
        }

    }
}
