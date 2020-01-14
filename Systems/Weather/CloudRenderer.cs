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

        public virtual void UpdateCloudTiles(int changeSpeed = 1) { }

    }

    public class CloudRenderer : CloudRendererDummy, IRenderer
    {
        CloudTile[] cloudTiles = new CloudTile[0];

        CloudTile[] cloudTilesTmp = new CloudTile[0];

        public int CloudTileSize = 50;

        internal float blendedCloudDensity = 0;
        internal float blendedGlobalCloudBrightness = 0;

        
        public int QuantityCloudTiles = 25;


        MeshRef cloudTilesMeshRef;
        long windChangeTimer = 0;
        long densityChangeTimer = 0;

        // Windspeed adds/subs from offsetX/Z
        float windSpeedX;
        float windSpeedZ;

        float windDirectionX;
        float windDirectionZ;

        double partialTileOffsetX, partialTileOffsetZ;



        Random rand;

        bool renderClouds;

        MeshData updateMesh = new MeshData()
        {
            CustomShorts = new CustomMeshDataPartShort()
        };

        ICoreClientAPI capi;
        IShaderProgram prog;
        

        public double RenderOrder => 0.35;
        public int RenderRange => 9999;

        WeatherSystemClient weatherSys;
        int cnt = 0;
        

        public CloudRenderer(ICoreClientAPI capi, WeatherSystemClient weatherSys)
        {
            this.capi = capi;
            capi.Event.RegisterRenderer(this, EnumRenderStage.OIT);
            

            this.weatherSys = weatherSys;

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
        float accum;

        #region Render, Tick
        public void CloudTick(float deltaTime)
        {
            rebuild = false;
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

            accum += deltaTime;
            if (!rebuild && accum < 0.02f) return;

            // Every 20 milliseconds
            accum = accum % 0.02f;


            deltaTime = Math.Min(deltaTime, 1);
            deltaTime *= capi.World.Calendar.SpeedOfTime / 60f;
            if (deltaTime > 0)
            {
                UpdateWindAndClouds(deltaTime);
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
            if (!renderClouds) return;

            if (!capi.IsGamePaused)
            {
                CloudTick(deltaTime);
            }
            
            if (capi.Render.FrameWidth == 0) return;

            prog.Use();
            prog.Uniform("sunPosition", capi.World.Calendar.SunPositionNormalized);

            

            partialTileOffsetX = capi.World.Player.Entity.Pos.X % CloudTileSize;
            partialTileOffsetZ = capi.World.Player.Entity.Pos.Z % CloudTileSize;


            prog.Uniform("sunColor", capi.World.Calendar.SunColor);
            prog.Uniform("dayLight", Math.Max(0, capi.World.Calendar.DayLightStrength - capi.World.Calendar.MoonLightStrength*0.95f));
            prog.Uniform("windOffset", new Vec3f(
                (float)(windOffsetX - partialTileOffsetX),
                0,
                (float)(windOffsetZ - partialTileOffsetZ)
            ));
            prog.Uniform("globalCloudBrightness", blendedGlobalCloudBrightness);

            prog.Uniform("rgbaFogIn", capi.Ambient.BlendedFogColor);
            prog.Uniform("fogMinIn", capi.Ambient.BlendedFogMin);
            prog.Uniform("fogDensityIn", capi.Ambient.BlendedFogDensity);
            prog.Uniform("playerPos", capi.Render.ShaderUniforms.PlayerPos);
            prog.Uniform("tileOffset", new Vec2f((tilePosX - tileOffsetX) * CloudTileSize, (tilePosZ - tileOffsetZ) * CloudTileSize));

            prog.Uniform("cloudTileSize", CloudTileSize);
            prog.Uniform("cloudsLength", (float)CloudTileSize * CloudTileLength);
            prog.Uniform("cloudOpaqueness", (float)weatherSys.GetBlendedCloudOpaqueness());
            prog.Uniform("thinCloudMode", (float)weatherSys.GetBlendedThinCloudModeness());
            prog.Uniform("undulatingCloudMode", (float)weatherSys.GetBlendedUndulatingCloudModeness());
            prog.Uniform("cloudCounter", (float)((capi.World.Calendar.TotalHours * 20) % 578f));
            

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
                    (weatherSys.CloudsYPosition * capi.World.BlockAccessor.MapSizeY + 0.5 - capi.World.Player.Entity.CameraPos.Y),
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
                        XOffset = (short)(x - CloudTileLength/2),
                        ZOffset = (short)(z - CloudTileLength/2),
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
                    tile.XOffset = (short)(newX - CloudTileLength / 2);
                    tile.ZOffset = (short)(newZ - CloudTileLength / 2);

                    cloudTilesTmp[newX * CloudTileLength + newZ] = tile;
                }
            }

            CloudTile[] tmp = cloudTiles;
            cloudTiles = cloudTilesTmp;
            cloudTilesTmp = tmp;
        }


        private readonly ParallelOptions _pOptions = new ParallelOptions { MaxDegreeOfParallelism = 8 };
        public override void UpdateCloudTiles(int changeSpeed = 64)
        {
            weatherSys.EnsureNoiseCacheIsFresh();

            // Load density from perlin noise
            int cnt = CloudTileLength * CloudTileLength;
            int end = CloudTileLength - 1;

            weatherSys.LoadAdjacentSimsAndLerpValues(capi.World.Player.Entity.Pos.XYZ);
            WeatherSimulation[] sims = weatherSys.adjacentSims;

            double lerpLeftRight = weatherSys.lerpLeftRight;
            double lerpTopBot = weatherSys.lerpTopBot;



            Parallel.For(0, cnt, _pOptions, i =>
            //for (int i = 0; i < cnt; i++)
            {
                int dx = i % CloudTileLength;
                int dz = i / CloudTileLength;

                int x = (tilePosX + dx - CloudTileLength / 2 - tileOffsetX);
                int z = (tilePosZ + dz - CloudTileLength / 2 - tileOffsetZ);
                CloudTile cloudTile = cloudTiles[dx * CloudTileLength + dz];
                cloudTile.brightnessRand.InitPositionSeed(x, z);
                double density = weatherSys.GetBlendedCloudThicknessAt(dx, dz, sims, lerpLeftRight, lerpTopBot);

                cloudTile.MaxThickness = (short)GameMath.Clamp(density * short.MaxValue, 0, short.MaxValue);

                cloudTile.Brightness = (short)((0.88f + cloudTile.brightnessRand.NextFloat()* 0.12f) * short.MaxValue);

                float changeVal = GameMath.Clamp((int)cloudTile.MaxThickness - (int)cloudTile.SelfThickness, -changeSpeed, changeSpeed);
                cloudTile.SelfThickness = (short)GameMath.Clamp(cloudTile.SelfThickness+changeVal, 0, short.MaxValue);


                /// North: Negative Z
                /// East: Positive X
                /// South: Positive Z
                /// West: Negative X
                if (dx > 0)
                {
                    cloudTiles[(dx - 1) * CloudTileLength + dz].EastThickness = cloudTile.SelfThickness;
                }
                if (dz > 0)
                {
                    cloudTiles[dx * CloudTileLength + dz - 1].SouthThickness = cloudTile.SelfThickness;
                }
                if (dx < end)
                {
                    cloudTiles[(dx + 1) * CloudTileLength + dz].WestThickness = cloudTile.SelfThickness;
                }
                if (dz < end)
                {
                    cloudTiles[dx * CloudTileLength + dz + 1].NorthThickness = cloudTile.SelfThickness;
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
            modeldata.CustomShorts = new CustomMeshDataPartShort()
            {
                StaticDraw = false,
                Instanced = true,
                InterleaveSizes = new int[] { 2, 4, 1, 1 },
                InterleaveOffsets = new int[] { 0, 4, 12, 14 },
                InterleaveStride = 16,
                Conversion = DataConversion.NormalizedFloat,
                Values = new short[QuantityCloudTiles * 8],
                Count = QuantityCloudTiles * 8,
            };
        }


        void UpdateBufferContents(MeshData mesh)
        {
            int pos = 0;

            for (int i = 0; i < cloudTiles.Length; i++)
            {
                CloudTile cloud = cloudTiles[i];
                
                mesh.CustomShorts.Values[pos++] = (short)(CloudTileSize * cloud.XOffset);
                mesh.CustomShorts.Values[pos++] = (short)(CloudTileSize * cloud.ZOffset);
                
                mesh.CustomShorts.Values[pos++] = cloud.NorthThickness;
                mesh.CustomShorts.Values[pos++] = cloud.EastThickness;
                mesh.CustomShorts.Values[pos++] = cloud.SouthThickness;
                mesh.CustomShorts.Values[pos++] = cloud.WestThickness;

                mesh.CustomShorts.Values[pos++] = cloud.SelfThickness;
                mesh.CustomShorts.Values[pos++] = cloud.Brightness;
            }
        }
        
        #endregion
            



        public void Dispose()
        {
            capi.Render.DeleteMesh(cloudTilesMeshRef);
        }

        
    }
}
