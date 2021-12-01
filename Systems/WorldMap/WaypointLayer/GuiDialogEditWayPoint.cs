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

        int color;
        string colorText;

        Waypoint waypoint;
        int wpIndex;

        internal Vec3d WorldPos;

        public override double DrawOrder => 0.2;


        static ILookup<int, System.Drawing.Color> colorLookup = typeof(System.Drawing.Color)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (System.Drawing.Color)f.GetValue(null, null))
            .Where(c => c.IsNamedColor)
            .ToLookup(c => c.ToArgb());



        public GuiDialogEditWayPoint(ICoreClientAPI capi, Waypoint waypoint, int index) : base("", capi)
        {
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

            string[] names = new string[] { "circle", "bee", "cave", "home", "ladder", "pick", "rocks", "ruins", "spiral", "star1", "star2", "trader", "vessel" };
            int index = names.IndexOf(waypoint.Icon);
            if (index < 0) index = 0;

            SingleComposer = capi.Gui
                .CreateCompo("worldmap-modwp", dialogBounds)
                .AddShadedDialogBG(bgBounds, false)
                .AddDialogTitleBar(Lang.Get("Modify waypoint"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("Name"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.FlatCopy())
                    .AddTextInput(rightColumn = rightColumn.FlatCopy().WithFixedWidth(200), onNameChanged, CairoFont.TextInput(), "nameInput")

                    .AddRichtext(Lang.Get("waypoint-color"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 5))
                    .AddTextInput(rightColumn = rightColumn.BelowCopy(0, 5).WithFixedWidth(150), onColorChanged, CairoFont.TextInput(), "colorInput")
                    .AddDynamicCustomDraw(rightColumn.FlatCopy().WithFixedWidth(30).WithFixedOffset(160, 0), onDrawColorRect, "colorRect")

                    .AddStaticText(Lang.Get("Icon"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 9))
                    .AddDropDown(
                        names,
                        new string[] { "<icon name=\"wpCircle\">", "<icon name=\"wpBee\">", "<icon name=\"wpCave\">", "<icon name=\"wpHome\">", "<icon name=\"wpLadder\">", "<icon name=\"wpPick\">", "<icon name=\"wpRocks\">", "<icon name=\"wpRuins\">", "<icon name=\"wpSpiral\">", "<icon name=\"wpStar1\">", "<icon name=\"wpStar2\">", "<icon name=\"wpTrader\">", "<icon name=\"wpVessel\">" },
                        index,
                        onIconSelectionChanged,
                        rightColumn = rightColumn.BelowCopy(0, 5).WithFixedWidth(60),
                        CairoFont.WhiteSmallishText(),
                        "iconInput"
                    )

                    .AddStaticText(Lang.Get("Pinned"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 9))
                    .AddSwitch(onPinnedToggled, rightColumn = rightColumn.BelowCopy(0, 5).WithFixedWidth(200), "pinnedSwitch")

                    .AddSmallButton(Lang.Get("Cancel"), onCancel, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100), EnumButtonStyle.Normal)
                    .AddSmallButton(Lang.Get("Delete"), onDelete, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.CenterFixed), EnumButtonStyle.Normal)
                    .AddSmallButton(Lang.Get("Save"), onSave, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal, key: "saveButton")
                .EndChildElements()
                .Compose()
            ;

            var col = System.Drawing.Color.FromArgb(255, ColorUtil.ColorR(waypoint.Color), ColorUtil.ColorG(waypoint.Color), ColorUtil.ColorB(waypoint.Color));


            if (colorLookup[waypoint.Color].Count() > 0)
            {
                SingleComposer.GetTextInput("colorInput").SetValue(colorLookup[waypoint.Color].First().ToKnownColor().ToString().ToLowerInvariant());
            }
            else
            {
                SingleComposer.GetTextInput("colorInput").SetValue(ColorUtil.Int2Hex(waypoint.Color));
            }

            SingleComposer.GetTextInput("nameInput").SetValue(waypoint.Title);
            SingleComposer.GetSwitch("pinnedSwitch").SetValue(waypoint.Pinned);

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
            string icon = SingleComposer.GetDropDown("iconInput").SelectedValue;
            capi.SendChatMessage(string.Format("/waypoint modify {0} {1} {2} {3} {4}", wpIndex, colorText, icon, pinned, name));
            TryClose();
            return true;
        }

        private bool onCancel()
        {
            TryClose();
            return true;
        }

        private void onDrawColorRect(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            ctx.Rectangle(0, 0, GuiElement.scaled(25), GuiElement.scaled(25));
            ctx.SetSourceRGBA(ColorUtil.ToRGBADoubles(color));

            ctx.FillPreserve();

            ctx.SetSourceRGBA(GuiStyle.DialogBorderColor);
            ctx.Stroke();
        }

        private void onNameChanged(string t1)
        {
            bool hasValidColor = (SingleComposer.GetTextInput("colorInput").Font.Color != GuiStyle.ErrorTextColor);
            SingleComposer.GetButton("saveButton").Enabled = (t1.Trim() != "") && hasValidColor;
        }

        private void onColorChanged(string colorstring)
        {
            int transparent = System.Drawing.Color.Transparent.ToArgb();
            int fullAlpha = System.Drawing.Color.Black.ToArgb();

            int? newColor = null;
            if (colorstring.StartsWith("#"))
            {
                if (colorstring.Length == 7)
                {
                    string s = colorstring.Substring(1);
                    try { newColor = Int32.Parse(s, NumberStyles.HexNumber) | fullAlpha; }
                    // We can ignore the exception because if one occurred,
                    // the newColor variable will still be set to null.
                    catch (Exception) { }
                }
            }
            else
            {
                var knownColor = System.Drawing.Color.FromName(colorstring);
                // Unknown color string will return a transparent color.
                if (knownColor.A == 0xFF)
                    newColor = knownColor.ToArgb();
            }

            color = newColor ?? transparent;
            colorText = colorstring;

            SingleComposer.GetTextInput("colorInput").Font.Color = newColor.HasValue
                ? GuiStyle.DialogDefaultTextColor : GuiStyle.ErrorTextColor;

            bool hasName = (SingleComposer.GetTextInput("nameInput").GetText().Trim() != "");
            SingleComposer.GetButton("saveButton").Enabled = newColor.HasValue && hasName;

            SingleComposer.GetCustomDraw("colorRect").Redraw();
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
