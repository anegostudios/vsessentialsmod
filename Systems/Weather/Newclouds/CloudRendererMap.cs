using System;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Common;
using OpenTK.Graphics.OpenGL;
//using HarmonyLib;

namespace FluffyClouds {

    public class CloudTile
    {
        public short GridXOffset; // Grid position
        public short GridZOffset; // Grid position

        public short TargetThickness;
        public short TargetBrightnes;
        public short TargetThinCloudMode;
        public short TargetUndulatingCloudMode;
        public short TargetCloudOpaquenes;

        public short SelfThickness;
        public short Brightness;
        public short ThinCloudMode;
        public short UndulatingCloudMode;
        public short CloudOpaqueness;

        public LCGRandom brightnessRand;

        internal bool rainValuesSet;
        internal float lerpRainCloudOverlay;
        internal float lerpRainOverlay;
    }

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

    public class CloudRendererMap : CloudRendererBase, IRenderer
    {
        int TextureData1;
        int TextureData2;
        short[] TextureDataBuffer1;
        short[] TextureDataBuffer2;

        int Framebuffer;
        public int TextureMap;
        public int TextureCol;

        public Vec3f offset = new Vec3f();
        MeshRef quad;
        IShaderProgram prog;
        int programId;

        Matrixf matrix = new Matrixf();
        float time = 0.0f;

        public CloudTilesState committedState = new CloudTilesState();
        public CloudTilesState mainThreadState = new CloudTilesState();
        public CloudTilesState offThreadState = new CloudTilesState();

        public CloudTile[] Tiles;
        CloudTile[] tempTiles;

        bool newStateRready = false;
        object cloudStateLock = new object();


        internal float blendedCloudDensity = 0;
        internal float blendedGlobalCloudBrightness = 0;
        public int QuantityCloudTiles = 25;
        long windChangeTimer = 0;

        // Windspeed adds/subs from offsetX/Z
        float cloudSpeedX;
        float cloudSpeedZ;

        float targetCloudSpeedX;
        float targetCloudSpeedZ;

        Random rand;
        bool renderCloudMap;
        public WeatherSystemClient weatherSys;
        
        Thread cloudTileUpdThread;
        bool isShuttingDown = false;

        ICoreClientAPI capi;
        ModSystem mod;

        int cloudTileBlendSpeed = 32;


        public double RenderOrder => 0.3;
        public int RenderRange => 9999;

        WeatherDataReaderPreLoad wreaderpreload;

        public CloudRendererMap(ModSystem mod, ICoreClientAPI capi)
        {

            WeatherSystemClient weatherSys = capi.ModLoader.GetModSystem<WeatherSystemBase>() as WeatherSystemClient;

            this.capi = capi;
            this.mod = mod;
            this.weatherSys = weatherSys;
            this.wreaderpreload = weatherSys.getWeatherDataReaderPreLoad();
            rand = new Random(capi.World.Seed);

            quad = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
//          capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "clouds");
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
            
            capi.Settings.AddWatcher<int>("viewDistance", OnViewDistanceChanged);
            capi.Settings.AddWatcher<int>("cloudRenderMode", (val) => renderCloudMap = val == 1);
            renderCloudMap = capi.Settings.Int.Get("cloudRenderMode") == 1;

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
            prog.AssetDomain = mod.Mod.Info.ModID;

            capi.Shader.RegisterFileShaderProgram("cloudmap", prog);
            
            bool success = prog.Compile();

            programId = prog.ProgramId; // (int)Traverse.Create(prog).Field("ProgramId").GetValue();

            return success;
        }

        void FreeGlResources(){

            GL.DeleteTexture(TextureData1);
            GL.DeleteTexture(TextureData2);
            GL.DeleteTexture(TextureMap);
            GL.DeleteTexture(TextureCol);
            GL.DeleteFramebuffer(Framebuffer);
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
            blendedCloudDensity = capi.Ambient.BlendedCloudDensity;
            blendedGlobalCloudBrightness = capi.Ambient.BlendedCloudBrightness;

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

                weatherSys.ProcessWeatherUpdates();

                if (requireTileRebuild)
                {
                    InitCloudTiles((8 * capi.World.Player.WorldData.DesiredViewDistance));
                    UpdateCloudTiles();
                    requireTileRebuild = false;
                    instantTileBlend = true;
                }

                WriteTexture();

                newStateRready = false;
            }

            capi.World.FrameProfiler.Mark("gt-clouds");
        }
        
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (!renderCloudMap) return;

            if(!capi.IsGamePaused){

                CloudTick(deltaTime);
                time = (time + deltaTime) % 86400.0f;
            }

            offset.X = (float)(committedState.CenterTilePos.X * CloudTileSize - capi.World.Player.Entity.Pos.X + windOffsetX);
            offset.Y = (float)(weatherSys.CloudLevelRel * capi.World.BlockAccessor.MapSizeY + 0.5 - capi.World.Player.Entity.CameraPos.Y);
            offset.Z = (float)(committedState.CenterTilePos.Z * CloudTileSize - capi.World.Player.Entity.Pos.Z + windOffsetZ);

            Vec2f offsetCentre = new Vec2f(
                (float)(((double)committedState.CenterTilePos.X * CloudTileSize - capi.World.DefaultSpawnPosition.X + windOffsetX) / CloudTileSize),
                (float)(((double)committedState.CenterTilePos.Z * CloudTileSize - capi.World.DefaultSpawnPosition.Z + windOffsetZ) / CloudTileSize)
            );

            matrix.Set(capi.Render.CameraMatrixOriginf);

            int fb;
            GL.GetInteger(GetPName.FramebufferBinding, out fb);

            int[] vp = new int[4];
            GL.GetInteger(GetPName.Viewport, vp);

            prog.Use();

            prog.Uniform("dayLight", Math.Max(0, capi.World.Calendar.DayLightStrength - capi.World.Calendar.MoonLightStrength*0.95f));
            prog.Uniform("globalCloudBrightness", capi.Ambient.BlendedCloudBrightness);
            prog.Uniform("time", time);
            prog.Uniform("rgbaFogIn", capi.Ambient.BlendedFogColor);
            prog.Uniform("fogMinIn", capi.Ambient.BlendedFogMin);
            prog.Uniform("fogDensityIn", capi.Ambient.BlendedFogDensity);
            prog.Uniform("sunPosition", capi.World.Calendar.SunPositionNormalized);
            prog.Uniform("nightVisionStrength", capi.Render.ShaderUniforms.NightVisionStrength);
            prog.Uniform("alpha", GameMath.Clamp(1 - 1.5f * Math.Max(0, capi.Render.ShaderUniforms.GlitchStrength - 0.1f), 0, 1));
            prog.Uniform("width", (float)CloudTileLength);
            prog.Uniform("mapOffset", offset);
            prog.Uniform("mapOffsetCentre", offsetCentre);
            prog.BindTexture2D("mapData1", TextureData1, 8);
            prog.BindTexture2D("mapData2", TextureData2, 9);
            prog.UniformMatrix("viewMatrix", matrix.Values);

            prog.Uniform("pointLightQuantity", capi.Render.ShaderUniforms.PointLightsCount);
            if(capi.Render.ShaderUniforms.PointLightsCount > 0){
                GL.Uniform3(GL.GetUniformLocation(programId, "pointLights"), capi.Render.ShaderUniforms.PointLightsCount, capi.Render.ShaderUniforms.PointLights3);
                GL.Uniform3(GL.GetUniformLocation(programId, "pointLightColors"), capi.Render.ShaderUniforms.PointLightsCount, capi.Render.ShaderUniforms.PointLightColors3);
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
            GL.Viewport(0, 0, CloudTileLength, CloudTileLength);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);

            capi.Render.RenderMesh(quad);

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fb);
            GL.Viewport(vp[0], vp[1], vp[2], vp[3]);

            prog.Stop();
            
        }
        #endregion

        void WriteTexture()
        {
            for(int i = 0; i < CloudTileLength * CloudTileLength; i++){

                var tile = Tiles[i];

                int half = CloudTileLength / 2;
                int j = (tile.GridZOffset + half) * CloudTileLength + tile.GridXOffset + half;

                TextureDataBuffer1[j * 4 + 0] = tile.ThinCloudMode;
                TextureDataBuffer1[j * 4 + 1] = tile.SelfThickness;
                TextureDataBuffer1[j * 4 + 2] = tile.CloudOpaqueness;
                TextureDataBuffer1[j * 4 + 3] = tile.Brightness;
                TextureDataBuffer2[j * 4]     = tile.UndulatingCloudMode;
            }

            GL.BindTexture(TextureTarget.Texture2D, TextureData1);
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, CloudTileLength, CloudTileLength, PixelFormat.Rgba, PixelType.Short, TextureDataBuffer1);
            GL.BindTexture(TextureTarget.Texture2D, TextureData2);
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, CloudTileLength, CloudTileLength, PixelFormat.Rgba, PixelType.Short, TextureDataBuffer2);

        }

        int makeTexture(int width, PixelInternalFormat internalFormat, PixelFormat format, PixelType type){
            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, width, 0, format, type, 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return texture;
        }

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

            FreeGlResources();

            TextureData1 = makeTexture(CloudTileLength, PixelInternalFormat.Rgba16, PixelFormat.Rgba, PixelType.Short);
            TextureData2 = makeTexture(CloudTileLength, PixelInternalFormat.Rgba16, PixelFormat.Rgba, PixelType.Short);

            TextureDataBuffer1 = new short[CloudTileLength * CloudTileLength * 4];
            TextureDataBuffer2 = new short[CloudTileLength * CloudTileLength * 4];

            int fb;
            GL.GetInteger(GetPName.FramebufferBinding, out fb);

            TextureMap = makeTexture(CloudTileLength, PixelInternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float);

            TextureCol = makeTexture(CloudTileLength, PixelInternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float);

            Framebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, TextureMap, 0);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, TextureCol, 0);

            DrawBuffersEnum[] buffers = { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 };
            GL.DrawBuffers(buffers.Length, buffers);

            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fb);

        }

        int accum = 20;
        
        public void UpdateCloudTilesOffThread(int changeSpeed)
        {
            bool reloadRainNoiseValues = false;
            accum++;
            if (accum > 10)
            {
                accum = 0;
                reloadRainNoiseValues = true;
            }

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
                Vec3d cloudTilePos = new Vec3d(tileXPos * CloudTileSize, capi.World.SeaLevel, tileZPos * CloudTileSize);

                int regSize = capi.World.BlockAccessor.RegionSize;
                int topLeftRegX = (int)Math.Round(cloudTilePos.X / regSize) - 1;
                int topLeftRegZ = (int)Math.Round(cloudTilePos.Z / regSize) - 1;

                if (topLeftRegX != prevTopLeftRegX || topLeftRegZ != prevTopLeftRegZ)
                {
                    prevTopLeftRegX = topLeftRegX;
                    prevTopLeftRegZ = topLeftRegZ;
                    wreaderpreload.LoadAdjacentSims(cloudTilePos);
                    wreaderpreload.EnsureCloudTileCacheIsFresh(tileOffset);
                }

                // Noise generation (from the precipitation and temperature subsystems) is expensive, lets do it less often.
                // Since the clouds have smooth transition anyways, it should not be noticable at all
                if (reloadRainNoiseValues || !cloudTile.rainValuesSet)
                {
                    wreaderpreload.LoadLerp(cloudTilePos);
                    cloudTile.lerpRainCloudOverlay = wreaderpreload.lerpRainCloudOverlay;
                    cloudTile.lerpRainOverlay = wreaderpreload.lerpRainOverlay;
                    cloudTile.rainValuesSet = true;
                } else
                {
                    wreaderpreload.LoadLerp(cloudTilePos, true, cloudTile.lerpRainCloudOverlay, cloudTile.lerpRainOverlay);
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

        #endregion

        public void Dispose()
        {

            FreeGlResources();
            capi.Render.DeleteMesh(quad);

        }

        
    }

}