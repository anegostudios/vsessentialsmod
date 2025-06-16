using Vintagestory.API.Client;
using Vintagestory.API.Common;
using OpenTK.Graphics.OpenGL;

namespace FluffyClouds {

    public class CloudRendererVolumetric : IRenderer
    {

        public double RenderOrder { get { return 0.31; } }
        public int RenderRange { get { return 1; } }

        ICoreClientAPI capi;
        ModSystem mod;
        CloudRendererMap map;
        IShaderProgram program;
        MeshRef quad;
        Matrixf matrix = new Matrixf();
        int frame = 0;
        float time = 0.0f;

        public CloudRendererVolumetric(ModSystem mod, ICoreClientAPI capi, CloudRendererMap map)
        {

            this.mod = mod;
            this.capi = capi;
            this.map = map;

            capi.Event.ReloadShader += LoadShader;
            LoadShader();

            quad = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
        }

        public bool LoadShader()
        {

            program = capi.Shader.NewShaderProgram();
            program.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            program.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            program.AssetDomain = mod.Mod.Info.ModID;
            capi.Shader.RegisterFileShaderProgram("cloudvolumetric", program);
            return program.Compile();

        }

        public void Dispose()
        {

            capi.Render.DeleteMesh(quad);
            program?.Dispose();

        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {

            program.Use();

            matrix.Set(capi.Render.PerspectiveProjectionMat)
                  .Mul(capi.Render.CameraMatrixOriginf)
                  .Invert();

            frame++;
            if (!capi.IsGamePaused) time += deltaTime;

            program.UniformMatrix("iMvpMatrix", matrix.Values);
            program.Uniform("cloudOffset", map.offset);
            program.Uniform("cloudMapWidth", (float)map.CloudTileLength);
            program.Uniform("FrameWidth", capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].Width);
            program.Uniform("frame", frame);
            program.Uniform("time", time);
            program.Uniform("PerceptionEffectIntensity", capi.Render.ShaderUniforms.PerceptionEffectIntensity);
            program.BindTexture2D("depthTex", capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId, 0);
            program.BindTexture2D("cloudMap", map.TextureMap, 8);
            program.BindTexture2D("cloudCol", map.TextureCol, 9);

            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFuncSeparate(0, BlendingFactorSrc.One, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.One);
            GL.BlendFuncSeparate(1, BlendingFactorSrc.Zero, BlendingFactorDest.OneMinusSrcColor, BlendingFactorSrc.Zero, BlendingFactorDest.OneMinusSrcColor);
            GL.BlendFuncSeparate(2, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);

            capi.Render.RenderMesh(quad);

            GL.Enable(EnableCap.DepthTest);
            GL.BlendFuncSeparate(0, BlendingFactorSrc.One, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.One);
            GL.BlendFuncSeparate(1, BlendingFactorSrc.Zero, BlendingFactorDest.OneMinusSrcColor, BlendingFactorSrc.Zero, BlendingFactorDest.OneMinusSrcColor);
            GL.BlendFuncSeparate(2, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            program.Stop();

        }

    }

}