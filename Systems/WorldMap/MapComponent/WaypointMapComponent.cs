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
            } else
            {
                int a = 1;
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

            double mouseX = args.X - mapElem.Bounds.renderX;
            double mouseY = args.Y - mapElem.Bounds.renderY;
            
            if (mouseOver = Math.Abs(viewPos.X - mouseX) < 8 && Math.Abs(viewPos.Y - mouseY) < 8)
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

                double mouseX = args.X - mapElem.Bounds.renderX;
                double mouseY = args.Y - mapElem.Bounds.renderY;

                if (Math.Abs(viewPos.X - mouseX) < 5 && Math.Abs(viewPos.Y - mouseY) < 5)
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
