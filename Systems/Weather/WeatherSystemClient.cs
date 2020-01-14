using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class WeatherSystemClient : WeatherSystemBase, IRenderer
    {
        public ICoreClientAPI capi;
        public IClientNetworkChannel clientChannel;
        public CloudRendererDummy cloudRenderer;


        public ClimateCondition clientClimateCond;
        float quarterSecAccum = 0;
        BlockPos plrPos = new BlockPos();
        Vec3d plrPosd = new Vec3d();
        float smoothedLightLevel;


        public bool haveLevelFinalize;

        

        protected WeatherSimulationSound simSounds;
        protected WeatherSimulationParticles simParticles;
        protected WeatherSimulationLightning simLightning;
        protected AuroraRenderer auroraRenderer;

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Client;
        }


        public override int CloudTileLength
        {
            get { return cloudRenderer.CloudTileLength; }
        }

        public override int CloudTileX
        {
            get { return cloudRenderer.tilePosX - cloudRenderer.tileOffsetX; }
        }
        public override int CloudTileZ
        {
            get { return cloudRenderer.tilePosZ - cloudRenderer.tileOffsetZ; }
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;
            clientChannel =
                 capi.Network.RegisterChannel("weather")
                .RegisterMessageType(typeof(WeatherState))
                .SetMessageHandler<WeatherState>(OnWeatherUpdate)
             ;

            capi.Event.RegisterGameTickListener(OnClientGameTick, 50);
            capi.Event.LevelFinalize += LevelFinalizeInit;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "weatherSystem");
            capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "weatherSystem");
            capi.Event.LeaveWorld += () => (cloudRenderer as CloudRenderer)?.Dispose();

            blendedWeatherData.Ambient = new AmbientModifier().EnsurePopulated();

            simSounds = new WeatherSimulationSound(capi as ICoreClientAPI, this);
            simParticles = new WeatherSimulationParticles(capi as ICoreClientAPI, this);
            simLightning = new WeatherSimulationLightning(capi as ICoreClientAPI, this);
            auroraRenderer = new AuroraRenderer(capi as ICoreClientAPI, this);
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            simLightning.OnRenderFrame(dt, stage);

            if (stage == EnumRenderStage.Before)
            {
                EntityPlayer eplr = capi.World.Player.Entity;
                plrPos.Set((int)eplr.Pos.X, (int)eplr.Pos.Y, (int)eplr.Pos.Z);
                plrPosd.Set(eplr.Pos.X, eplr.Pos.Y, eplr.Pos.Z);

                LoadAdjacentSimsAndLerpValues(plrPosd);
                updateAdjacentAndBlendWeatherData();

                int lightlevel = Math.Max(
                    capi.World.BlockAccessor.GetLightLevel(plrPos, EnumLightLevelType.OnlySunLight),
                    capi.World.BlockAccessor.GetLightLevel(plrPos.Up(), EnumLightLevelType.OnlySunLight)
                );
                smoothedLightLevel += (lightlevel - smoothedLightLevel) * dt * 4;

                // light level > 17 = 100% fog
                // light level <= 2 = 0% fog
                float fogMultiplier = GameMath.Clamp(smoothedLightLevel / 20f, 0f, 1);
                float fac = (float)GameMath.Clamp(capi.World.Player.Entity.Pos.Y / capi.World.SeaLevel, 0, 1);
                fac *= fac;
                fogMultiplier *= fac;

                blendedWeatherData.Ambient.FlatFogDensity.Weight *= fogMultiplier;
                blendedWeatherData.Ambient.FogDensity.Weight *= fogMultiplier;

                //Console.WriteLine("{0} / {1}", blendedWeatherData.Ambient.FlatFogDensity.Value, blendedWeatherData.Ambient.FlatFogDensity.Weight);

                dt = Math.Min(0.5f, dt);
                GlobalConstants.CurrentWindSpeedClient.X += ((float)GetWindSpeed(plrPosd) - GlobalConstants.CurrentWindSpeedClient.X) * dt;
                GlobalConstants.CurrentRainFallClient = GetRainFall(plrPosd);
            }
        }



        
        private void OnClientGameTick(float dt)
        {
            quarterSecAccum += dt;
            if (quarterSecAccum > 0.25f)
            {
                clientClimateCond = capi.World.BlockAccessor.GetClimateAt(plrPos);
                quarterSecAccum = 0;
            }

            simLightning.ClientTick(dt);

            for (int i = 0; i < 4; i++)
            {
                WeatherSimulation sim = adjacentSims[i];
                if (sim == dummySim) continue;
                sim.TickEvery25ms(dt);
            }

            simSounds.Update(dt);
        }



        Queue<WeatherState> earlyWeatherUpdates = new Queue<WeatherState>();
        private void OnWeatherUpdate(WeatherState msg)
        {
            // Ok thing. The server will send weather updates before our weather simulation is initialized.
            // We initialize our weather simulators only during LevelFinalize
            // So, let's just queue those up in that case, and process once we done initializing

            if (!haveLevelFinalize)
            {
                earlyWeatherUpdates.Enqueue(msg);
                return;
            }

            WeatherSimulation weatherSim = getOrCreateWeatherSimForRegion(msg.RegionX, msg.RegionZ);

            if (weatherSim == null)
            {
                Console.WriteLine("weatherSim for region {0}/{1} is null. No idea what to do here", msg.RegionX, msg.RegionZ);
                return;
            }

            if (msg.updateInstant)
            {
                ReloadConfigs();
                weatherSim.ReloadPatterns(api.World.Seed);

                for (int i = 0; i < weatherSim.WeatherPatterns.Length; i++)
                {
                    weatherSim.WeatherPatterns[i].Initialize(i, api.World.Seed);
                }
            }

            weatherSim.NewWePattern = weatherSim.WeatherPatterns[msg.NewPattern.Index];
            weatherSim.NewWePattern.State = msg.NewPattern;

            weatherSim.OldWePattern = weatherSim.WeatherPatterns[msg.OldPattern.Index];
            weatherSim.OldWePattern.State = msg.OldPattern;

            weatherSim.TransitionDelay = msg.TransitionDelay;
            weatherSim.Transitioning = msg.Transitioning;
            weatherSim.Weight = msg.Weight;

            bool windChanged = weatherSim.CurWindPattern.State.Index != msg.WindPattern.Index;
            weatherSim.CurWindPattern = weatherSim.WindPatterns[msg.WindPattern.Index];
            weatherSim.CurWindPattern.State = msg.WindPattern;

            if (msg.updateInstant)
            {
                weatherSim.NewWePattern.OnBeginUse();
            }


            api.World.Logger.Notification("Weather pattern update @{0}/{1}", weatherSim.regionX, weatherSim.regionZ);

            if (msg.Transitioning)
            {
                weatherSim.Weight = 0;
            }

            if (msg.updateInstant)
            {
                weatherSim.TickEvery25ms(0.025f);
                cloudRenderer.UpdateCloudTiles(short.MaxValue);
            }
        }




        private void LevelFinalizeInit()
        {
            base.Initialize();

            simSounds.Initialize();
            simParticles.Initialize();
            cloudRenderer = new CloudRenderer(capi, this);

            smoothedLightLevel = capi.World.BlockAccessor.GetLightLevel(capi.World.Player.Entity.Pos.AsBlockPos, EnumLightLevelType.OnlySunLight);
            dummySim = new WeatherSimulation(this, 0, 0);
            adjacentSims[0] = dummySim;
            adjacentSims[1] = dummySim;
            adjacentSims[2] = dummySim;
            adjacentSims[3] = dummySim;
            dummySim.Initialize();

            capi.Ambient.CurrentModifiers.InsertBefore("serverambient", "weather", blendedWeatherData.Ambient);
            haveLevelFinalize = true;

            foreach (var packet in earlyWeatherUpdates)
            {
                OnWeatherUpdate(packet);
            }

            // Pre init the clouds.             
            capi.Ambient.UpdateAmbient(0.1f);
            CloudRenderer renderer = this.cloudRenderer as CloudRenderer;

            renderer.blendedCloudDensity = capi.Ambient.BlendedCloudDensity;
            renderer.blendedGlobalCloudBrightness = capi.Ambient.BlendedCloudBrightness;
            renderer.UpdateWindAndClouds(0.1f);
        }



        public double GetBlendedCloudThicknessAt(int dx, int dz)
        {
            return GameMath.BiLerp(
                adjacentSims[0].GetBlendedCloudThicknessAt(dx, dz),
                adjacentSims[1].GetBlendedCloudThicknessAt(dx, dz),
                adjacentSims[2].GetBlendedCloudThicknessAt(dx, dz),
                adjacentSims[3].GetBlendedCloudThicknessAt(dx, dz),
                lerpLeftRight, lerpTopBot
            );
        }



        public double GetBlendedCloudThicknessAt(int dx, int dz, WeatherSimulation[] adjacentSims, double lerpLeftRight, double lerpTopBot)
        {
            return GameMath.BiLerp(
                adjacentSims[0].GetBlendedCloudThicknessAt(dx, dz),
                adjacentSims[1].GetBlendedCloudThicknessAt(dx, dz),
                adjacentSims[2].GetBlendedCloudThicknessAt(dx, dz),
                adjacentSims[3].GetBlendedCloudThicknessAt(dx, dz),
                lerpLeftRight, lerpTopBot
            );
        }


        public double GetBlendedCloudOpaqueness()
        {
            return GameMath.BiLerp(
                adjacentSims[0].GetBlendedCloudOpaqueness(),
                adjacentSims[1].GetBlendedCloudOpaqueness(),
                adjacentSims[2].GetBlendedCloudOpaqueness(),
                adjacentSims[3].GetBlendedCloudOpaqueness(),
                lerpLeftRight, lerpTopBot
            );
        }

        public double GetBlendedThinCloudModeness()
        {
            return GameMath.BiLerp(
                adjacentSims[0].GetBlendedThinCloudModeness(),
                adjacentSims[1].GetBlendedThinCloudModeness(),
                adjacentSims[2].GetBlendedThinCloudModeness(),
                adjacentSims[3].GetBlendedThinCloudModeness(),
                lerpLeftRight, lerpTopBot
            );
        }

        public double GetBlendedUndulatingCloudModeness()
        {
            return GameMath.BiLerp(
                adjacentSims[0].GetBlendedUndulatingCloudModeness(),
                adjacentSims[1].GetBlendedUndulatingCloudModeness(),
                adjacentSims[2].GetBlendedUndulatingCloudModeness(),
                adjacentSims[3].GetBlendedUndulatingCloudModeness(),
                lerpLeftRight, lerpTopBot
            );
        }

        public void EnsureNoiseCacheIsFresh()
        {
            adjacentSims[0].EnsureNoiseCacheIsFresh();
            adjacentSims[1].EnsureNoiseCacheIsFresh();
            adjacentSims[2].EnsureNoiseCacheIsFresh();
            adjacentSims[3].EnsureNoiseCacheIsFresh();
        }


        public override void Dispose()
        {
            base.Dispose();

            simSounds?.Dispose();
        }


        public double RenderOrder => -0.1;
        public int RenderRange => 999;

        public double CloudsYPosition
        {
            get { return capi.Ambient.BlendedCloudYPos; }
        }
    }
}
