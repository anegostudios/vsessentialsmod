using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent;

public class CameraPathRenderer : IRenderer
{
    public double RenderOrder => 0.7;
    public int RenderRange => 100;

    public List<CameraPoint> CameraPoints;
    public BlockPos Origin;

    protected ICoreClientAPI capi;
    protected MeshData cameraPathModel;
    protected MeshRef cameraPathModelRef;
    protected SystemCinematicCamera modsys;

    public CameraPathRenderer(SystemCinematicCamera modsys, ICoreClientAPI capi, List<CameraPoint> CameraPoints)
    {
        this.modsys = modsys;
        this.capi = capi;
        this.CameraPoints = CameraPoints;

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "cinecam"/*, 0.7*/);
    }

    Matrixf mat = new Matrixf();

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (capi.HideGuis || cameraPathModelRef == null) return;

        var prog = capi.Shader.GetProgram((int)EnumShaderProgram.Autocamera);

        prog.Use();
        var rapi = capi.Render;

        rapi.LineWidth = 2;
        rapi.BindTexture2d(0);

        mat.Set(rapi.CameraMatrixOrigin);
        Vec3d cameraPos = capi.World.Player.Entity.CameraPos;
        mat.Translate(
            (float)(Origin.X - cameraPos.X),
            (float)(Origin.Y - cameraPos.Y),
            (float)(Origin.Z - cameraPos.Z)
        );

        prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
        prog.UniformMatrix("modelViewMatrix", mat.Values);

        rapi.RenderMesh(cameraPathModelRef);

        prog.Stop();
    }

    public void InitModel()
    {
        cameraPathModel = new MeshData(4, 4, false, false, true, true);
        cameraPathModel.SetMode(EnumDrawMode.LineStrip);
        cameraPathModelRef = null;
    }

    public void UpdateModel(BlockPos origin)
    {
        this.Origin = origin;
        GenerateCameraPathModel();
    }

    public void GenerateCameraPathModel()
    {
        InitModel();
        int vertexIndex = 0;
        var path = modsys.GenerateCameraPath();

        for (int i = 0; i < path.Length; i++)
        {
            var pos = path[i];

            cameraPathModel.AddVertexSkipTex(
                (float)(pos.X - Origin.X),
                (float)(pos.Y - Origin.Y),
                (float)(pos.Z - Origin.Z),
                i % 2*30 == 0 ? ColorUtil.WhiteArgb : ColorUtil.ToRgba(255, 255, 50, 50)
            );

            cameraPathModel.AddIndex(vertexIndex++);

            if (i < path.Length-1)
            {
                pos = path[i+1];
                cameraPathModel.AddVertexSkipTex(
                    (float)(pos.X - Origin.X),
                    (float)(pos.Y - Origin.Y),
                    (float)(pos.Z - Origin.Z),
                    i % 2 * 30 == 0 ? ColorUtil.WhiteArgb : ColorUtil.ToRgba(255, 255, 50, 50)
                );

                cameraPathModel.AddIndex(vertexIndex++);
            }
        }

        cameraPathModelRef?.Dispose();
        cameraPathModelRef = capi.Render.UploadMesh(cameraPathModel);
    }


    public void Dispose()
    {
        cameraPathModelRef?.Dispose();
    }

}

