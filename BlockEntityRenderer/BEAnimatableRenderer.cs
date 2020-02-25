using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class BEAnimatableRenderer : IRenderer
    {
        public double RenderOrder => 1;
        public int RenderRange => 99;

        BlockPos pos;
        ICoreClientAPI capi;
        AnimatorBase animator;
        protected Dictionary<string, AnimationMetaData> activeAnimationsByAnimCode = new Dictionary<string, AnimationMetaData>();

        MeshRef meshref;
        int textureId;

        public float[] ModelMat = Mat4f.Create();

        public bool ShouldRender;
        Vec3f rotation;

        public BEAnimatableRenderer(ICoreClientAPI capi, BlockPos pos, Vec3f rotation, AnimatorBase animator, Dictionary<string, AnimationMetaData> activeAnimationsByAnimCode, MeshRef meshref)
        {
            this.pos = pos;
            this.capi = capi;
            this.animator = animator;
            this.activeAnimationsByAnimCode = activeAnimationsByAnimCode;
            this.meshref = meshref;
            this.rotation = rotation;
            if (rotation == null) rotation = new Vec3f();

            textureId = capi.BlockTextureAtlas.GetPosition(capi.World.BlockAccessor.GetBlock(pos), "rusty").atlasTextureId;

            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "beanimatable");
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (!ShouldRender) return;

            animator.OnFrame(activeAnimationsByAnimCode, dt);
            
            EntityPlayer entityPlayer = capi.World.Player.Entity;

            Mat4f.Identity(ModelMat);
            Mat4f.Translate(ModelMat, ModelMat, (float)(pos.X - entityPlayer.CameraPos.X), (float)(pos.Y - entityPlayer.CameraPos.Y), (float)(pos.Z - entityPlayer.CameraPos.Z));

            Mat4f.Translate(ModelMat, ModelMat, 0.5f, 0, 0.5f);
            Mat4f.RotateY(ModelMat, ModelMat, rotation.Y * GameMath.DEG2RAD);
            Mat4f.Translate(ModelMat, ModelMat, -0.5f, 0, -0.5f);

            IRenderAPI rpi = capi.Render;


            IShaderProgram prevProg = rpi.CurrentActiveShader;
            prevProg?.Stop();


            IShaderProgram prog = rpi.GetEngineShader(EnumShaderProgram.Entityanimated);
            prog.Use();

            prog.Uniform("rgbaAmbientIn", rpi.AmbientColor);
            prog.Uniform("rgbaFogIn", rpi.FogColor);
            prog.Uniform("fogMinIn", rpi.FogMin);
            prog.Uniform("fogDensityIn", rpi.FogDensity);
            prog.BindTexture2D("entityTex", textureId, 0);
            prog.Uniform("alphaTest", 0.1f);
            prog.UniformMatrix("projectionMatrix", rpi.CurrentProjectionMatrix);

            
            rpi.GlToggleBlend(true, EnumBlendMode.Standard);
            

            Vec4f lightrgbs = capi.World.BlockAccessor.GetLightRGBs((int)pos.X, (int)pos.Y, (int)pos.Z);

            prog.Uniform("rgbaLightIn", lightrgbs);
            //prog.Uniform("extraGlow", entity.Properties.Client.GlowLevel);
            prog.UniformMatrix("modelMatrix", ModelMat);
            prog.UniformMatrix("viewMatrix", rpi.CameraMatrixOriginf);
            prog.Uniform("addRenderFlags", 0);
            prog.Uniform("windWaveIntensity", (float)0);

            /*color[0] = (entity.RenderColor >> 16 & 0xff) / 255f;
            color[1] = ((entity.RenderColor >> 8) & 0xff) / 255f;
            color[2] = ((entity.RenderColor >> 0) & 0xff) / 255f;
            color[3] = ((entity.RenderColor >> 24) & 0xff) / 255f;

            prog.Uniform("renderColor", color);*/

            prog.Uniform("renderColor", ColorUtil.WhiteArgbVec);


            prog.UniformMatrices(
                "elementTransforms",
                GlobalConstants.MaxAnimatedElements,
                animator.Matrices
            );

            capi.Render.RenderMesh(meshref);

            prog.Stop();
            prevProg?.Use();
        }


        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            meshref?.Dispose();
        }

    }
}
