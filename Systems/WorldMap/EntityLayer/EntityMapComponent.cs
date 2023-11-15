using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityMapComponent : MapComponent
    {
        public Entity entity;
        internal MeshRef quadModel;
        public LoadedTexture Texture;

        Vec2f viewPos = new Vec2f();
        Matrixf mvMat = new Matrixf();

        int color;

        public EntityMapComponent(ICoreClientAPI capi, LoadedTexture texture, Entity entity, string color = null) : base(capi)
        {
            quadModel = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
            this.Texture = texture;
            this.entity = entity;
            this.color = color == null ? 0 : (ColorUtil.Hex2Int(color) | 255 << 24);
        }

        public override void Render(GuiElementMap map, float dt)
        {
            var player = (entity as EntityPlayer)?.Player;
            if (player?.WorldData?.CurrentGameMode == EnumGameMode.Spectator == true) return;
            if ((entity as EntityPlayer)?.Controls.Sneak == true && player != capi.World.Player) return;

            map.TranslateWorldPosToViewPos(entity.Pos.XYZ, ref viewPos);

            float x = (float)(map.Bounds.renderX + viewPos.X);
            float y = (float)(map.Bounds.renderY + viewPos.Y);
            
            ICoreClientAPI api = map.Api;

            if (Texture.Disposed) throw new Exception("Fatal. Trying to render a disposed texture");
            if (quadModel.Disposed) throw new Exception("Fatal. Trying to render a disposed texture");

            capi.Render.GlToggleBlend(true);

            IShaderProgram prog = api.Render.GetEngineShader(EnumShaderProgram.Gui);
            if (color == 0)
            {
                prog.Uniform("rgbaIn", ColorUtil.WhiteArgbVec);
            } else
            {
                Vec4f vec = new Vec4f();
                ColorUtil.ToRGBAVec4f(color, ref vec);
                prog.Uniform("rgbaIn", vec);
            }

            prog.Uniform("applyColor", 0);
            prog.Uniform("extraGlow", 0);
            prog.Uniform("noTexture", 0f);
            prog.BindTexture2D("tex2d", Texture.TextureId, 0);

            float yawoffset = player != null ? 90 : 180; // whyyyy

            mvMat
                .Set(api.Render.CurrentModelviewMatrix)
                .Translate(x, y, 60)
                .Scale(Texture.Width, Texture.Height, 0)
                .Scale(0.5f, 0.5f, 0)
                .RotateZ(-entity.Pos.Yaw + yawoffset * GameMath.DEG2RAD)
            ;

            prog.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
            prog.UniformMatrix("modelViewMatrix", mvMat.Values);

            api.Render.RenderMesh(quadModel);
        }

        public override void Dispose()
        {
            base.Dispose();

            quadModel.Dispose();
        }


        public override void OnMouseMove(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {
            Vec2f viewPos = new Vec2f();
            mapElem.TranslateWorldPosToViewPos(entity.Pos.XYZ, ref viewPos);

            double mouseX = args.X - mapElem.Bounds.renderX;
            double mouseY = args.Y - mapElem.Bounds.renderY;

            if (Math.Abs(viewPos.X - mouseX) < 5 && Math.Abs(viewPos.Y - mouseY) < 5)
            {
                EntityPlayer eplr = entity as EntityPlayer;
                if (eplr != null)
                {
                    hoverText.AppendLine("Player " + capi.World.PlayerByUid(eplr.PlayerUID)?.PlayerName);
                } else
                {
                    hoverText.AppendLine(entity.GetName());
                }
            }
        }
    }

}
