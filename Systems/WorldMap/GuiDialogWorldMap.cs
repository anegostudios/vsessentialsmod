using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public delegate void OnViewChangedDelegate(List<Vec2i> nowVisibleChunks, List<Vec2i> nowHiddenChunks);


    public class GuiDialogWorldMap : GuiDialogGeneric
    {
        public override bool PrefersUngrabbedMouse => true;

        EnumDialogType dialogType = EnumDialogType.HUD;
        public override EnumDialogType DialogType => dialogType; 
        public List<MapComponent> mapComponents = new List<MapComponent>();


        public GuiElementMap mapElem;
        OnViewChangedDelegate viewChanged;
        long listenerId;
        GuiElementHoverText hoverTextElem;
        bool requireRecompose = false;

        int mapWidth=1200, mapHeight=800;

        public override double DrawOrder => 0.11;

        public GuiDialogWorldMap(OnViewChangedDelegate viewChanged, ICoreClientAPI capi) : base("", capi)
        {
            this.viewChanged = viewChanged;
            ComposeDialog();

            capi.RegisterCommand("worldmapsize", "Set the size of the world map dialog", "width height", onCmdMapSize);
        }

        private void onCmdMapSize(int groupId, CmdArgs args)
        {
            mapWidth = (int)args.PopInt(1200);
            mapHeight = (int)args.PopInt(800);
            ComposeDialog();

            capi.ShowChatMessage(string.Format("Map size {0}x{1} set", mapWidth, mapHeight));
        }

        public override bool TryOpen()
        {
            ComposeDialog();
            return base.TryOpen();
        }

        private void ComposeDialog()
        {
            ElementBounds mapBounds = ElementBounds.Fixed(0, 28, mapWidth, mapHeight);
            ElementBounds layerList = mapBounds.RightCopy().WithFixedSize(1, 350);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(3);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(mapBounds, layerList);

            ElementBounds dialogBounds =
                ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            if (dialogType == EnumDialogType.HUD)
            {
                mapBounds = ElementBounds.Fixed(0, 0, 250, 250);

                bgBounds = ElementBounds.Fill.WithFixedPadding(2);
                bgBounds.BothSizing = ElementSizing.FitToChildren;
                bgBounds.WithChildren(mapBounds);

                dialogBounds =
                    ElementStdBounds.AutosizedMainDialog
                    .WithAlignment(EnumDialogArea.RightTop)
                    .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, GuiStyle.DialogToScreenPadding);
            }

            Vec3d centerPos = capi.World.Player.Entity.Pos.XYZ;

            if (SingleComposer != null)
            {
                mapElem.mapComponents = null; // Let's not dispose that
                SingleComposer.Dispose();
            }

            SingleComposer = capi.Gui
                .CreateCompo("worldmap", dialogBounds)
                .AddShadedDialogBG(bgBounds, false)
                .AddIf(dialogType == EnumDialogType.Dialog)
                    .AddDialogTitleBar("World Map", OnTitleBarClose)
                    .AddInset(mapBounds, 2)
                .EndIf()
                .BeginChildElements(bgBounds)
                    .AddHoverText("", CairoFont.WhiteDetailText(), 350, mapBounds.FlatCopy(), "hoverText")
                    .AddInteractiveElement(new GuiElementMap(mapComponents, centerPos, capi, mapBounds), "mapElem")
                .EndChildElements()
                .Compose()
            ;
            SingleComposer.OnRecomposed += SingleComposer_OnRecomposed;

            mapElem = SingleComposer.GetElement("mapElem") as GuiElementMap;

            mapElem.viewChanged = viewChanged;
            mapElem.ZoomAdd(1, 0.5f, 0.5f);
            

            hoverTextElem = SingleComposer.GetHoverText("hoverText");
            hoverTextElem.SetAutoWidth(true);

            if (listenerId != 0)
            {
                capi.Event.UnregisterGameTickListener(listenerId);
            }

            listenerId = capi.Event.RegisterGameTickListener(
                (dt) => {
                    mapElem.EnsureMapFullyLoaded();
                    if (requireRecompose)
                    {
                        TryClose();
                        TryOpen();
                        requireRecompose = false;
                    }
                }
            , 100);
        }

        private void SingleComposer_OnRecomposed()
        {
            requireRecompose = true;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();

            if (mapElem != null) mapElem.chunkViewBoundsBefore = new Cuboidi();
            //mapComponents.Clear();
            //mmapElem.EnsureMapFullyLoaded();

            OnMouseMove(new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));
        }


        private void OnTitleBarClose()
        {
            TryClose();
        }

        public override bool TryClose()
        {
            if (DialogType == EnumDialogType.Dialog && capi.Settings.Bool["showMinimapHud"])
            {
                Open(EnumDialogType.HUD);
                return false;
            }

            return base.TryClose();
        }

        public void Open(EnumDialogType type)
        {
            this.dialogType = type;
            opened = false;
            TryOpen();
        }
        
        
        
        public override void OnGuiClosed()
        {
            base.OnGuiClosed();

            capi.Event.UnregisterGameTickListener(listenerId);
            listenerId = 0;

            foreach (MapComponent cmp in mapComponents)
            {
                cmp.Dispose();
            }

            mapComponents.Clear();
        }


        public override void Dispose()
        {
            base.Dispose();

            capi.Event.UnregisterGameTickListener(listenerId);
            listenerId = 0;

            foreach (MapComponent cmp in mapComponents)
            {
                cmp.Dispose();
            }

            mapComponents.Clear();
        }


        Vec3d hoveredWorldPos = new Vec3d();
        public override void OnMouseMove(MouseEvent args)
        {
            base.OnMouseMove(args);

            if (SingleComposer != null && SingleComposer.Bounds.PointInside(args.X, args.Y))
            {
                loadWorldPos(args.X, args.Y, ref hoveredWorldPos);

                double yAbs = hoveredWorldPos.Y;
                hoveredWorldPos.Sub(capi.World.DefaultSpawnPosition.AsBlockPos);
                hoveredWorldPos.Y = yAbs;

                StringBuilder hoverText = new StringBuilder();
                hoverText.AppendLine(string.Format("{0}, {1}, {2}", (int)hoveredWorldPos.X, (int)hoveredWorldPos.Y, (int)hoveredWorldPos.Z));

                foreach (MapComponent cmp in mapComponents)
                {
                    cmp.OnMouseMove(args, mapElem, hoverText);
                }

                string text = hoverText.ToString().TrimEnd();

                hoverTextElem.SetNewText(text);
            }
        }

        void loadWorldPos(double mouseX, double mouseY, ref Vec3d worldPos)
        {
            double x = mouseX - SingleComposer.Bounds.absX;
            double y = mouseY - SingleComposer.Bounds.absY - (dialogType == EnumDialogType.Dialog ? GuiElement.scaled(30) : 0); // no idea why the 30 :/

            mapElem.TranslateViewPosToWorldPos(new Vec2f((float)x, (float)y), ref worldPos);
            worldPos.Y++;
        }

        public override void OnMouseDown(MouseEvent args)
        {
            base.OnMouseDown(args);
        }


        public override void OnRenderGUI(float deltaTime)
        {
            base.OnRenderGUI(deltaTime);
            capi.Render.CheckGlError("map-rend2d");
        }

        public override void OnFinalizeFrame(float dt)
        {
            base.OnFinalizeFrame(dt);
            capi.Render.CheckGlError("map-fina");

            bool showHover = SingleComposer.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY) && Focused;

            hoverTextElem.SetVisible(showHover);
            hoverTextElem.SetAutoDisplay(showHover);
        }


        GuiDialogAddWayPoint addWpDlg;
        public override void OnMouseUp(MouseEvent args)
        {
            if (!SingleComposer.Bounds.PointInside(args.X, args.Y))
            {
                base.OnMouseUp(args);
                return;
            }

            foreach (MapComponent cmp in mapComponents)
            {
                cmp.OnMouseUpOnElement(args, mapElem);
                if (args.Handled) return;
            }


            if (args.Button == API.Common.EnumMouseButton.Right)
            {
                Vec3d wpPos = new Vec3d();
                loadWorldPos(args.X, args.Y, ref wpPos);

                if (addWpDlg != null)
                {
                    addWpDlg.TryClose();
                    addWpDlg.Dispose();
                }
                addWpDlg = new GuiDialogAddWayPoint(capi);
                addWpDlg.WorldPos = wpPos;
                addWpDlg.TryOpen();
            }

            base.OnMouseUp(args);
        }
    }
}
