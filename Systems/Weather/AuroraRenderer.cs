using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{

    public class AuroraRenderer : IRenderer
    {
        bool renderAurora = true;

        ICoreClientAPI capi;
        IShaderProgram prog;
        Random rand;

        public double RenderOrder => 0.35;
        public int RenderRange => 9999;

        MeshRef quadTilesRef;

        WeatherSystemClient wsys;

        public AuroraRenderer(ICoreClientAPI capi, WeatherSystemClient wsys)
        {
            this.capi = capi;
            this.wsys = wsys;
            capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "aurora");

            capi.Event.ReloadShader += LoadShader;
            LoadShader();

            rand = new Random(capi.World.Seed);

            renderAurora = capi.Settings.Bool["renderAurora"];
            renderAurora = true;
        }


        public bool LoadShader()
        {
            InitQuads();
            prog = capi.Shader.NewShaderProgram();

            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

            capi.Shader.RegisterFileShaderProgram("aurora", prog);

            return prog.Compile();
        }


        Matrixf mvMat = new Matrixf();
        Vec4f col = new Vec4f(1,1,1,1);
        float quarterSecAccum = 0;
        public ClimateCondition clientClimateCond;
        BlockPos plrPos = new BlockPos();

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (!renderAurora) return;

            if (capi.Render.FrameWidth == 0) return;

            var campos = capi.World.Player.Entity.CameraPos;

            quarterSecAccum += deltaTime;
            if (quarterSecAccum > 0.25f)
            {
                plrPos.X = (int)campos.X;
                plrPos.Y = capi.World.SeaLevel;
                plrPos.Z = (int)campos.Z;

                clientClimateCond = capi.World.BlockAccessor.GetClimateAt(plrPos);
                quarterSecAccum = 0;
            }

            if (clientClimateCond == null) return;

            float tempfac = GameMath.Clamp((Math.Max(0, -clientClimateCond.Temperature) - 5) / 15f, 0, 1);
           
            col.W = GameMath.Clamp(1 - 1.5f * capi.World.Calendar.DayLightStrength, 0, 1) * tempfac;

            if (col.W <= 0) return;

            prog.Use();
            prog.Uniform("color", col);
            prog.Uniform("rgbaFogIn", capi.Ambient.BlendedFogColor);
            prog.Uniform("fogMinIn", capi.Ambient.BlendedFogMin);
            prog.Uniform("fogDensityIn", capi.Ambient.BlendedFogDensity);
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);

            prog.Uniform("flatFogDensity", capi.Ambient.BlendedFlatFogDensity);
            prog.Uniform("flatFogStart", capi.Ambient.BlendedFlatFogYPosForShader - (float)campos.Y);

            float speedmul = capi.World.Calendar.SpeedOfTime / 60f;
            prog.Uniform("auroraCounter", (float)(capi.InWorldEllapsedMilliseconds / 5000.0 * speedmul) % 579f);


            mvMat
                .Set(capi.Render.MvMatrix.Top)
                .FollowPlayer()
                .Translate(0, 1.1f * capi.World.BlockAccessor.MapSizeY + 0.5 - capi.World.Player.Entity.CameraPos.Y, 0)
            ;

            prog.UniformMatrix("modelViewMatrix", mvMat.Values);
            capi.Render.RenderMesh(quadTilesRef);

            prog.Stop();
        }
        


        
        public void InitQuads()
        {
            quadTilesRef?.Dispose();
            float height = 200;

            MeshData mesh = new MeshData(4, 6, false, true, true, false);
            mesh.CustomFloats = new CustomMeshDataPartFloat(4);
            mesh.CustomFloats.InterleaveStride = 4;
            mesh.CustomFloats.InterleaveOffsets = new int[] { 0 };
            mesh.CustomFloats.InterleaveSizes = new int[] { 1 };

            float x, y, z;
            Random rnd = new Random();

            float resolution = 1.5f;
            float spread = 1.5f;

            float parts = 20 * resolution;
            float advance = 1f / resolution;

            for (int i = 0; i < 15; i++)
            {
                Vec3f dir = new Vec3f((float)rnd.NextDouble() * 20 - 10, (float)rnd.NextDouble() * 5 - 3, (float)rnd.NextDouble() * 20 - 10);
                dir.Normalize();
                dir.Mul(advance);

                x = spread * ((float)rnd.NextDouble() * 800 - 400);
                y = spread * ((float)rnd.NextDouble() * 80 - 40);
                z = spread * ((float)rnd.NextDouble() * 800 - 400);
                

                for (int j = 0; j < parts+2; j++)
                {
                    float lngx = (float)rnd.NextDouble() * 5 + 20;
                    float lngy = (float)rnd.NextDouble() * 4 + 4;
                    float lngz = (float)rnd.NextDouble() * 5 + 20;

                    x += dir.X * lngx;
                    y += dir.Y * lngy;
                    z += dir.Z * lngz;

                    int lastelement = mesh.VerticesCount;

                    mesh.AddVertex(x, y+height, z, j % 2, 1);
                    mesh.AddVertex(x, y, z, j % 2, 0);

                    float f = j / (parts-1);
                    mesh.CustomFloats.Add(f, f);

                    if (j > 0 && j < (parts - 1))
                    {
                        mesh.AddIndex(lastelement + 1);
                        mesh.AddIndex(lastelement + 3);
                        mesh.AddIndex(lastelement + 2);
                        mesh.AddIndex(lastelement + 0);
                        mesh.AddIndex(lastelement + 1);
                        mesh.AddIndex(lastelement + 2);
                    }
                }
            }

            quadTilesRef = capi.Render.UploadMesh(mesh);
        }




        public void Dispose()
        {
            capi.Render.DeleteMesh(quadTilesRef);
        }


    }
}
