using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cairo;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class GuiElementMap : GuiElement
    {
        List<MapComponent> mapComponents;
        public bool IsDragingMap;
        public float ZoomLevel = 1;


        Vec3d prevPlayerPos = new Vec3d();
        public Cuboidi chunkViewBoundsBefore = new Cuboidi();

        public OnViewChangedDelegate viewChanged;

        public ICoreClientAPI Api => api;


        /// <summary>
        /// In blocks
        /// </summary>
        public Cuboidd CurrentBlockViewBounds = new Cuboidd();

        public GuiElementMap(List<MapComponent> mapComponents, Vec3d centerPos, ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds)
        {
            this.mapComponents = mapComponents;


            prevPlayerPos.X = api.World.Player.Entity.Pos.X;
            prevPlayerPos.Z = api.World.Player.Entity.Pos.Z;

        }
        

        public override void ComposeElements(Context ctxStatic, ImageSurface surface)
        {
            Bounds.CalcWorldBounds();

            mapComponents.Clear();
            chunkViewBoundsBefore = new Cuboidi();

            BlockPos start = api.World.Player.Entity.Pos.AsBlockPos;
            CurrentBlockViewBounds = new Cuboidd(
                start.X - Bounds.InnerWidth / 2, 0, start.Z - Bounds.InnerHeight / 2, 
                start.X + Bounds.InnerWidth / 2, 0, start.Z + Bounds.InnerHeight / 2
            );
        }


        public override void RenderInteractiveElements(float deltaTime)
        {
            api.Render.BeginScissor(Bounds);

            foreach (MapComponent cmp in mapComponents)
            {
                cmp.Render(this, deltaTime);
                api.Render.CheckGlError();
            }

            api.Render.EndScissor();

            api.Render.CheckGlError();
        }

        public override void PostRenderInteractiveElements(float deltaTime)
        {
            base.PostRenderInteractiveElements(deltaTime);

            EntityPlayer plr = api.World.Player.Entity;
            CurrentBlockViewBounds.Translate((plr.Pos.X - prevPlayerPos.X), 0, (plr.Pos.Z - prevPlayerPos.Z));

            prevPlayerPos.Set(plr.Pos.X, plr.Pos.Y, plr.Pos.Z);

        }
        
        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseDownOnElement(api, args);

            IsDragingMap = true;
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseUp(api, args);

            IsDragingMap = false;
        }

        public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
        {
            if (IsDragingMap)
            {
                CurrentBlockViewBounds.Translate(-args.DeltaX / ZoomLevel, 0, -args.DeltaY / ZoomLevel);
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
            float zoomBefore = ZoomLevel;

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
            double blocksWidth = CurrentBlockViewBounds.X2 - CurrentBlockViewBounds.X1;
            double blocksLength = CurrentBlockViewBounds.Z2 - CurrentBlockViewBounds.Z1;
            
            viewPos.X = (float)((worldPos.X - CurrentBlockViewBounds.X1) / blocksWidth * Bounds.InnerWidth);
            viewPos.Y = (float)((worldPos.Z - CurrentBlockViewBounds.Z1) / blocksLength * Bounds.InnerHeight);
        }

        public void TranslateViewPosToWorldPos(Vec2f viewPos, ref Vec3d worldPos)
        {
            double blocksWidth = CurrentBlockViewBounds.X2 - CurrentBlockViewBounds.X1;
            double blocksLength = CurrentBlockViewBounds.Z2 - CurrentBlockViewBounds.Z1;

            worldPos.X = viewPos.X * blocksWidth / Bounds.InnerWidth + CurrentBlockViewBounds.X1;
            worldPos.Z = viewPos.Y * blocksLength / Bounds.InnerHeight + CurrentBlockViewBounds.Z1;
            worldPos.Y = api.World.BlockAccessor.GetRainMapHeightAt(worldPos.AsBlockPos);
        }



        List<Vec2i> nowVisible = new List<Vec2i>();
        List<Vec2i> nowHidden = new List<Vec2i>();

        public void EnsureMapFullyLoaded()
        {
            int chunksize = api.World.BlockAccessor.ChunkSize;
            
            nowVisible.Clear();
            nowHidden.Clear();

            Cuboidi chunkviewBounds = CurrentBlockViewBounds.ToCuboidi();
            chunkviewBounds.Div(chunksize);

            BlockPos cur = new BlockPos().Set(chunkviewBounds.X1, 0, chunkviewBounds.Z1);
            

            while (cur.X <= chunkviewBounds.X2)
            {
                cur.Z = chunkviewBounds.Z1;

                while (cur.Z <= chunkviewBounds.Z2)
                {
                    if (!chunkViewBoundsBefore.ContainsOrTouches(cur))
                    {
                        nowVisible.Add(new Vec2i(cur.X, cur.Z));
                    }
                    cur.Z++;
                }

                cur.X++;
            }
            
            
            cur.Set(chunkViewBoundsBefore.X1, 0, chunkViewBoundsBefore.Z1);

            while (cur.X <= chunkViewBoundsBefore.X2)
            {
                cur.Z = chunkViewBoundsBefore.Z1;

                while (cur.Z <= chunkViewBoundsBefore.Z2)
                {
                    if (!chunkviewBounds.ContainsOrTouches(cur))
                    {
                        nowHidden.Add(new Vec2i(cur.X, cur.Z));
                    }

                    cur.Z++;
                }

                cur.X++;
            }


            chunkViewBoundsBefore = chunkviewBounds.Clone();

            viewChanged(nowVisible, nowHidden);
        }


        internal void AddMapComponent(MapComponent cmp)
        {
            mapComponents.Add(cmp);
        }

        internal bool RemoveMapComponent(MapComponent cmp)
        {
            return mapComponents.Remove(cmp);
        }

        public override void Dispose()
        {
            // mapComponents is diposed by the Gui Dialog
        }
    }
}
