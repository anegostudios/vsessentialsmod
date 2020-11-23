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
        public CloudRenderer cloudRenderer;


        public ClimateCondition clientClimateCond;
        public bool playerChunkLoaded;

        float quarterSecAccum = 0;
        BlockPos plrPos = new BlockPos();
        Vec3d plrPosd = new Vec3d();
        float smoothedLightLevel;


        public bool haveLevelFinalize;



        public WeatherSimulationSound simSounds;
        public WeatherSimulationParticles simParticles;
        public WeatherSimulationLightning simLightning;
        public AuroraRenderer auroraRenderer;


        private long blendedLastCheckedMS = -1L;
        private WeatherDataSnapshot blendedWeatherDataCached = null;
        public WeatherDataSnapshot BlendedWeatherData {
            get
            {
                long ms = capi.ElapsedMilliseconds / 10L;  //Can't possibly need to update client-side weather more than 100 times per second
                if (ms != blendedLastCheckedMS)
                {
                    blendedLastCheckedMS = ms;
                    blendedWeatherDataCached = WeatherDataAtPlayer.BlendedWeatherData;
                }
                return blendedWeatherDataCached;
            }
        }

        public WeatherDataReaderPreLoad WeatherDataAtPlayer;


        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Client;
        }



        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;
            base.Initialize();

            clientChannel =
                 capi.Network.GetChannel("weather")
                .SetMessageHandler<WeatherState>(OnWeatherUpdatePacket)
                .SetMessageHandler<WeatherConfigPacket>(OnWeatherConfigUpdatePacket)
                .SetMessageHandler<WeatherPatternAssetsPacket>(OnAssetsPacket)
             ;

            capi.Event.RegisterGameTickListener(OnClientGameTick, 50);

            capi.Event.LevelFinalize += LevelFinalizeInit;

            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "weatherSystem");
            capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "weatherSystem");
            capi.Event.LeaveWorld += () => cloudRenderer?.Dispose();
            capi.Event.OnGetClimate += Event_OnGetClimate;

            simSounds = new WeatherSimulationSound(capi as ICoreClientAPI, this);
            simParticles = new WeatherSimulationParticles(capi as ICoreClientAPI, this);
            simLightning = new WeatherSimulationLightning(capi as ICoreClientAPI, this);
            auroraRenderer = new AuroraRenderer(capi as ICoreClientAPI, this);
        }

        private void OnAssetsPacket(WeatherPatternAssetsPacket networkMessage)
        {
            WeatherPatternAssets p = JsonUtil.FromString<WeatherPatternAssets>(networkMessage.Data);
            this.GeneralConfig = p.GeneralConfig;
            this.GeneralConfig.Init(api.World);

            this.WeatherConfigs = p.WeatherConfigs;
            this.WindConfigs = p.WindConfigs;
            this.WeatherEventConfigs = p.WeatherEventConfigs;

            foreach (var val in weatherSimByMapRegion)
            {
                val.Value.ReloadPatterns(api.World.Seed);
            }
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            simLightning.OnRenderFrame(dt, stage);

            if (stage == EnumRenderStage.Before)
            {
                EntityPlayer eplr = capi.World.Player.Entity;
                plrPos.Set((int)eplr.Pos.X, (int)eplr.Pos.Y, (int)eplr.Pos.Z);
                plrPosd.Set(eplr.Pos.X, eplr.Pos.Y, eplr.Pos.Z);

                WeatherDataAtPlayer.LoadAdjacentSimsAndLerpValues(plrPosd);
                WeatherDataAtPlayer.UpdateAdjacentAndBlendWeatherData();

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

                WeatherDataAtPlayer.BlendedWeatherData.Ambient.FlatFogDensity.Weight *= fogMultiplier;
                WeatherDataAtPlayer.BlendedWeatherData.Ambient.FogDensity.Weight *= fogMultiplier;

                
                dt = Math.Min(0.5f, dt);

                // Windspeed should be stored inside ClimateConditions and not be a global constant
                double windspeed = WeatherDataAtPlayer.GetWindSpeed(plrPosd.Y);
                GlobalConstants.CurrentWindSpeedClient.X += ((float)windspeed - GlobalConstants.CurrentWindSpeedClient.X) * dt;

                capi.Ambient.CurrentModifiers["weather"] = WeatherDataAtPlayer.BlendedWeatherData.Ambient;
            }
        }




        private void OnClientGameTick(float dt)
        {
            quarterSecAccum += dt;
            if (quarterSecAccum > 0.25f)
            {
                clientClimateCond = capi.World.BlockAccessor.GetClimateAt(plrPos, EnumGetClimateMode.NowValues);
                quarterSecAccum = 0;

                playerChunkLoaded |= capi.World.BlockAccessor.GetChunkAtBlockPos(plrPos) != null; // To avoid rain for one second right after joining
            }

            simLightning.ClientTick(dt);

            for (int i = 0; i < 4; i++)
            {
                WeatherSimulationRegion sim = WeatherDataAtPlayer.AdjacentSims[i];
                if (sim == dummySim) continue;
                sim.TickEvery25ms(dt);
            }

            simSounds.Update(dt);
            rainOverlaySnap.climateCond = clientClimateCond;
            rainOverlaySnap.SetAmbient(rainOverlayPattern, capi == null ? 0 : capi.Ambient.Base.FogDensity.Value);
        }


        Queue<WeatherState> weatherUpdateQueue = new Queue<WeatherState>();

        private void OnWeatherConfigUpdatePacket(WeatherConfigPacket packet)
        {
            OverridePrecipitation = packet.OverridePrecipitation;
            RainCloudDaysOffset = packet.RainCloudDaysOffset;
        }

        private void OnWeatherUpdatePacket(WeatherState msg)
        {
            weatherUpdateQueue.Enqueue(msg);
        }

        public void ProcessWeatherUpdates()
        {
            foreach (var packet in weatherUpdateQueue)
            {
                ProcessWeatherUpdate(packet);
            }
            weatherUpdateQueue.Clear();
        }

        void ProcessWeatherUpdate(WeatherState msg)
        { 
            WeatherSimulationRegion weatherSim = getOrCreateWeatherSimForRegion(msg.RegionX, msg.RegionZ);

            if (weatherSim == null)
            {
                Console.WriteLine("weatherSim for region {0}/{1} is null. No idea what to do here", msg.RegionX, msg.RegionZ);
                return;
            }

            if (msg.updateInstant)
            {
//                ReloadConfigs();
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

            //bool windChanged = weatherSim.CurWindPattern.State.Index != msg.WindPattern.Index;
            weatherSim.CurWindPattern = weatherSim.WindPatterns[msg.WindPattern.Index];
            weatherSim.CurWindPattern.State = msg.WindPattern;

            weatherSim.CurWeatherEvent = weatherSim.WeatherEvents[msg.WeatherEvent.Index];
            weatherSim.CurWeatherEvent.State = msg.WeatherEvent;

            if (msg.updateInstant)
            {
                weatherSim.NewWePattern.OnBeginUse();
                cloudRenderer.instantTileBlend = true;
            }


            //api.World.Logger.Notification("Weather pattern update @{0}/{1}", weatherSim.regionX, weatherSim.regionZ);

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
            InitDummySim();

            WeatherDataAtPlayer = getWeatherDataReaderPreLoad();
            WeatherDataSlowAccess = getWeatherDataReader();

            simSounds.Initialize();
            simParticles.Initialize();
            cloudRenderer = new CloudRenderer(capi, this);

            smoothedLightLevel = capi.World.BlockAccessor.GetLightLevel(capi.World.Player.Entity.Pos.AsBlockPos, EnumLightLevelType.OnlySunLight);
                        

            capi.Ambient.CurrentModifiers.InsertBefore("serverambient", "weather", WeatherDataAtPlayer.BlendedWeatherData.Ambient);
            haveLevelFinalize = true;

            // Pre init the clouds.             
            capi.Ambient.UpdateAmbient(0.1f);
            CloudRenderer renderer = this.cloudRenderer as CloudRenderer;

            renderer.blendedCloudDensity = capi.Ambient.BlendedCloudDensity;
            renderer.blendedGlobalCloudBrightness = capi.Ambient.BlendedCloudBrightness;
            renderer.CloudTick(0.1f);
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

        /// <summary>
        /// Get the current precipitation as seen by the client at pos
        /// </summary>
        /// <param name="rainOnly">If true, returns 0 if it is currently snowing</param>
        /// <returns></returns>
        public double GetActualRainLevel(BlockPos pos, bool rainOnly = false)
        {
            ClimateCondition conds = clientClimateCond;
            if (conds == null || !playerChunkLoaded) return 0.0;
            float precIntensity = conds.Rainfall;

            if (rainOnly)
            {
                // TODO:  Testing whether it is snowing or not on the client is kinda slow - though the WeatherSimulationParticles must already know!

                WeatherDataSnapshot weatherData = BlendedWeatherData;
                EnumPrecipitationType precType = weatherData.BlendedPrecType;
                if (precType == EnumPrecipitationType.Auto)
                {
                    precType = conds.Temperature < weatherData.snowThresholdTemp ? EnumPrecipitationType.Snow : EnumPrecipitationType.Rain;
                }
                if (precType == EnumPrecipitationType.Snow) return 0d;
            }

            return precIntensity;
        }
    }
}
