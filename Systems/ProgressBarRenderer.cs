using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

#nullable disable

public class ModSystemProgressBar : ModSystem
{
    List<ProgressBarRenderer> pbrenderer = new List<ProgressBarRenderer>();
    ICoreClientAPI capi;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
    }

    public IProgressBar AddProgressbar()
    {
        var pbr = new ProgressBarRenderer(capi, pbrenderer.Count * 30);
        capi.Event.RegisterRenderer(pbr, EnumRenderStage.Ortho);
        this.pbrenderer.Add(pbr);
        return pbr;
    }

    public void RemoveProgressbar(IProgressBar pbr)
    {
        if (pbr == null) return;

        var prend = pbr as ProgressBarRenderer;
        pbrenderer.Remove(prend);
        capi.Event.UnregisterRenderer(prend, EnumRenderStage.Ortho);
        prend.Dispose();
    }
}

public interface IProgressBar
{
    float Progress { get; set; }
}

public class ProgressBarRenderer : IRenderer, IProgressBar
{
    MeshRef whiteRectangleRef;
    MeshRef progressQuadRef;
    ICoreClientAPI capi;
    Matrixf mvMatrix = new Matrixf();

    float offsety;

    public float Progress { get; set; } = 0;


    public double RenderOrder { get { return 0; } }

    public int RenderRange { get { return 10; } }

    public ProgressBarRenderer(ICoreClientAPI api, float offsety)
    {
        this.capi = api;
        this.offsety = offsety;

        // This will get a line loop with vertices inside [-1,-1] till [1,1]
        MeshData rectangle = LineMeshUtil.GetRectangle(ColorUtil.WhiteArgb);
        whiteRectangleRef = api.Render.UploadMesh(rectangle);

        // This will get a quad with vertices inside [-1,-1] till [1,1]
        progressQuadRef = api.Render.UploadMesh(QuadMeshUtil.GetQuad());
    }


    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        IShaderProgram curShader = capi.Render.CurrentActiveShader;

        Vec4f color = new Vec4f(1, 1, 1, 1);

        // Render rectangle
        curShader.Uniform("rgbaIn", color);
        curShader.Uniform("extraGlow", 0);
        curShader.Uniform("applyColor", 0);
        curShader.Uniform("tex2d", 0);
        curShader.Uniform("noTexture", 1f);

        var fwdt = capi.Render.FrameWidth;
        var fhgt = capi.Render.FrameHeight;

        var sc = RuntimeEnv.GUIScale / 2;

        mvMatrix
            .Set(capi.Render.CurrentModelviewMatrix)
            .Translate(fwdt / 2 - 50*sc, fhgt / 2 + 20*sc + offsety*sc, 50)
            .Scale(sc * 100, sc * 20, 0)
            .Translate(0.5f, 0.5f, 0)
            .Scale(0.5f, 0.5f, 0)
        ;

        curShader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        curShader.UniformMatrix("modelViewMatrix", mvMatrix.Values);

        capi.Render.RenderMesh(whiteRectangleRef);


        // Render progress bar
        float width = Progress * 100;

        mvMatrix
            .Set(capi.Render.CurrentModelviewMatrix)
            .Translate(fwdt / 2 - 50*sc, fhgt / 2 + 20*sc + offsety*sc, 50)
            .Scale(sc * width, sc * 20, 0)
            .Translate(0.5f, 0.5f, 0)
            .Scale(0.5f, 0.5f, 0)
        ;

        curShader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        curShader.UniformMatrix("modelViewMatrix", mvMatrix.Values);

        capi.Render.RenderMesh(progressQuadRef);
    }

    public void Dispose()
    {
        capi.Render.DeleteMesh(whiteRectangleRef);
        capi.Render.DeleteMesh(progressQuadRef);
    }
}
