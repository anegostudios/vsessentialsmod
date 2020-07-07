using Cairo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class WaypointMapComponent : MapComponent
    {
        public MeshRef quadModel;
        

        Vec2f viewPos = new Vec2f();
        Vec4f color = new Vec4f();
        Waypoint waypoint;
        int waypointIndex;

        Matrixf mvMat = new Matrixf();

        WaypointMapLayer wpLayer;

        bool mouseOver;

        public WaypointMapComponent(int waypointIndex, Waypoint waypoint, WaypointMapLayer wpLayer, ICoreClientAPI capi) : base(capi)
        {
            this.waypointIndex = waypointIndex;
            this.waypoint = waypoint;
            this.wpLayer = wpLayer;
            
            ColorUtil.ToRGBAVec4f(waypoint.Color, ref color);

            quadModel = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
        }

        public override void Render(GuiElementMap map, float dt)
        {
            //if (Texture.Disposed) throw new Exception("Fatal. Trying to render a disposed texture");
            if (quadModel.Disposed) throw new Exception("Fatal. Trying to render a disposed meshref");

            map.TranslateWorldPosToViewPos(waypoint.Position, ref viewPos);

            float x = (float)(map.Bounds.renderX + viewPos.X);
            float y = (float)(map.Bounds.renderY + viewPos.Y);

            if (waypoint.Pinned)
            {
                map.Api.Render.PushScissor(null);
                x = (float)GameMath.Clamp(x, map.Bounds.renderX + 2, map.Bounds.renderX + map.Bounds.InnerWidth - 2);
                y = (float)GameMath.Clamp(y, map.Bounds.renderY + 2, map.Bounds.renderY + map.Bounds.InnerHeight - 2);
            }

            ICoreClientAPI api = map.Api;

            IShaderProgram prog = api.Render.GetEngineShader(EnumShaderProgram.Gui);
            prog.Uniform("rgbaIn", color);
            prog.Uniform("extraGlow", 0);
            prog.Uniform("applyColor", 0);
            prog.Uniform("noTexture", 0f);

            LoadedTexture tex;

            float hover = (mouseOver ? 6 : 0) - 1.5f * Math.Max(1, 1 / map.ZoomLevel);
            

            if (wpLayer.texturesByIcon.TryGetValue(waypoint.Icon, out tex))
            {
                prog.BindTexture2D("tex2d", wpLayer.texturesByIcon[waypoint.Icon].TextureId, 0);
                mvMat
                    .Set(api.Render.CurrentModelviewMatrix)
                    .Translate(x, y, 60)
                    .Scale(tex.Width + hover, tex.Height + hover, 0)
                    .Scale(0.5f, 0.5f, 0)
                ;
                prog.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
                prog.UniformMatrix("modelViewMatrix", mvMat.Values);

                api.Render.RenderMesh(quadModel);
            }

            if (waypoint.Pinned)
            {
                map.Api.Render.PopScissor();
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            quadModel.Dispose();

            // Texture is disposed by WaypointMapLayer
        }



        public override void OnMouseMove(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {
            Vec2f viewPos = new Vec2f();
            mapElem.TranslateWorldPosToViewPos(waypoint.Position, ref viewPos);

            
            double x = viewPos.X + mapElem.Bounds.renderX;
            double y = viewPos.Y + mapElem.Bounds.renderY;
            if (waypoint.Pinned)
            {
                x = (float)GameMath.Clamp(x, mapElem.Bounds.renderX + 2, mapElem.Bounds.renderX + mapElem.Bounds.InnerWidth - 2);
                y = (float)GameMath.Clamp(y, mapElem.Bounds.renderY + 2, mapElem.Bounds.renderY + mapElem.Bounds.InnerHeight - 2);
            }
            double dX = args.X - x;
            double dY = args.Y - y;


            if (mouseOver = Math.Abs(dX) < 8 && Math.Abs(dY) < 8)
            {
                string text = Lang.Get("Waypoint {0}", waypointIndex) + "\n" + waypoint.Title;
                hoverText.AppendLine(text);
            }
        }

        GuiDialogEditWayPoint editWpDlg;
        public override void OnMouseUpOnElement(MouseEvent args, GuiElementMap mapElem)
        {
            if (args.Button == EnumMouseButton.Right)
            {
                Vec2f viewPos = new Vec2f();
                mapElem.TranslateWorldPosToViewPos(waypoint.Position, ref viewPos);

                double x = viewPos.X + mapElem.Bounds.renderX;
                double y = viewPos.Y + mapElem.Bounds.renderY;
                if (waypoint.Pinned)
                {
                    x = (float)GameMath.Clamp(x, mapElem.Bounds.renderX + 2, mapElem.Bounds.renderX + mapElem.Bounds.InnerWidth - 2);
                    y = (float)GameMath.Clamp(y, mapElem.Bounds.renderY + 2, mapElem.Bounds.renderY + mapElem.Bounds.InnerHeight - 2);
                }
                double dX = args.X - x;
                double dY = args.Y - y;


                if (Math.Abs(dX) < 7 && Math.Abs(dY) < 7)
                {
                    if (editWpDlg != null)
                    {
                        editWpDlg.TryClose();
                        editWpDlg.Dispose();
                    }
                    editWpDlg = new GuiDialogEditWayPoint(capi, waypoint, waypointIndex);
                    editWpDlg.TryOpen();
                    args.Handled = true;
                }
            }
        }
    }

}
