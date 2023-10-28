using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class WaypointMapComponent : MapComponent
    {
        Vec2f viewPos = new Vec2f();
        Vec4f color = new Vec4f();
        Waypoint waypoint;
        int waypointIndex;

        Matrixf mvMat = new Matrixf();

        WaypointMapLayer wpLayer;

        bool mouseOver;

        public static float IconScale = 0.85f;

        public WaypointMapComponent(int waypointIndex, Waypoint waypoint, WaypointMapLayer wpLayer, ICoreClientAPI capi) : base(capi)
        {
            this.waypointIndex = waypointIndex;
            this.waypoint = waypoint;
            this.wpLayer = wpLayer;
            
            ColorUtil.ToRGBAVec4f(waypoint.Color, ref color);
        }

        public override void Render(GuiElementMap map, float dt)
        {
            map.TranslateWorldPosToViewPos(waypoint.Position, ref viewPos);
            if (waypoint.Pinned)
            {
                map.Api.Render.PushScissor(null);
                map.ClampButPreserveAngle(ref viewPos, 2);
            } else
            {
                if (viewPos.X < -10 || viewPos.Y < -10 || viewPos.X > map.Bounds.OuterWidth + 10 || viewPos.Y > map.Bounds.OuterHeight + 10) return;
            }

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
            
            if (!wpLayer.texturesByIcon.TryGetValue(waypoint.Icon, out tex))
            {
                wpLayer.texturesByIcon.TryGetValue("circle", out tex);
            }

            if (tex != null)
            {
                prog.BindTexture2D("tex2d", tex.TextureId, 0);
                prog.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
                mvMat
                    .Set(api.Render.CurrentModelviewMatrix)
                    .Translate(x, y, 60)
                    .Scale(tex.Width + hover, tex.Height + hover, 0)
                    .Scale(0.5f * IconScale, 0.5f * IconScale, 0)
                ;

                // Shadow
                var shadowMvMat = mvMat.Clone().Scale(1.25f, 1.25f, 1.25f);
                prog.Uniform("rgbaIn", new Vec4f(0, 0, 0, 0.6f));
                prog.UniformMatrix("modelViewMatrix", shadowMvMat.Values);
                api.Render.RenderMesh(wpLayer.quadModel);

                // Actual waypoint icon
                prog.Uniform("rgbaIn", color);
                prog.UniformMatrix("modelViewMatrix", mvMat.Values);
                api.Render.RenderMesh(wpLayer.quadModel);

            }

            if (waypoint.Pinned)
            {
                map.Api.Render.PopScissor();
            }
        }

        public override void Dispose()
        {
            base.Dispose();
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
                mapElem.ClampButPreserveAngle(ref viewPos, 2);
                x = viewPos.X + mapElem.Bounds.renderX;
                y = viewPos.Y + mapElem.Bounds.renderY;

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
                    mapElem.ClampButPreserveAngle(ref viewPos, 2);
                    x = viewPos.X + mapElem.Bounds.renderX;
                    y = viewPos.Y + mapElem.Bounds.renderY;

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

                    var mapdlg = capi.ModLoader.GetModSystem<WorldMapManager>().worldMapDlg;

                    editWpDlg = new GuiDialogEditWayPoint(capi, mapdlg.MapLayers.FirstOrDefault(l => l is WaypointMapLayer) as WaypointMapLayer, waypoint, waypointIndex);
                    editWpDlg.TryOpen();
                    editWpDlg.OnClosed += () => capi.Gui.RequestFocus(mapdlg);

                    args.Handled = true;
                }
            }
        }
    }

}
