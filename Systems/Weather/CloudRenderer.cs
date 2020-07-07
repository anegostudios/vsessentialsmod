using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class CloudRendererBase
    {
        public int CloudTileLength = 5;
        public int CloudTileSize { get; set; } = 50;


        // Offset X/Z counts up/down to -blocksize or +blocksize. Then it resets to 0 and shifts the noise pattern by 1 pixel
        public double windOffsetX;
        public double windOffsetZ;

        public virtual void UpdateCloudTiles(int changeSpeed = 1) { }
    }

    public class CloudTilesState
    {
        public Vec3i CenterTilePos = new Vec3i();
        public int TileOffsetX;
        public int TileOffsetZ;

        public int WindTileOffsetX;
        public int WindTileOffsetZ;

        public void Set(CloudTilesState state)
        {
            TileOffsetX = state.TileOffsetX;
            TileOffsetZ = state.TileOffsetZ;

            WindTileOffsetX = state.WindTileOffsetX;
            WindTileOffsetZ = state.WindTileOffsetZ;

            CenterTilePos.X = state.CenterTilePos.X;
            CenterTilePos.Z = state.CenterTilePos.Z;
        }
    }

    public class CloudRenderer : CloudRendererBase, IRenderer
    {
        CloudTilesState committedState = new CloudTilesState();
        CloudTilesState mainThreadState = new CloudTilesState();
        CloudTilesState offThreadState = new CloudTilesState();

        CloudTile[] Tiles;
        CloudTile[] tempTiles;

        bool newStateRready = false;
        object cloudStateLock = new object();


        internal float blendedCloudDensity = 0;
        internal float blendedGlobalCloudBrightness = 0;
        public int QuantityCloudTiles = 25;
        MeshRef cloudTilesMeshRef;
        long windChangeTimer = 0;

        // Windspeed adds/subs from offsetX/Z
        float cloudSpeedX;
        float cloudSpeedZ;

        float targetCloudSpeedX;
        float targetCloudSpeedZ;

        Random rand;
        bool renderClouds;
        WeatherSystemClient weatherSys;
        
        Thread cloudTileUpdThread;
        bool isShuttingDown = false;

        ICoreClientAPI capi;
        IShaderProgram prog;
        Matrixf mvMat = new Matrixf();

        int cloudTileBlendSpeed = 32;


        MeshData updateMesh = new MeshData()
        {
            CustomShorts = new CustomMeshDataPartShort()
        };

        public double RenderOrder => 0.35;
        public int RenderRange => 9999;

        WeatherDataReaderPreLoad wreaderpreload;

        public CloudRenderer(ICoreClientAPI capi, WeatherSystemClient weatherSys)
        {
            this.capi = capi;
            this.weatherSys = weatherSys;
            this.wreaderpreload = weatherSys.getWeatherDataReaderPreLoad();
            rand = new Random(capi.World.Seed);

            capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "clouds");
            capi.Event.ReloadShader += LoadShader;

            LoadShader();

            
            
            double time = capi.World.Calendar.TotalHours * 60;
            windOffsetX += 2f * time;
            windOffsetZ += 0.1f * time;

            mainThreadState.WindTileOffsetX += (int)(windOffsetX / CloudTileSize);
            windOffsetX %= CloudTileSize;

            mainThreadState.WindTileOffsetZ += (int)(windOffsetZ / CloudTileSize);
            windOffsetZ %= CloudTileSize;

            offThreadState.Set(mainThreadState);
            committedState.Set(mainThreadState);

            InitCloudTiles((8 * capi.World.Player.WorldData.DesiredViewDistance));
            LoadCloudModel();

            
            capi.Settings.AddWatcher<int>("viewDistance", OnViewDistanceChanged);
            capi.Settings.AddWatcher<bool>("renderClouds", (val) => renderClouds = val);

            renderClouds = capi.Settings.Bool["renderClouds"];

            InitCustomDataBuffers(updateMesh);

            capi.Event.LeaveWorld += () =>
            {
                isShuttingDown = true;
            };

            cloudTileUpdThread = new Thread(new ThreadStart(() =>
            {
                while (!isShuttingDown)
                {
                    if (!newStateRready)
                    {
                        int winddx = (int)windOffsetX / CloudTileSize;
                        int winddz = (int)windOffsetZ / CloudTileSize;

                        int prevx = offThreadState.CenterTilePos.X;
                        int prevz = offThreadState.CenterTilePos.Z;
                        offThreadState.Set(mainThreadState);
                        
                        offThreadState.WindTileOffsetX += winddx;
                        offThreadState.WindTileOffsetZ += winddz;

                        int dx = winddx + prevx - offThreadState.CenterTilePos.X;
                        int dz = winddz + prevz - offThreadState.CenterTilePos.Z;
                        if (dx != 0 || dz != 0)
                        {
                            MoveCloudTilesOffThread(dx, dz);
                        }

                        UpdateCloudTilesOffThread(instantTileBlend ? short.MaxValue : cloudTileBlendSpeed);

                        instantTileBlend = false;
                        newStateRready = true;
                    }

                    Thread.Sleep(40);
                }
            }));

            cloudTileUpdThread.IsBackground = true;
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
            requireTileRebuild = true;
        }


        #region Render, Tick

        bool isFirstTick = true;
        bool requireTileRebuild = false;
        public bool instantTileBlend = false;

        public void CloudTick(float deltaTime)
        {
            if (isFirstTick)
            {
                weatherSys.ProcessWeatherUpdates();
                UpdateCloudTilesOffThread(short.MaxValue);
                cloudTileUpdThread.Start();
                isFirstTick = false;
            }

            deltaTime = Math.Min(deltaTime, 1);
            deltaTime *= capi.World.Calendar.SpeedOfTime / 60f;
            if (deltaTime > 0)
            {
                if (windChangeTimer - capi.ElapsedMilliseconds < 0)
                {
                    windChangeTimer = capi.ElapsedMilliseconds + rand.Next(20000, 120000);
                    targetCloudSpeedX = (float)rand.NextDouble() * 5f;
                    targetCloudSpeedZ = (float)rand.NextDouble() * 0.5f;
                }

                //float windspeedx = 3 * (float)wreaderpreload.GetWindSpeed(capi.World.Player.Entity.Pos.Y); - likely wrong
                float windspeedx = 3 * (float)weatherSys.WeatherDataAtPlayer.GetWindSpeed(capi.World.Player.Entity.Pos.Y);

                // Wind speed direction change smoothing 
                cloudSpeedX = cloudSpeedX + (targetCloudSpeedX + windspeedx - cloudSpeedX) * deltaTime;
                cloudSpeedZ = cloudSpeedZ + (targetCloudSpeedZ - cloudSpeedZ) * deltaTime;
            }

            lock (cloudStateLock)
            {
                if (deltaTime > 0)
                {
                    windOffsetX += cloudSpeedX * deltaTime;
                    windOffsetZ += cloudSpeedZ * deltaTime;
                }

                mainThreadState.CenterTilePos.X = (int)(capi.World.Player.Entity.Pos.X) / CloudTileSize;
                mainThreadState.CenterTilePos.Z = (int)(capi.World.Player.Entity.Pos.Z) / CloudTileSize;
            }


            if (newStateRready)
            {
                int dx = offThreadState.WindTileOffsetX - committedState.WindTileOffsetX;
                int dz = offThreadState.WindTileOffsetZ - committedState.WindTileOffsetZ;

                committedState.Set(offThreadState);

                mainThreadState.WindTileOffsetX = committedState.WindTileOffsetX;
                mainThreadState.WindTileOffsetZ = committedState.WindTileOffsetZ;

                windOffsetX -= dx * CloudTileSize;
                windOffsetZ -= dz * CloudTileSize;
                
                UpdateBufferContents(updateMesh);
                capi.Render.UpdateMesh(cloudTilesMeshRef, updateMesh);

                weatherSys.ProcessWeatherUpdates();

                if (requireTileRebuild)
                {
                    InitCloudTiles((8 * capi.World.Player.WorldData.DesiredViewDistance));
                    UpdateCloudTiles();
                    LoadCloudModel();
                    InitCustomDataBuffers(updateMesh);
                    requireTileRebuild = false;
                    instantTileBlend = true;
                }

                newStateRready = false;
            }

            capi.World.FrameProfiler.Mark("gt-clouds");
        }



        
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


            double plrPosX = capi.World.Player.Entity.Pos.X;
            double plrPosZ = capi.World.Player.Entity.Pos.Z;

            double offsetX = committedState.CenterTilePos.X * CloudTileSize - plrPosX + windOffsetX;
            double offsetZ = committedState.CenterTilePos.Z * CloudTileSize - plrPosZ + windOffsetZ;


            prog.Uniform("sunColor", capi.World.Calendar.SunColor);
            prog.Uniform("dayLight", Math.Max(0, capi.World.Calendar.DayLightStrength - capi.World.Calendar.MoonLightStrength*0.95f));
            prog.Uniform("windOffset", new Vec3f((float)offsetX, 0, (float)offsetZ));

            

            prog.Uniform("rgbaFogIn", capi.Ambient.BlendedFogColor);
            prog.Uniform("fogMinIn", capi.Ambient.BlendedFogMin);
            prog.Uniform("fogDensityIn", capi.Ambient.BlendedFogDensity);
            prog.Uniform("playerPos", capi.Render.ShaderUniforms.PlayerPos);
            prog.Uniform("tileOffset", new Vec2f((committedState.CenterTilePos.X - committedState.TileOffsetX) * CloudTileSize, (committedState.CenterTilePos.Z - committedState.TileOffsetZ) * CloudTileSize));

            prog.Uniform("cloudTileSize", CloudTileSize);
            prog.Uniform("cloudsLength", (float)CloudTileSize * CloudTileLength);

            prog.Uniform("globalCloudBrightness", blendedGlobalCloudBrightness);
            
            float yTranslate = (float)(weatherSys.CloudsYPosition * capi.World.BlockAccessor.MapSizeY + 0.5 - capi.World.Player.Entity.CameraPos.Y);

            prog.Uniform("cloudYTranslate", yTranslate);
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
                .Translate(offsetX, yTranslate, offsetZ)
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
            Tiles = new CloudTile[QuantityCloudTiles];
            tempTiles = new CloudTile[QuantityCloudTiles];

            int seed = rand.Next();

            for (int x = 0; x < CloudTileLength; x++)
            {
                for (int z = 0; z < CloudTileLength; z++)
                {
                    Tiles[x * CloudTileLength + z] = new CloudTile()
                    {
                        GridXOffset = (short)(x - CloudTileLength / 2),
                        GridZOffset = (short)(z - CloudTileLength / 2),
                        brightnessRand = new LCGRandom(seed)
                    };
                }
            }
        }

        



        
        public void UpdateCloudTilesOffThread(int changeSpeed)
        {
            // Load density from perlin noise
            int cnt = CloudTileLength * CloudTileLength;

           
            int prevTopLeftRegX = -9999;
            int prevTopLeftRegZ = -9999;
            Vec3i tileOffset = new Vec3i(offThreadState.TileOffsetX - offThreadState.WindTileOffsetX, 0, offThreadState.TileOffsetZ - offThreadState.WindTileOffsetZ);
            
            Vec3i tileCenterPos = offThreadState.CenterTilePos;

            for (int i = 0; i < cnt; i++)
            {
                CloudTile cloudTile = Tiles[i];

                int tileXPos = tileCenterPos.X + cloudTile.GridXOffset;
                int tileZPos = tileCenterPos.Z + cloudTile.GridZOffset;

                cloudTile.brightnessRand.InitPositionSeed(tileXPos - offThreadState.WindTileOffsetX, tileZPos - offThreadState.WindTileOffsetZ);

                // This is a block position
                Vec3d cloudTilePos = new Vec3d(tileXPos * CloudTileSize, 0, tileZPos * CloudTileSize);

                int regSize = capi.World.BlockAccessor.RegionSize;
                int topLeftRegX = (int)Math.Round(cloudTilePos.X / regSize) - 1;
                int topLeftRegZ = (int)Math.Round(cloudTilePos.Z / regSize) - 1;

                if (topLeftRegX != prevTopLeftRegX || topLeftRegZ != prevTopLeftRegZ)
                {
                    prevTopLeftRegX = topLeftRegX;
                    prevTopLeftRegZ = topLeftRegZ;
                    wreaderpreload.LoadAdjacentSimsAndLerpValues(cloudTilePos);
                    wreaderpreload.EnsureCloudTileCacheIsFresh(tileOffset);
                } else 
                {
                    wreaderpreload.LoadLerp(cloudTilePos);
                }

                // This is the tile position relative to the current regions origin point
                int cloudTileX = (int)cloudTilePos.X / CloudTileSize;
                int cloudTileZ = (int)cloudTilePos.Z / CloudTileSize;

                // Here we need the cloud tile position relative to cloud tile pos 0/0 of the current region
                double density = GameMath.Clamp(wreaderpreload.GetBlendedCloudThicknessAt(cloudTileX, cloudTileZ), 0, 1);
                double bright = wreaderpreload.GetBlendedCloudBrightness(1) * (0.85f + cloudTile.brightnessRand.NextFloat() * 0.15f);


                cloudTile.TargetBrightnes = (short)(GameMath.Clamp(bright, 0, 1) * short.MaxValue);
                cloudTile.TargetThickness = (short)GameMath.Clamp(density * short.MaxValue, 0, short.MaxValue);
                cloudTile.TargetThinCloudMode = (short)GameMath.Clamp(wreaderpreload.GetBlendedThinCloudModeness() * short.MaxValue, 0, short.MaxValue);
                cloudTile.TargetCloudOpaquenes = (short)GameMath.Clamp(wreaderpreload.GetBlendedCloudOpaqueness() * short.MaxValue, 0, short.MaxValue);
                cloudTile.TargetUndulatingCloudMode = (short)GameMath.Clamp(wreaderpreload.GetBlendedUndulatingCloudModeness() * short.MaxValue, 0, short.MaxValue);

                cloudTile.Brightness = LerpTileValue(cloudTile.TargetBrightnes, cloudTile.Brightness, changeSpeed);
                cloudTile.SelfThickness = LerpTileValue(cloudTile.TargetThickness, cloudTile.SelfThickness, changeSpeed);
                cloudTile.ThinCloudMode = LerpTileValue(cloudTile.TargetThinCloudMode, cloudTile.ThinCloudMode, changeSpeed);
                cloudTile.CloudOpaqueness = LerpTileValue(cloudTile.TargetCloudOpaquenes, cloudTile.CloudOpaqueness, changeSpeed);
                cloudTile.UndulatingCloudMode = LerpTileValue(cloudTile.TargetUndulatingCloudMode, cloudTile.UndulatingCloudMode, changeSpeed);


                // North: Negative Z
                // South: Positive Z
                if (i > 0)
                {
                    Tiles[i - 1].NorthThickness = cloudTile.SelfThickness;
                }
                if (i < Tiles.Length - 1)
                {
                    Tiles[i+1].SouthThickness = cloudTile.SelfThickness;
                }

                // East: Positive X
                // West: Negative X
                if (i < CloudTileLength - 1)
                {
                    Tiles[i + CloudTileLength].EastThickness = cloudTile.SelfThickness;
                }
                if (i > CloudTileLength - 1)
                {
                    Tiles[i - CloudTileLength].WestThickness = cloudTile.SelfThickness;
                }
            }            
        }

        private short LerpTileValue(int target, int current, int changeSpeed)
        {
            float changeVal = GameMath.Clamp(target - current, -changeSpeed, changeSpeed);
            return (short)GameMath.Clamp(current + changeVal, 0, short.MaxValue);
        }

        public void MoveCloudTilesOffThread(int dx, int dz)
        {
            // We have to "physically" move the cloud tiles so that we can smoothly blend tile.SelfThickness
            for (int x = 0; x < CloudTileLength; x++)
            {
                for (int z = 0; z < CloudTileLength; z++)
                {
                    int newX = GameMath.Mod(x + dx, CloudTileLength);
                    int newZ = GameMath.Mod(z + dz, CloudTileLength);

                    CloudTile tile = Tiles[x * CloudTileLength + z];
                    tile.GridXOffset = (short)(newX - CloudTileLength / 2);
                    tile.GridZOffset = (short)(newZ - CloudTileLength / 2);

                    tempTiles[newX * CloudTileLength + newZ] = tile;
                }
            }

            var flip = Tiles;
            Tiles = tempTiles;
            tempTiles = flip;
        }



        public void LoadCloudModel()
        {
            MeshData modeldata = new MeshData(4 * 6, 6 * 6, false, false, true);
            //modeldata.Rgba2 = null;
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

            byte[] rgba = CubeMeshUtil.GetShadedCubeRGBA(ColorUtil.WhiteArgb, CloudSideShadings, false);
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
                InterleaveSizes = new int[] { 2, 4, 1, 1, 1, 1, 1, 1  },
                InterleaveOffsets = new int[] { 0, 4, 12, 14, 16, 18, 20, 22 },
                InterleaveStride = 24,
                Conversion = DataConversion.NormalizedFloat,
                Values = new short[QuantityCloudTiles * 12],
                Count = QuantityCloudTiles * 12,
            };
        }


        void UpdateBufferContents(MeshData mesh)
        {
            int pos = 0;


            for (int i = 0; i < Tiles.Length; i++)
            {
                CloudTile tile = Tiles[i];

                mesh.CustomShorts.Values[pos++] = (short)(CloudTileSize * tile.GridXOffset);
                mesh.CustomShorts.Values[pos++] = (short)(CloudTileSize * tile.GridZOffset);
                
                mesh.CustomShorts.Values[pos++] = tile.NorthThickness;
                mesh.CustomShorts.Values[pos++] = tile.EastThickness;
                mesh.CustomShorts.Values[pos++] = tile.SouthThickness;
                mesh.CustomShorts.Values[pos++] = tile.WestThickness;

                mesh.CustomShorts.Values[pos++] = tile.SelfThickness;
                mesh.CustomShorts.Values[pos++] = tile.Brightness;

                mesh.CustomShorts.Values[pos++] = tile.ThinCloudMode;
                mesh.CustomShorts.Values[pos++] = tile.UndulatingCloudMode;
                mesh.CustomShorts.Values[pos++] = tile.CloudOpaqueness;

                mesh.CustomShorts.Values[pos++] = 0; // nothing
            }
        }
        
        #endregion
            



        public void Dispose()
        {
            capi.Render.DeleteMesh(cloudTilesMeshRef);
        }

        
    }
}
