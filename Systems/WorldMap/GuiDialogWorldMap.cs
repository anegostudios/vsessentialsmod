using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public delegate void OnViewChangedDelegate(List<Vec2i> nowVisibleChunks, List<Vec2i> nowHiddenChunks);


    public class GuiDialogWorldMap : GuiDialogGeneric
    {
        public override bool PrefersUngrabbedMouse => true;

        EnumDialogType dialogType = EnumDialogType.HUD;
        public override EnumDialogType DialogType => dialogType;
        


        OnViewChangedDelegate viewChanged;
        long listenerId;
        bool requireRecompose = false;

        int mapWidth=1200, mapHeight=800;

        GuiComposer fullDialog;
        GuiComposer hudDialog;

        public override double DrawOrder => dialogType == EnumDialogType.HUD ? 0.07 : 0.11;

        public GuiDialogWorldMap(OnViewChangedDelegate viewChanged, ICoreClientAPI capi) : base("", capi)
        {
            this.viewChanged = viewChanged;
            fullDialog = ComposeDialog(EnumDialogType.Dialog);
            hudDialog = ComposeDialog(EnumDialogType.HUD);

            capi.RegisterCommand("worldmapsize", "Set the size of the world map dialog", "width height", onCmdMapSize);
        }


        private void onCmdMapSize(int groupId, CmdArgs args)
        {
            if (args.Length == 0)
            {
                capi.ShowChatMessage(string.Format("Current map size: {0}x{1}", mapWidth, mapHeight));
                return;
            }

            mapWidth = (int)args.PopInt(1200);
            mapHeight = (int)args.PopInt(800);
            fullDialog = ComposeDialog(EnumDialogType.Dialog);
            capi.ShowChatMessage(string.Format("Map size {0}x{1} set", mapWidth, mapHeight));
        }


        private GuiComposer ComposeDialog(EnumDialogType dlgType)
        {
            GuiComposer compo;

            ElementBounds mapBounds = ElementBounds.Fixed(0, 28, mapWidth, mapHeight);
            ElementBounds layerList = mapBounds.RightCopy().WithFixedSize(1, 350);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(3);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(mapBounds, layerList);

            ElementBounds dialogBounds =
                ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            if (dlgType == EnumDialogType.HUD)
            {
                mapBounds = ElementBounds.Fixed(0, 0, 250, 250);

                bgBounds = ElementBounds.Fill.WithFixedPadding(2);
                bgBounds.BothSizing = ElementSizing.FitToChildren;
                bgBounds.WithChildren(mapBounds);

                dialogBounds =
                    ElementStdBounds.AutosizedMainDialog
                    .WithAlignment(EnumDialogArea.RightTop)
                    .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, GuiStyle.DialogToScreenPadding);

                compo = hudDialog;
            } else
            {
                compo = fullDialog;
            }

            Cuboidd beforeBounds = null;
            
            if (compo != null)
            {
                beforeBounds = (compo.GetElement("mapElem") as GuiElementMap)?.CurrentBlockViewBounds;
                compo.Dispose();
            }

            compo = capi.Gui
                .CreateCompo("worldmap" + dlgType, dialogBounds)
                .AddShadedDialogBG(bgBounds, false)
                .AddIf(dlgType == EnumDialogType.Dialog)
                    .AddDialogTitleBar(Lang.Get("World Map"), OnTitleBarClose)
                    .AddInset(mapBounds, 2)
                .EndIf()
                .BeginChildElements(bgBounds)
                    .AddHoverText("", CairoFont.WhiteDetailText(), 350, mapBounds.FlatCopy(), "hoverText")
                    .AddInteractiveElement(new GuiElementMap(capi.ModLoader.GetModSystem<WorldMapManager>().MapLayers, capi, this, mapBounds, dlgType == EnumDialogType.HUD), "mapElem")
                .EndChildElements()
                .Compose()
            ;

            compo.OnComposed += OnRecomposed;

            GuiElementMap mapElem = compo.GetElement("mapElem") as GuiElementMap;
            if (beforeBounds != null)
            {
                mapElem.chunkViewBoundsBefore = beforeBounds.ToCuboidi().Div(capi.World.BlockAccessor.ChunkSize);
            }
            mapElem.viewChanged = viewChanged;
            mapElem.ZoomAdd(1, 0.5f, 0.5f);



            var hoverTextElem = compo.GetHoverText("hoverText");
            hoverTextElem.SetAutoWidth(true);

            if (listenerId == 0)
            {
                listenerId = capi.Event.RegisterGameTickListener(
                    (dt) =>
                    {
                        if (!IsOpened()) return;

                        GuiElementMap singlec = SingleComposer.GetElement("mapElem") as GuiElementMap;
                        singlec?.EnsureMapFullyLoaded();

                        if (requireRecompose)
                        {
                            var dlgtype = dialogType;
                            capi.ModLoader.GetModSystem<WorldMapManager>().ToggleMap(dlgtype);
                            capi.ModLoader.GetModSystem<WorldMapManager>().ToggleMap(dlgtype);
                            requireRecompose = false;
                        }


                    }
                , 100);
            }

            capi.World.FrameProfiler.Mark("composeworldmap");
            return compo;
        }

        private void OnRecomposed()
        {
            requireRecompose = true;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();

            if (dialogType == EnumDialogType.HUD)
            {
                SingleComposer = hudDialog;
            } else
            {
                SingleComposer = fullDialog;
            }

            GuiElementMap mapElem = SingleComposer.GetElement("mapElem") as GuiElementMap;
            if (mapElem != null) mapElem.chunkViewBoundsBefore = new Cuboidi();

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

        }


        public override void Dispose()
        {
            base.Dispose();

            capi.Event.UnregisterGameTickListener(listenerId);
            listenerId = 0;

            fullDialog.Dispose();
            hudDialog.Dispose();
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

                var mpc = SingleComposer.GetElement("mapElem") as GuiElementMap;
                var hoverTextElem = SingleComposer.GetHoverText("hoverText");
                
                foreach (MapLayer layer in mpc.mapLayers)
                {
                    layer.OnMouseMoveClient(args, mpc, hoverText);
                }

                string text = hoverText.ToString().TrimEnd();
                hoverTextElem.SetNewText(text);
            }
        }

        void loadWorldPos(double mouseX, double mouseY, ref Vec3d worldPos)
        {
            double x = mouseX - SingleComposer.Bounds.absX;
            double y = mouseY - SingleComposer.Bounds.absY - (dialogType == EnumDialogType.Dialog ? GuiElement.scaled(30) : 0); // no idea why the 30 :/

            var mpc = SingleComposer.GetElement("mapElem") as GuiElementMap;
            mpc.TranslateViewPosToWorldPos(new Vec2f((float)x, (float)y), ref worldPos);
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
            var hoverTextElem = SingleComposer.GetHoverText("hoverText");

            hoverTextElem.SetVisible(showHover);
            hoverTextElem.SetAutoDisplay(showHover);
        }

        public void TranslateWorldPosToViewPos(Vec3d worldPos, ref Vec2f viewPos)
        {
            var mpc = SingleComposer.GetElement("mapElem") as GuiElementMap;
            mpc.TranslateWorldPosToViewPos(worldPos, ref viewPos);
        }

        GuiDialogAddWayPoint addWpDlg;
        public override void OnMouseUp(MouseEvent args)
        {
            if (!SingleComposer.Bounds.PointInside(args.X, args.Y))
            {
                base.OnMouseUp(args);
                return;
            }

            var mpc = SingleComposer.GetElement("mapElem") as GuiElementMap;

            foreach (MapLayer ml in mpc.mapLayers)
            {
                ml.OnMouseUpClient(args, mpc);
                if (args.Handled) return;
            }

            if (args.Button == EnumMouseButton.Right)
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
                addWpDlg.OnClosed += () => capi.Gui.RequestFocus(this);
            }

            base.OnMouseUp(args);
        }

        public override bool ShouldReceiveKeyboardEvents()
        {
            return base.ShouldReceiveKeyboardEvents() && dialogType == EnumDialogType.Dialog;
        }
    }
}
