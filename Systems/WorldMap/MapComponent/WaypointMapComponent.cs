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
        public LoadedTexture Texture;

        Vec2f viewPos = new Vec2f();
        Vec4f color = new Vec4f();
        Waypoint waypoint;
        int waypointIndex;

        Matrixf mvMat = new Matrixf();

        public WaypointMapComponent(int waypointIndex, Waypoint waypoint, LoadedTexture texture, ICoreClientAPI capi) : base(capi)
        {
            this.waypointIndex = waypointIndex;
            this.waypoint = waypoint;
            this.Texture = texture;
            ColorUtil.ToRGBAVec4f(waypoint.Color, ref color);

            quadModel = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
        }

        public override void Render(GuiElementMap map, float dt)
        {
            if (Texture.Disposed) throw new Exception("Fatal. Trying to render a disposed texture");
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
            prog.BindTexture2D("tex2d", Texture.TextureId, 0);

            mvMat
                .Set(api.Render.CurrentModelviewMatrix)
                .Translate(x, y, 60)
                .Scale(Texture.Width, Texture.Height, 0)
                .Scale(0.5f, 0.5f, 0)
            ;

            prog.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
            prog.UniformMatrix("modelViewMatrix", mvMat.Values);
            
            api.Render.RenderMesh(quadModel);
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
            
            if (Math.Abs(viewPos.X - mouseX) < 5 && Math.Abs(viewPos.Y - mouseY) < 5)
            {
                string text = Lang.Get("Waypoint {0}", waypointIndex) + "\n" + waypoint.Title;
                hoverText.Append(text);
            }
        }
    }

}
