using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class GuiDialogAddWayPoint : GuiDialogGeneric
    {
        public override bool PrefersUngrabbedMouse => prefersUngrabbedMouse = true;

        public override EnumDialogType DialogType => dialogType;

        public override bool DisableMouseGrab => true;

        public Vec3d? WorldPos { get; set; }

        public override double DrawOrder => 0.2;

        public static Dictionary<string, string> SavedNames { get; set; } = [];

        public GuiDialogAddWayPoint(ICoreClientAPI clientApi, WaypointMapLayer mapLayer) : base("", clientApi)
        {
            GetIconsAndColors(mapLayer);
        }

        public override bool CaptureAllInputs()
        {
            return IsOpened();
        }

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

        public override bool TryOpen()
        {
            ComposeDialog();
            SetToolTipText();
            return base.TryOpen();
        }


        public static void LoadSavedNames(ILogger logger)
        {
            string path = Path.Combine(GamePaths.Config, savedNamesFileName);
            if (!File.Exists(path))
            {
                return;
            }

            JsonObject parsedFile = new(JObject.Parse(File.ReadAllText(path)));

            Dictionary<string, string>? loadedNames = null;
            try
            {
                loadedNames = parsedFile.AsObject<Dictionary<string, string>>();
            }
            catch (Exception exception)
            {
                logger.Error($"[GuiDialogAddWayPoint] Error while loading saved file names from file '{savedNamesFileName}':\n{exception}");
            }
            if (loadedNames == null)
            {
                logger.Error($"[GuiDialogAddWayPoint] Error while loading saved file names from file '{savedNamesFileName}'");
                return;
            }

            SavedNames.AddRange(loadedNames);
        }

        public static void StoreSavedNames(ILogger logger)
        {
            FileInfo file = new(Path.Combine(GamePaths.Config, savedNamesFileName));
            if (file.Directory == null)
            {
                logger.Error($"[GuiDialogAddWayPoint] Error while storing saved file names to file '{savedNamesFileName}': ");
                return;
            }

            GamePaths.EnsurePathExists(file.Directory.FullName);
            string json = JsonConvert.SerializeObject(SavedNames, Formatting.Indented);
            File.WriteAllText(file.FullName, json);
        }


        protected const string composerId = "worldmap-addwp";
        protected const string colorPickerId = "colorPicker";
        protected const string iconPickerId = "iconPicker";
        protected const string nameInputId = "nameInput";
        protected const string saveButtonId = "saveButton";
        protected const string pinnedSwitchId = "pinnedSwitch";
        protected const string savedNamesFileName = "waypoints-names.json";
        protected const string nameSuggestionPrefix = "wpSuggestion";
        protected const int colorIconSize = 22;

        protected int[] colors = [];
        protected string[] icons = [];
        protected string? selectedIcon;
        protected string? selectedColor;
        protected bool autoSuggest = true;
        protected bool ignoreNextAutosuggestDisable;
        protected EnumDialogType dialogType = EnumDialogType.Dialog;
        protected bool prefersUngrabbedMouse = true;
        protected bool currentNameWasSuggested = false;
        protected int lastSuggestedNameLength = 0;
        protected bool ignoreNextOnNameChanged = false;


        protected void GetIconsAndColors(WaypointMapLayer mapLayer)
        {
            icons = mapLayer.WaypointIcons.Keys.ToArray();
            colors = mapLayer.WaypointColors.ToArray();
        }


        protected virtual void ComposeDialog()
        {
            ElementBounds leftColumn = ElementBounds.Fixed(0, 28, 90, 25);
            ElementBounds rightColumn = leftColumn.RightCopy();

            ElementBounds buttonRow = ElementBounds.Fixed(0, 28, 360, 25);

            ElementBounds backgroundBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);

            backgroundBounds.BothSizing = ElementSizing.FitToChildren;
            backgroundBounds.WithChildren(leftColumn, rightColumn);

            ElementBounds dialogBounds =
                ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            SingleComposer?.Dispose();

            selectedIcon = icons[0];
            selectedColor = ColorUtil.Int2Hex(colors[0]);

            SingleComposer = capi.Gui.CreateCompo(composerId, dialogBounds);

#pragma warning disable S1121 // Assignments should not be made from within sub-expressions // Thats how Tyron does it, will leave to him to deal with
            SingleComposer.AddShadedDialogBG(backgroundBounds, false)
                .AddDialogTitleBar(Lang.Get("Add waypoint"), () => TryClose())
                .BeginChildElements(backgroundBounds)
                    .AddStaticText(Lang.Get("Name"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.FlatCopy())
                    .AddTextInput(rightColumn = rightColumn.FlatCopy().WithFixedWidth(200), OnNameChanged, CairoFont.TextInput(), nameInputId)

                    .AddStaticText(Lang.Get("Pinned"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 9))
                    .AddSwitch(OnPinnedToggled, rightColumn.BelowCopy(0, 5).WithFixedWidth(200), pinnedSwitchId)

                    .AddRichtext(Lang.Get("waypoint-color"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 5))
                    .AddColorListPicker(colors, OnColorSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize, colorIconSize), 270, colorPickerId)

                    .AddStaticText(Lang.Get("Icon"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.WithFixedPosition(0, leftColumn.fixedY + leftColumn.fixedHeight).WithFixedWidth(200).BelowCopy(0, 0))
                    .AddIconListPicker(icons, OnIconSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize + 5, colorIconSize + 5), 270, iconPickerId)

                    .AddSmallButton(Lang.Get("Cancel"), OnCancel, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100), EnumButtonStyle.Normal)
                    .AddSmallButton(Lang.Get("Save"), OnSave, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal, key: saveButtonId)
                .EndChildElements();
#pragma warning restore S1121

            SingleComposer.Compose();

            SingleComposer.GetButton(saveButtonId).Enabled = false;

            SingleComposer.ColorListPickerSetValue(colorPickerId, 0);
            SingleComposer.IconListPickerSetValue(iconPickerId, 0);
        }

        protected virtual void OnIconSelected(int index)
        {
            if (index < colors.Length && index >= 0)
            {
                selectedIcon = icons[index];
            }
            else
            {
                selectedIcon = icons[0];
            }

            AutoSuggestName();
            SetToolTipText();
        }

        protected virtual void OnColorSelected(int index)
        {
            if (index < colors.Length && index >= 0)
            {
                selectedColor = ColorUtil.Int2Hex(colors[index]);
            }
            else
            {
                selectedColor = ColorUtil.Int2Hex(colors[0]);
            }

            AutoSuggestName();
        }

        protected virtual void OnPinnedToggled(bool on)
        {

        }

        protected virtual void AutoSuggestName()
        {
            if (!autoSuggest) return;

            GuiElementTextInput nameInputElement = SingleComposer.GetTextInput(nameInputId);

            ignoreNextAutosuggestDisable = true;
            currentNameWasSuggested = true;

            string? savedName = TryGetSelectedSavedName();
            if (savedName != null)
            {
                lastSuggestedNameLength = savedName.Length;
                nameInputElement.SetValue(savedName);
                return;
            }

            if (Lang.HasTranslation(nameSuggestionPrefix + "-" + selectedIcon + "-" + selectedColor))
            {
                string newName = Lang.Get(nameSuggestionPrefix + "-" + selectedIcon + "-" + selectedColor);
                lastSuggestedNameLength = newName.Length;
                nameInputElement.SetValue(newName);
            }
            else if (Lang.HasTranslation(nameSuggestionPrefix + "-" + selectedIcon))
            {
                string newName = Lang.Get(nameSuggestionPrefix + "-" + selectedIcon);
                lastSuggestedNameLength = newName.Length;
                nameInputElement.SetValue(newName);
            }
            else
            {
                lastSuggestedNameLength = 0;
                nameInputElement.SetValue("");
            }

            lastSuggestedNameLength = nameInputElement.GetText().Length;
        }

        protected virtual string? TryGetSelectedSavedName()
        {
            if (selectedIcon == null || selectedColor == null) return null;

            return TryGetSavedName(selectedIcon, selectedColor);
        }

        protected virtual string? TryGetSavedName(string icon, string color)
        {
            string key = GetSavedWaypointKey(icon, color);
            if (SavedNames.TryGetValue(key, out string? name))
            {
                return name;
            }

            return null;
        }

        protected virtual void SaveWaypointName(string name)
        {
            if (selectedIcon == null || selectedColor == null) return;

            string key = GetSavedWaypointKey(selectedIcon, selectedColor);

            SavedNames[key] = name;
        }

        protected virtual void SetToolTipText()
        {
            if (selectedIcon == null) return;

            for (int colorIndex = 0; colorIndex < colors.Length; colorIndex++)
            {
                GuiElementColorListPicker? picker = null;
                try
                {
                    picker = SingleComposer.GetColorListPicker($"{colorPickerId}-{colorIndex}");
                }
                catch (Exception exception)
                {
                    capi.Logger.Error($"[GuiDialogAddWayPoint] Error while setting tooltip text for color picker: {exception}");
                    Debug.WriteLine(exception);
                }

                if (picker == null)
                {
                    continue;
                }

                string? waypointName = TryGetSavedName(selectedIcon, ColorUtil.Int2Hex(colors[colorIndex]));

                if (waypointName != null)
                {
                    picker.ShowToolTip = true;
                    picker.TooltipText = waypointName;
                }
                else
                {
                    picker.ShowToolTip = false;
                    picker.TooltipText = "";
                }
            }
        }

        protected virtual string GetSavedWaypointKey(string icon, string color) => $"{icon}-{color}";

        protected virtual bool OnSave()
        {
            if (WorldPos == null) return false;

            string name = SingleComposer.GetTextInput(nameInputId).GetText();
            bool pinned = SingleComposer.GetSwitch(pinnedSwitchId).On;

            string command = string.Format(
                "/waypoint addati {0} ={1} ={2} ={3} {4} {5} {6}",
                selectedIcon,
                WorldPos.X.ToString(GlobalConstants.DefaultCultureInfo),
                WorldPos.Y.ToString(GlobalConstants.DefaultCultureInfo),
                WorldPos.Z.ToString(GlobalConstants.DefaultCultureInfo),
                pinned,
                selectedColor,
                name);
            capi.SendChatMessage(command);

            TryClose();

            SaveWaypointName(name);

            StoreSavedNames(capi.Logger);

            return true;
        }

        protected virtual bool OnCancel()
        {
            TryClose();
            return true;
        }

        protected virtual void OnNameChanged(string newName)
        {
            if (ignoreNextOnNameChanged)
            {
                ignoreNextOnNameChanged = false;
                return;
            }

            SingleComposer.GetButton(saveButtonId).Enabled = (newName.Trim() != "");

            if (!ignoreNextAutosuggestDisable)
            {
                autoSuggest = newName.Length == 0;
                currentNameWasSuggested = false;
            }

            if (newName.Length < lastSuggestedNameLength)
            {
                GuiElementTextInput nameInputElement = SingleComposer.GetTextInput(nameInputId);
                lastSuggestedNameLength = 0;
                ignoreNextOnNameChanged = true;
                nameInputElement.SetValue("");
            }

            ignoreNextAutosuggestDisable = false;
        }
    }
}
