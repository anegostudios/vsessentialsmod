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
    public class CloudRendererDummy
    {
        public int CloudTileLength = 5;

        // Current cloud tile position of the player
        public int tilePosX;
        public int tilePosZ;

        // Offset X/Z counts up/down to -blocksize or +blocksize. Then it resets to 0 and shifts the noise pattern by 1 pixel
        public double windOffsetX;
        public double windOffsetZ;
        public int tileOffsetX;
        public int tileOffsetZ;

    }

    public class CloudRenderer : CloudRendererDummy, IRenderer
    {
        CloudTile[] cloudTiles = new CloudTile[0];

        CloudTile[] cloudTilesTmp = new CloudTile[0];

        public int CloudTileSize = 50;

        internal float blendedCloudDensity = 0;//-1.3f;
        internal float blendedGlobalCloudBrightness = 0;//0.85f;

        
        public int QuantityCloudTiles = 25;


        MeshRef cloudTilesMeshRef;
        long windChangeTimer = 0;
        long densityChangeTimer = 0;

        // Windspeed adds/subs from offsetX/Z
        float windSpeedX;
        float windSpeedZ;

        float windDirectionX;
        float windDirectionZ;





        Random rand;

        bool renderClouds;

        MeshData updateMesh = new MeshData()
        {
            //CustomFloats = new CustomMeshDataPartFloat(),
            CustomBytes = new CustomMeshDataPartByte(),
            CustomInts = new CustomMeshDataPartInt()
        };

        ICoreClientAPI capi;
        IShaderProgram prog;
        

        public double RenderOrder => 0.35;
        public int RenderRange => 9999;

        WeatherSimulation weatherSim;
        int cnt = 0;
        

        public CloudRenderer(ICoreClientAPI capi, WeatherSimulation weatherSim)
        {
            this.capi = capi;
            capi.Event.RegisterRenderer(this, EnumRenderStage.OIT);
            capi.Event.RegisterGameTickListener(OnGameTick, 20);

            this.weatherSim = weatherSim;

            capi.Event.ReloadShader += LoadShader;
            LoadShader();

            rand = new Random(capi.World.Seed);
            

            double time = capi.World.Calendar.TotalHours * 60;
            windOffsetX += 2f * time;
            windOffsetZ += 0.1f * time;

            tileOffsetX += (int)(windOffsetX / CloudTileSize);
            windOffsetX %= CloudTileSize;

            tileOffsetZ += (int)(windOffsetZ / CloudTileSize);
            windOffsetZ %= CloudTileSize;


            InitCloudTiles((8 * capi.World.Player.WorldData.DesiredViewDistance));
            LoadCloudModel();

            
            capi.Settings.AddWatcher<int>("viewDistance", OnViewDistanceChanged);
            capi.Settings.AddWatcher<bool>("renderClouds", (val) => renderClouds = val);

            renderClouds = capi.Settings.Bool["renderClouds"];

            InitCustomDataBuffers(updateMesh);
        }


        public bool LoadShader()
        {
            prog = capi.Shader.NewShaderProgram();

            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

            capi.Shader.RegisterFileShaderProgram("clouds", prog);

            return prog.Compile();
        }

        private void OnViewDistanceChanged(int newValue)
        {
            InitCloudTiles((8 * capi.World.Player.WorldData.DesiredViewDistance));
            UpdateCloudTiles();
            LoadCloudModel();
            InitCustomDataBuffers(updateMesh);
        }

        bool rebuild = false;

        #region Render, Tick
        public void OnGameTick(float deltaTime)
        {
            if (!renderClouds) return;

            rebuild = false;

            deltaTime = Math.Min(deltaTime, 1);
            deltaTime *= capi.World.Calendar.SpeedOfTime / 60f;
            if (deltaTime > 0)
            {
                UpdateWindAndClouds(deltaTime);
            }

            int currentTilePosX = (int)(capi.World.Player.Entity.Pos.X) / CloudTileSize;
            int currentTilePosZ = (int)(capi.World.Player.Entity.Pos.Z) / CloudTileSize;

            // Player has entered a new cloud tile position
            if (tilePosX != currentTilePosX || tilePosZ != currentTilePosZ)
            {
                MoveCloudTiles(tilePosX - currentTilePosX, tilePosZ - currentTilePosZ);
                tilePosX = currentTilePosX;
                tilePosZ = currentTilePosZ;

                rebuild = true;
            }


            if (rebuild || cnt++ > 10)
            {
                UpdateCloudTiles();
                UpdateBufferContents(updateMesh);
                capi.Render.UpdateMesh(cloudTilesMeshRef, updateMesh);
                cnt = 0;
            }

            capi.World.FrameProfiler.Mark("gt-clouds");
        }



        internal void UpdateWindAndClouds(float deltaTime)
        {
            if (windChangeTimer - capi.ElapsedMilliseconds < 0)
            {
                windChangeTimer = capi.ElapsedMilliseconds + rand.Next(20000, 120000);
                windDirectionX = (float)rand.NextDouble() * 5f;
                windDirectionZ = (float)rand.NextDouble() * 0.2f;
            }

            // Wind speed direction change smoothing 
            windSpeedX = windSpeedX + (windDirectionX - windSpeedX) * deltaTime;
            windSpeedZ = windSpeedZ + (windDirectionZ - windSpeedZ) * deltaTime;

            windOffsetX += windSpeedX * deltaTime;
            windOffsetZ += windSpeedZ * deltaTime;

            if (windOffsetX > CloudTileSize)
            {
                MoveCloudTiles(1, 0);
                tileOffsetX++;
                windOffsetX = windSpeedX > 0 ? windOffsetX - CloudTileSize : windOffsetX + CloudTileSize;
                rebuild = true;
            }

            if (windOffsetZ > CloudTileSize)
            {
                MoveCloudTiles(0, 1);
                tileOffsetZ++;
                windOffsetZ = windSpeedZ > 0 ? windOffsetZ - CloudTileSize : windOffsetZ + CloudTileSize;
                rebuild = true;
            }

            if (capi.ElapsedMilliseconds - densityChangeTimer > 25)
            {
                densityChangeTimer = capi.ElapsedMilliseconds;
                

                if (Math.Abs(blendedCloudDensity - capi.Ambient.BlendedCloudDensity) > 0.01)
                {
                    blendedCloudDensity = blendedCloudDensity + (capi.Ambient.BlendedCloudDensity - blendedCloudDensity) * deltaTime;
                    rebuild = true;
                }

                if (Math.Abs(blendedGlobalCloudBrightness - capi.Ambient.BlendedCloudBrightness) > 0.01)
                {
                    blendedGlobalCloudBrightness =
                        GameMath.Clamp(blendedGlobalCloudBrightness + (capi.Ambient.BlendedCloudBrightness - blendedGlobalCloudBrightness) * deltaTime, 0, 1);
                    rebuild = true;
                }
            }


            
        }


        Matrixf mvMat = new Matrixf();
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            capi.Render.GlMatrixModeModelView();

            if (!renderClouds || capi.Render.FrameWidth == 0) return;

            double partialTileOffsetX = capi.World.Player.Entity.Pos.X % CloudTileSize;
            double partialTileOffsetZ = capi.World.Player.Entity.Pos.Z % CloudTileSize;

            prog.Use();
            prog.Uniform("sunPosition", capi.World.Calendar.SunPositionNormalized);

            int[] hsv = ColorUtil.RgbToHsvInts((int)(capi.World.Calendar.SunColor.R * 255), (int)(capi.World.Calendar.SunColor.G * 255), (int)(capi.World.Calendar.SunColor.B * 255));
            hsv[1] = (int)(hsv[1] * 0.9);
            hsv[2] = (int)(hsv[2] * 0.9);
            int[] rgba = ColorUtil.Hsv2RgbInts(hsv[0], hsv[1], hsv[2]);
            Vec3f sunCol = new Vec3f(rgba[0] / 255f, rgba[1] / 255f, rgba[2] / 255f);

            prog.Uniform("sunColor", sunCol);
            prog.Uniform("dayLight", Math.Max(0, capi.World.Calendar.DayLightStrength + capi.World.Calendar.MoonLightStrength - 0.15f));
            prog.Uniform("windOffset", new Vec3f(
                (float)(windOffsetX - partialTileOffsetX),
                0,
                (float)(windOffsetZ - partialTileOffsetZ)
            ));
            prog.Uniform("globalCloudBrightness", blendedGlobalCloudBrightness);
            prog.Uniform("rgbaFogIn", capi.Ambient.BlendedFogColor);
            prog.Uniform("fogMinIn", capi.Ambient.BlendedFogMin);
            prog.Uniform("fogDensityIn", capi.Ambient.BlendedFogDensity);
            prog.Uniform("playerPos", capi.World.Player.Entity.Pos.XYZFloat);
            
            prog.Uniform("cloudTileSize", CloudTileSize);
            prog.Uniform("cloudsLength", (float)CloudTileSize * CloudTileLength);
            
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);

            //int cnt = capi.Render.PointLightsCount;
            //prog.Uniform("pointLightQuantity", cnt);
            //shv.PointLightsArray(cnt, ScreenManager.Platform.PointLights3);
            //shv.PointLightColorsArray(cnt, ScreenManager.Platform.PointLightColors3);

            prog.Uniform("flatFogDensity", capi.Ambient.BlendedFlatFogDensity);
            prog.Uniform("flatFogStart", capi.Ambient.BlendedFlatFogYPosForShader);


            mvMat
                .Set(capi.Render.MvMatrix.Top)
                .FollowPlayer()
                .Translate(
                    windOffsetX - partialTileOffsetX,
                    (int)(1 * capi.World.BlockAccessor.MapSizeY) + 0.5 - capi.World.Player.Entity.Pos.Y,
                    windOffsetZ - partialTileOffsetZ
                )
            ;

            prog.UniformMatrix("modelViewMatrix", mvMat.Values);
            capi.Render.RenderMeshInstanced(cloudTilesMeshRef, QuantityCloudTiles);
            

            // Very slightly rotate all the clouds so it looks better when they pass through blocks 
            // Putting this line before translate = Clouds no longer centered around the player
            // Putting it after the translate = Clouds hop by a few pixels on every reload
            // Need to find a correct solution for this
            // Mat4.Rotate(mv, mv, 0.04f, new float[] { 0, 1, 0 });
            
            prog.Stop();
            
        }
        #endregion


        #region Init/Setup, Rebuild
        public void InitCloudTiles(int viewDistance)
        {
            // 1 cloud tile = 30 blocks
            // Min amount of cloud tiles = 5*5
            // Also we'll display clouds triple as far as block view distance

            CloudTileLength = GameMath.Clamp(viewDistance / CloudTileSize, 20, 200);

            QuantityCloudTiles = CloudTileLength * CloudTileLength;
            cloudTiles = new CloudTile[QuantityCloudTiles];
            cloudTilesTmp = new CloudTile[QuantityCloudTiles];

            int i = 0;
            for (int x = 0; x < CloudTileLength; x++)
            {
                for (int z = 0; z < CloudTileLength; z++)
                {
                    cloudTiles[i++] = new CloudTile()
                    {
                        XOffset = x,
                        ZOffset = z,
                        brightnessRand = new LCGRandom(rand.Next())
                    };
                }
            }
        }

        

        public void MoveCloudTiles(int dx, int dz)
        {
            for (int x = 0; x < CloudTileLength; x++)
            {
                for (int z = 0; z < CloudTileLength; z++)
                {
                    int newX = GameMath.Mod(x + dx, CloudTileLength);
                    int newZ = GameMath.Mod(z + dz, CloudTileLength);

                    CloudTile tile = cloudTiles[x * CloudTileLength + z];
                    tile.XOffset = newX;
                    tile.ZOffset = newZ;

                    cloudTilesTmp[newX * CloudTileLength + newZ] = tile;
                }
            }

            CloudTile[] tmp = cloudTiles;
            cloudTiles = cloudTilesTmp;
            cloudTilesTmp = tmp;
        }


        private readonly ParallelOptions _pOptions = new ParallelOptions { MaxDegreeOfParallelism = 16 };
        public void UpdateCloudTiles()
        {
            weatherSim.EnsureNoiseCacheIsFresh();

            // Load density from perlin noise
            int cnt = CloudTileLength * CloudTileLength;
            int end = CloudTileLength - 1;

            //int len = width * height;
            //Parallel.For(0, len, _pOptions, i =>
            Parallel.For(0, cnt, _pOptions, i =>
            //for (int i = 0; i < cnt; i++)
            {
                int dx = i % CloudTileLength;
                int dz = i / CloudTileLength;

                int x = (tilePosX + dx - CloudTileLength / 2 - tileOffsetX);
                int z = (tilePosZ + dz - CloudTileLength / 2 - tileOffsetZ);
                CloudTile cloudTile = cloudTiles[dx * CloudTileLength + dz];
                cloudTile.brightnessRand.InitPositionSeed(x, z);
                double density = weatherSim.GetBlendedCloudDensityAt(dx, dz);

                cloudTile.MaxDensity = (byte)GameMath.Clamp((int)((64 * 255 * density * 3)) / 64, 0, 255);
                cloudTile.Brightness = (byte)(225 + cloudTile.brightnessRand.NextInt(31));
                cloudTile.YOffset = (float)weatherSim.GetBlendedCloudOffsetYAt(dx, dz);

                float changeVal = GameMath.Clamp(cloudTile.MaxDensity - cloudTile.SelfDensity, -1, 1);
                cloudTile.SelfDensity += (byte)changeVal;


                /// North: Negative Z
                /// East: Positive X
                /// South: Positive Z
                /// West: Negative X
                if (dx > 0)
                {
                    cloudTiles[(dx - 1) * CloudTileLength + dz].EastDensity = cloudTile.SelfDensity;
                }
                if (dz > 0)
                {
                    cloudTiles[dx * CloudTileLength + dz - 1].SouthDensity = cloudTile.SelfDensity;
                }
                if (dx < end)
                {
                    cloudTiles[(dx + 1) * CloudTileLength + dz].WestDensity = cloudTile.SelfDensity;
                }
                if (dz < end)
                {
                    cloudTiles[dx * CloudTileLength + dz + 1].NorthDensity = cloudTile.SelfDensity;
                }
            }

            );
        }


        public void LoadCloudModel()
        {
            MeshData modeldata = new MeshData(4 * 6, 6 * 6, false, false, true);
            modeldata.Rgba2 = null;
            modeldata.Flags = new int[4 * 6];

            float[] CloudSideShadings = new float[] {
                1f,
                0.9f,
                0.9f,
                0.7f
            };

            
            MeshData tile = CloudMeshUtil.GetCubeModelDataForClouds(
                CloudTileSize / 2,
                CloudTileSize / 4,
                new Vec3f(0,0,0)
            );

            byte[] rgba = CloudMeshUtil.GetShadedCubeRGBA(ColorUtil.WhiteArgb, CloudSideShadings, false);
            tile.SetRgba(rgba);
            tile.Flags = new int[] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5 };

            modeldata.AddMeshData(tile);

            // modeldata will hold xyz (+offset), indices and cloud density

            InitCustomDataBuffers(modeldata);
            UpdateBufferContents(modeldata);

            cloudTilesMeshRef?.Dispose();
            cloudTilesMeshRef = capi.Render.UploadMesh(modeldata);
        }


        void InitCustomDataBuffers(MeshData modeldata)
        {
            /*modeldata.CustomFloats = new CustomMeshDataPartFloat()
            {
                StaticDraw = false,
                Instanced = true,
                InterleaveSizes = new int[] { 1 },
                InterleaveOffsets = new int[] { 0 },
                InterleaveStride = 4,
                Values = new float[QuantityCloudTiles],
                Count = QuantityCloudTiles
            };*/

            modeldata.CustomInts = new CustomMeshDataPartInt()
            {
                StaticDraw = false,
                Instanced = true, 
                InterleaveSizes = new int[] { 2 },
                InterleaveOffsets = new int[] { 0, 4 },
                InterleaveStride = 8,
                Values = new int[QuantityCloudTiles * 2],
                Count = QuantityCloudTiles * 2
            };

            modeldata.CustomBytes = new CustomMeshDataPartByte()
            {
                StaticDraw = false,
                Instanced = true,
                InterleaveSizes = new int[] { 4, 1, 1 },
                InterleaveOffsets = new int[] { 0, 4, 5 },
                InterleaveStride = 8,
                Values = new byte[QuantityCloudTiles * 8],
                Count = QuantityCloudTiles * 8,
                Conversion = DataConversion.NormalizedFloat
            };
        }


        void UpdateBufferContents(MeshData mesh)
        {
            //int floatsPosition = 0;
            int intsPosition = 0;
            int bytesPosition = 0;

            for (int i = 0; i < cloudTiles.Length; i++)
            {
                CloudTile cloud = cloudTiles[i];

                mesh.CustomInts.Values[intsPosition++] = (cloud.XOffset - CloudTileLength / 2);
                mesh.CustomInts.Values[intsPosition++] = (cloud.ZOffset - CloudTileLength / 2);

                mesh.CustomBytes.Values[bytesPosition++] = cloud.NorthDensity;
                mesh.CustomBytes.Values[bytesPosition++] = cloud.EastDensity;
                mesh.CustomBytes.Values[bytesPosition++] = cloud.SouthDensity;
                mesh.CustomBytes.Values[bytesPosition++] = cloud.WestDensity;

                mesh.CustomBytes.Values[bytesPosition++] = cloud.SelfDensity;
                mesh.CustomBytes.Values[bytesPosition++] = cloud.Brightness;

                bytesPosition+=2;
            }
        }
        
        #endregion
            



        public void Dispose()
        {
            capi.Render.DeleteMesh(cloudTilesMeshRef);
        }

        
    }
}
