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

        internal MeshRef meshref;
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
            if (rotation == null) this.rotation = new Vec3f();

            textureId = capi.BlockTextureAtlas.AtlasTextureIds[0];

            capi.Event.EnqueueMainThreadTask(() =>
            {
                capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "beanimatable");
                capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "beanimatable");
                capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "beanimatable");
            }, "registerrenderers");
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (!ShouldRender || meshref.Disposed || !meshref.Initialized) return;

            bool shadowPass = stage != EnumRenderStage.Opaque;
            
            EntityPlayer entityPlayer = capi.World.Player.Entity;

            Mat4f.Identity(ModelMat);
            Mat4f.Translate(ModelMat, ModelMat, (float)(pos.X - entityPlayer.CameraPos.X), (float)(pos.Y - entityPlayer.CameraPos.Y), (float)(pos.Z - entityPlayer.CameraPos.Z));

            Mat4f.Translate(ModelMat, ModelMat, 0.5f, 0, 0.5f);
            Mat4f.RotateY(ModelMat, ModelMat, rotation.Y * GameMath.DEG2RAD);
            Mat4f.Translate(ModelMat, ModelMat, -0.5f, 0, -0.5f);

            IRenderAPI rpi = capi.Render;


            IShaderProgram prevProg = rpi.CurrentActiveShader;
            prevProg?.Stop();


            IShaderProgram prog = rpi.GetEngineShader(shadowPass ? EnumShaderProgram.Shadowmapentityanimated : EnumShaderProgram.Entityanimated);
            prog.Use();
            Vec4f lightrgbs = capi.World.BlockAccessor.GetLightRGBs((int)pos.X, (int)pos.Y, (int)pos.Z);
            rpi.GlToggleBlend(true, EnumBlendMode.Standard);

            if (!shadowPass)
            {
                prog.Uniform("rgbaAmbientIn", rpi.AmbientColor);
                prog.Uniform("rgbaFogIn", rpi.FogColor);
                prog.Uniform("fogMinIn", rpi.FogMin);
                prog.Uniform("fogDensityIn", rpi.FogDensity);
                prog.Uniform("rgbaLightIn", lightrgbs);
                prog.Uniform("renderColor", ColorUtil.WhiteArgbVec);
                prog.Uniform("alphaTest", 0.1f);
                prog.UniformMatrix("modelMatrix", ModelMat);
                prog.UniformMatrix("viewMatrix", rpi.CameraMatrixOriginf);
                prog.Uniform("windWaveIntensity", (float)0);
                prog.Uniform("skipRenderJointId", -2);
                prog.Uniform("skipRenderJointId2", -2);
                prog.Uniform("glitchEffectStrength", 0f);
            } else
            {
                prog.UniformMatrix("modelViewMatrix", Mat4f.Mul(new float[16], capi.Render.CurrentModelviewMatrix, ModelMat));
            }

            prog.BindTexture2D("entityTex", textureId, 0);
            prog.UniformMatrix("projectionMatrix", rpi.CurrentProjectionMatrix);

            prog.Uniform("addRenderFlags", 0);
            

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
            capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
            capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
            meshref?.Dispose();
        }

    }
}
