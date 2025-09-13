using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class GuiElementMap : GuiElement
    {
        public List<MapLayer> mapLayers;
        public bool IsDragingMap;
        public float ZoomLevel = 1;


        public Vec3d prevPlayerPos = new Vec3d();
        public Cuboidi chunkViewBoundsBefore = new Cuboidi();

        public OnViewChangedDelegate viewChanged;
        public OnViewChangedSyncDelegate viewChangedSync;

        public ICoreClientAPI Api => api;

        bool snapToPlayer;

        /// <summary>
        /// In blocks
        /// </summary>
        public Cuboidd CurrentBlockViewBounds = new Cuboidd();

        bool dialogHasFocus => worldmapdlg.Focused && worldmapdlg.DialogType == EnumDialogType.Dialog;
        GuiDialogWorldMap worldmapdlg;

        public GuiElementMap(List<MapLayer> mapLayers, ICoreClientAPI capi, GuiDialogWorldMap worldmapdlg, ElementBounds bounds, bool snapToPlayer) : base(capi, bounds)
        {
            this.mapLayers = mapLayers;
            this.snapToPlayer = snapToPlayer;
            this.worldmapdlg = worldmapdlg;

            prevPlayerPos.X = api.World.Player.Entity.Pos.X;
            prevPlayerPos.Z = api.World.Player.Entity.Pos.Z;
        }


        public override void ComposeElements(Context ctxStatic, ImageSurface surface)
        {
            Bounds.CalcWorldBounds();
            chunkViewBoundsBefore = new Cuboidi();

            BlockPos start = api.World.Player.Entity.Pos.AsBlockPos;
            CurrentBlockViewBounds = new Cuboidd(
                start.X - Bounds.InnerWidth / 2 / ZoomLevel, 0, start.Z - Bounds.InnerHeight / 2 / ZoomLevel,
                start.X + Bounds.InnerWidth / 2 / ZoomLevel, 0, start.Z + Bounds.InnerHeight / 2 / ZoomLevel
            );
        }


        public override void RenderInteractiveElements(float deltaTime)
        {
            api.Render.PushScissor(Bounds);

            for (int i = 0; i < mapLayers.Count; i++)
            {
                mapLayers[i].Render(this, deltaTime);
            }

            api.Render.PopScissor();

            api.Render.CheckGlError();
        }

        float tkeyDeltaX, tkeyDeltaY;
        float skeyDeltaX, skeyDeltaY;

        public override void PostRenderInteractiveElements(float deltaTime)
        {
            base.PostRenderInteractiveElements(deltaTime);

            EntityPlayer plr = api.World.Player.Entity;
            double diffx = plr.Pos.X - prevPlayerPos.X;
            double diffz = plr.Pos.Z - prevPlayerPos.Z;

            if (Math.Abs(diffx) > 0.0002 || Math.Abs(diffz) > 0.0002)
            {
                if (snapToPlayer)
                {
                    var start = api.World.Player.Entity.Pos;
                    CurrentBlockViewBounds.X1 = start.X - Bounds.InnerWidth / 2 / ZoomLevel;
                    CurrentBlockViewBounds.Z1 = start.Z - Bounds.InnerHeight / 2 / ZoomLevel;
                    CurrentBlockViewBounds.X2 = start.X + Bounds.InnerWidth / 2 / ZoomLevel;
                    CurrentBlockViewBounds.Z2 = start.Z + Bounds.InnerHeight / 2 / ZoomLevel;
                } else
                {
                    CurrentBlockViewBounds.Translate(diffx, 0, diffz);
                }
            }

            prevPlayerPos.Set(plr.Pos.X, plr.Pos.Y, plr.Pos.Z);

            if (dialogHasFocus)
            {
                if (api.Input.KeyboardKeyStateRaw[(int)GlKeys.Up])
                {
                    tkeyDeltaY = 15f;
                }
                else if (api.Input.KeyboardKeyStateRaw[(int)GlKeys.Down])
                {
                    tkeyDeltaY = -15f;
                }
                else tkeyDeltaY = 0;

                if (api.Input.KeyboardKeyStateRaw[(int)GlKeys.Left])
                {
                    tkeyDeltaX = 15f;
                }
                else if (api.Input.KeyboardKeyStateRaw[(int)GlKeys.Right])
                {
                    tkeyDeltaX = -15f;
                }
                else tkeyDeltaX = 0;


                skeyDeltaX += (tkeyDeltaX - skeyDeltaX) * deltaTime * 15;
                skeyDeltaY += (tkeyDeltaY - skeyDeltaY) * deltaTime * 15;

                if (Math.Abs(skeyDeltaX) > 0.5f || Math.Abs(skeyDeltaY) > 0.5f)
                {
                    CurrentBlockViewBounds.Translate(-skeyDeltaX / ZoomLevel, 0, -skeyDeltaY / ZoomLevel);
                }
            }
        }

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseDownOnElement(api, args);

            if (args.Button == EnumMouseButton.Left)
            {
                IsDragingMap = true;
                prevMouseX = args.X;
                prevMouseY = args.Y;
            }
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseUp(api, args);

            IsDragingMap = false;
        }

        int prevMouseX, prevMouseY;

        public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
        {
            if (IsDragingMap)
            {
                CurrentBlockViewBounds.Translate(-(args.X - prevMouseX) / ZoomLevel, 0, -(args.Y - prevMouseY) / ZoomLevel);
                prevMouseX = args.X;
                prevMouseY = args.Y;
            }
        }

        public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
        {
            if (!Bounds.ParentBounds.PointInside(api.Input.MouseX, api.Input.MouseY))
            {
                return;
            }

            float px = (float)((api.Input.MouseX - Bounds.absX) / Bounds.InnerWidth);
            float py = (float)((api.Input.MouseY - Bounds.absY) / Bounds.InnerHeight);

            ZoomAdd(args.delta > 0 ? 0.25f : -0.25f, px, py);
            args.SetHandled(true);
        }


        public void ZoomAdd(float zoomDiff, float px, float pz)
        {
            if (zoomDiff < 0 && ZoomLevel + zoomDiff < 0.25f) return;
            if (zoomDiff > 0 && ZoomLevel + zoomDiff > 6f) return;

            ZoomLevel += zoomDiff;

            double nowRelSize = 1 / ZoomLevel;
            double diffX = Bounds.InnerWidth * nowRelSize - CurrentBlockViewBounds.Width;
            double diffZ = Bounds.InnerHeight * nowRelSize - CurrentBlockViewBounds.Length;
            CurrentBlockViewBounds.X2 += diffX;
            CurrentBlockViewBounds.Z2 += diffZ;

            CurrentBlockViewBounds.Translate(-diffX * px, 0, -diffZ * pz);


            EnsureMapFullyLoaded();
        }


        public void TranslateWorldPosToViewPos(Vec3d worldPos, ref Vec2f viewPos)
        {
            if (worldPos == null) throw new ArgumentNullException("worldPos is null");

            double blocksWidth = CurrentBlockViewBounds.X2 - CurrentBlockViewBounds.X1;
            double blocksLength = CurrentBlockViewBounds.Z2 - CurrentBlockViewBounds.Z1;

            viewPos.X = (float)((worldPos.X - CurrentBlockViewBounds.X1) / blocksWidth * Bounds.InnerWidth);
            viewPos.Y = (float)((worldPos.Z - CurrentBlockViewBounds.Z1) / blocksLength * Bounds.InnerHeight);
        }

        public void ClampButPreserveAngle(ref Vec2f viewPos, int border)
        {
            if (viewPos.X >= border && viewPos.X <= Bounds.InnerWidth - 2 &&
                viewPos.Y >= border && viewPos.Y <= Bounds.InnerHeight - 2)
                return;

            var centerX = Bounds.InnerWidth / 2 - border;
            var centerY = Bounds.InnerHeight / 2 - border;

            var relX = (viewPos.X - centerX) / centerX;
            var relY = (viewPos.Y - centerY) / centerY;
            var factor = Math.Max(Math.Abs(relX), Math.Abs(relY));

            viewPos.X = (float)((viewPos.X - centerX) / factor + centerX);
            viewPos.Y = (float)((viewPos.Y - centerY) / factor + centerY);
        }

        public void TranslateViewPosToWorldPos(Vec2f viewPos, ref Vec3d worldPos)
        {
            if (worldPos == null) throw new ArgumentNullException("viewPos is null");

            double blocksWidth = CurrentBlockViewBounds.X2 - CurrentBlockViewBounds.X1;
            double blocksLength = CurrentBlockViewBounds.Z2 - CurrentBlockViewBounds.Z1;

            worldPos.X = viewPos.X * blocksWidth / Bounds.InnerWidth + CurrentBlockViewBounds.X1;
            worldPos.Z = viewPos.Y * blocksLength / Bounds.InnerHeight + CurrentBlockViewBounds.Z1;
            worldPos.Y = api.World.BlockAccessor.GetRainMapHeightAt(worldPos.AsBlockPos);
        }



        List<FastVec2i> nowVisible = new List<FastVec2i>();
        List<FastVec2i> nowHidden = new List<FastVec2i>();

        public void EnsureMapFullyLoaded()
        {
            const int chunksize = GlobalConstants.ChunkSize;

            nowVisible.Clear();
            nowHidden.Clear();

            Cuboidi chunkviewBounds = CurrentBlockViewBounds.ToCuboidi();
            chunkviewBounds.Div(chunksize);

            if (chunkViewBoundsBefore.Equals(chunkviewBounds)) return;
            viewChangedSync(chunkviewBounds.X1, chunkviewBounds.Z1, chunkviewBounds.X2, chunkviewBounds.Z2);
            
            BlockPos cur = new BlockPos();

            bool beforeBoundsEmpty = chunkViewBoundsBefore.SizeX == 0 && chunkViewBoundsBefore.SizeZ == 0;

            // When panning, add to nowVisible incrementally in the direction we moved before (i.e. starting from the edge of the current visible area)
            int xDir = (chunkviewBounds.X2 > chunkViewBoundsBefore.X2) ? 1 : -1;
            int zDir = (chunkviewBounds.Z2 > chunkViewBoundsBefore.Z2) ? 1 : -1;
            cur.Set(xDir > 0 ? chunkviewBounds.X1 : chunkviewBounds.X2, 0, chunkviewBounds.Z1);
            while ((xDir > 0 && cur.X <= chunkviewBounds.X2) || (xDir < 0 && cur.X >= chunkviewBounds.X1))
            {
                cur.Z = zDir > 0 ? chunkviewBounds.Z1 : chunkviewBounds.Z2;

                while ((zDir > 0 && cur.Z <= chunkviewBounds.Z2) || (zDir < 0 && cur.Z >= chunkviewBounds.Z1))
                {
                    if (beforeBoundsEmpty || !chunkViewBoundsBefore.ContainsOrTouches(cur))
                    {
                        nowVisible.Add(new FastVec2i(cur.X, cur.Z));
                    }
                    cur.Z += zDir;
                }

                cur.X += xDir;
            }

            if (!beforeBoundsEmpty)
            {
                cur.Set(chunkViewBoundsBefore.X1, 0, chunkViewBoundsBefore.Z1);

                while (cur.X <= chunkViewBoundsBefore.X2)
                {
                    cur.Z = chunkViewBoundsBefore.Z1;

                    while (cur.Z <= chunkViewBoundsBefore.Z2)
                    {
                        if (!chunkviewBounds.ContainsOrTouches(cur))
                        {
                            nowHidden.Add(new FastVec2i(cur.X, cur.Z));
                        }

                        cur.Z++;
                    }

                    cur.X++;
                }
            }

            chunkViewBoundsBefore = chunkviewBounds.Clone();

            if (nowHidden.Count > 0 || nowVisible.Count > 0)
            {
                viewChanged(nowVisible, nowHidden);
            }
        }

        public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
        {
            base.OnKeyDown(api, args);

            // Centers the map around the players position
            if (args.KeyCode == (int)GlKeys.Space)
            {
                CenterMapTo(api.World.Player.Entity.Pos.AsBlockPos);
            }


            if (api.Input.KeyboardKeyStateRaw[(int)GlKeys.Up] ||
                api.Input.KeyboardKeyStateRaw[(int)GlKeys.Down] ||
                api.Input.KeyboardKeyStateRaw[(int)GlKeys.Left] ||
                api.Input.KeyboardKeyStateRaw[(int)GlKeys.Right]
               )
            {
                args.Handled = true;
            }

            if (api.Input.KeyboardKeyStateRaw[(int)GlKeys.Plus] || api.Input.KeyboardKeyStateRaw[(int)GlKeys.KeypadPlus])
            {
                ZoomAdd(0.25f, 0.5f, 0.5f);
            }
            if (api.Input.KeyboardKeyStateRaw[(int)GlKeys.Minus] || api.Input.KeyboardKeyStateRaw[(int)GlKeys.KeypadMinus])
            {
                ZoomAdd(-0.25f, 0.5f, 0.5f);
            }
        }

        public override void OnKeyUp(ICoreClientAPI api, KeyEvent args)
        {
            base.OnKeyUp(api, args);
        }


        public void CenterMapTo(BlockPos pos)
        {
            CurrentBlockViewBounds = new Cuboidd(
                pos.X - Bounds.InnerWidth / 2 / ZoomLevel, 0, pos.Z - Bounds.InnerHeight / 2 / ZoomLevel,
                pos.X + Bounds.InnerWidth / 2 / ZoomLevel, 0, pos.Z + Bounds.InnerHeight / 2 / ZoomLevel
            );
        }


        public override void Dispose()
        {
            // mapComponents is diposed by the Gui Dialog
        }
    }
}

