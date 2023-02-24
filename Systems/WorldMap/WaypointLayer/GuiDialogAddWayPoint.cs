using Cairo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class GuiDialogAddWayPoint : GuiDialogGeneric
    {
        public override bool PrefersUngrabbedMouse => true;

        EnumDialogType dialogType = EnumDialogType.Dialog;
        public override EnumDialogType DialogType => dialogType;
        
        internal Vec3d WorldPos;

        public override double DrawOrder => 0.2;

        int[] colors;
        string[] icons;
        string curIcon;
        string curColor;
        bool autoSuggest = true;
        bool ignoreNextAutosuggestDisable;

        public GuiDialogAddWayPoint(ICoreClientAPI capi, WaypointMapLayer wml) : base("", capi)
        {
            icons =  wml.WaypointIcons.Keys.ToArray();
            colors = wml.WaypointColors.ToArray();

            ComposeDialog();
        }

        public override bool TryOpen()
        {
            ComposeDialog();
            return base.TryOpen();
        }

        private void ComposeDialog()
        {
            ElementBounds leftColumn = ElementBounds.Fixed(0, 28, 90, 25);
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

            curIcon = icons[0];
            curColor = ColorUtil.Int2Hex(colors[0]);

            SingleComposer = capi.Gui
                .CreateCompo("worldmap-addwp", dialogBounds)
                .AddShadedDialogBG(bgBounds, false)
                .AddDialogTitleBar(Lang.Get("Add waypoint"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("Name"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.FlatCopy())
                    .AddTextInput(rightColumn = rightColumn.FlatCopy().WithFixedWidth(200), onNameChanged, CairoFont.TextInput(), "nameInput")

                    .AddStaticText(Lang.Get("Pinned"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 9))
                    .AddSwitch(onPinnedToggled, rightColumn = rightColumn.BelowCopy(0, 5).WithFixedWidth(200), "pinnedSwitch")

                    .AddRichtext(Lang.Get("waypoint-color"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 5))
                    .AddColorListPicker(colors, onColorSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize, colorIconSize), 270, "colorpicker")

                    .AddStaticText(Lang.Get("Icon"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.WithFixedPosition(0, leftColumn.fixedY + leftColumn.fixedHeight).WithFixedWidth(200).BelowCopy(0, 0))
                    .AddIconListPicker(icons, onIconSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize+5, colorIconSize+5), 270, "iconpicker")

                    .AddSmallButton(Lang.Get("Cancel"), onCancel, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100), EnumButtonStyle.Normal)
                    .AddSmallButton(Lang.Get("Save"), onSave, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal, key: "saveButton")
                .EndChildElements()
                .Compose()
            ;

            SingleComposer.GetButton("saveButton").Enabled = false;

            SingleComposer.ColorListPickerSetValue("colorpicker", 0);
            SingleComposer.IconListPickerSetValue("iconpicker", 0);
        }

        private void onIconSelected(int index)
        {
            curIcon = icons[index];
            autoSuggestName();
        }


        private void onColorSelected(int index)
        {
            curColor = ColorUtil.Int2Hex(colors[index]);
            autoSuggestName();
        }

        private void onPinnedToggled(bool on)
        {
            
        }

        private void autoSuggestName()
        {
            if (!autoSuggest) return;
            
            var textElem = SingleComposer.GetTextInput("nameInput");

            ignoreNextAutosuggestDisable = true;
            if (Lang.HasTranslation("wpSuggestion-" + curIcon + "-" + curColor))
            {
                textElem.SetValue(Lang.Get("wpSuggestion-" + curIcon + "-" + curColor));
            }
            else if (Lang.HasTranslation("wpSuggestion-" + curIcon))
            {
                textElem.SetValue(Lang.Get("wpSuggestion-" + curIcon));
            }
            else
            {
                textElem.SetValue("");
            }
            
        }

        private bool onSave()
        {
            string name = SingleComposer.GetTextInput("nameInput").GetText();
            bool pinned = SingleComposer.GetSwitch("pinnedSwitch").On;

            capi.SendChatMessage(string.Format("/waypoint addati {0} ={1} ={2} ={3} {4} {5} {6}", curIcon, WorldPos.X.ToString(GlobalConstants.DefaultCultureInfo), WorldPos.Y.ToString(GlobalConstants.DefaultCultureInfo), WorldPos.Z.ToString(GlobalConstants.DefaultCultureInfo), pinned, curColor, name));
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

            if (!ignoreNextAutosuggestDisable)
            {
                autoSuggest = t1.Length == 0;
            }

            ignoreNextAutosuggestDisable = false;
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
