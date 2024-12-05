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
        /// <summary>
        /// Set by the WeatherSimulation System in the survival mod. Value is client side at the players position. Has values in the 0..1 range. 1 being it just rained. Used by leaf blocks to spawn rain drop particles. 0 meaning it hasn't rained in 4+ hours.
        /// </summary>
        public static float CurrentEnvironmentWetness4h;

        public ICoreClientAPI capi;
        public IClientNetworkChannel clientChannel;
        public CloudRenderer cloudRenderer;


        public ClimateCondition clientClimateCond;
        public bool playerChunkLoaded;

        float quarterSecAccum = 0;
        BlockPos plrPos = new BlockPos();
        Vec3d plrPosd = new Vec3d();


        public bool haveLevelFinalize;



        public WeatherSimulationSound simSounds;
        public WeatherSimulationParticles simParticles;
        
        public AuroraRenderer auroraRenderer;

        public event Action<WeatherDataSnapshot> OnGetBlendedWeatherData;

        private long blendedLastCheckedMSDiv60 = -1L;
        private WeatherDataSnapshot blendedWeatherDataCached = null;
        public WeatherDataSnapshot BlendedWeatherData {
            get
            {
                long msd60 = capi.ElapsedMilliseconds / 60;
                if (msd60 != blendedLastCheckedMSDiv60)
                {
                    // Refresh at 16 FPS rate, thats plenty
                    blendedLastCheckedMSDiv60 = msd60;
                    blendedWeatherDataCached = WeatherDataAtPlayer.BlendedWeatherData;

                    OnGetBlendedWeatherData(blendedWeatherDataCached);
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
                .SetMessageHandler<LightningFlashPacket>(OnLightningFlashPacket)
                .SetMessageHandler<WeatherCloudYposPacket>(OnCloudLevelRelPacket)
             ;

            capi.Event.RegisterGameTickListener(OnClientGameTick, 50);

            capi.Event.LevelFinalize += LevelFinalizeInit;

            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "weatherSystem");
            capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "weatherSystem");
            capi.Event.LeaveWorld += () => cloudRenderer?.Dispose();
            capi.Event.OnGetClimate += Event_OnGetClimate;

            simSounds = new WeatherSimulationSound(capi, this);
            simParticles = new WeatherSimulationParticles(capi, this);
            auroraRenderer = new AuroraRenderer(capi, this);

            capi.Logger.Notification("Initialised WeatherSystemClient. simLightning is " + (simLightning == null ? "null." : "loaded."));
        }

        private void OnCloudLevelRelPacket(WeatherCloudYposPacket msg)
        {
            this.CloudLevelRel = msg.CloudYRel;
        }

        private void OnAssetsPacket(WeatherPatternAssetsPacket msg)
        {
            WeatherPatternAssets p = JsonUtil.FromString<WeatherPatternAssets>(msg.Data);
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

        Vec3f windSpeedSmoothed = new Vec3f();
        double windRandCounter;

        Vec3f surfaceWindSpeedSmoothed = new Vec3f();
        double surfaceWindRandCounter;

        float wetnessScanAccum2s = 0;

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            try
            {
                simLightning.OnRenderFrame(dt, stage);
            }
            catch (Exception e)
            {
                if (simLightning == null) api.Logger.Error("simLightning was null! Please report this as a bug");
                api.Logger.Error(e);
            }

            if (stage == EnumRenderStage.Before)
            {
                EntityPlayer eplr = capi.World.Player.Entity;
                plrPos.Set((int)eplr.Pos.X, (int)eplr.Pos.Y, (int)eplr.Pos.Z);
                plrPosd.Set(eplr.Pos.X, eplr.Pos.Y, eplr.Pos.Z);

                WeatherDataAtPlayer.LoadAdjacentSimsAndLerpValues(plrPosd, dt);
                WeatherDataAtPlayer.UpdateAdjacentAndBlendWeatherData();


                dt = Math.Min(0.5f, dt);

                // Windspeed should be stored inside ClimateConditions and not be a global constant
                var windspeed = capi.World.BlockAccessor.GetWindSpeedAt(plrPosd); // WeatherDataAtPlayer.GetWindSpeed(plrPosd.Y);
                windSpeedSmoothed.X += ((float)windspeed.X - windSpeedSmoothed.X) * dt;
                windSpeedSmoothed.Y += ((float)windspeed.Y - windSpeedSmoothed.Y) * dt;
                windSpeedSmoothed.Z += ((float)windspeed.Z - windSpeedSmoothed.Z) * dt;

                windRandCounter = (windRandCounter + dt) % (2000 * Math.PI);
                double rndx = (2 * Math.Sin(windRandCounter / 8) + Math.Sin(windRandCounter / 2) + Math.Sin(0.5 + 2 * windRandCounter)) / 10.0;
                GlobalConstants.CurrentWindSpeedClient.Set(windSpeedSmoothed.X, windSpeedSmoothed.Y, windSpeedSmoothed.Z + (float)rndx * windSpeedSmoothed.X);

                var rainy = capi.World.BlockAccessor.GetRainMapHeightAt(plrPos.X, plrPos.Z);
                plrPosd.Y = rainy;
                

                var surfacewindspeed = capi.World.BlockAccessor.GetWindSpeedAt(plrPosd); // WeatherDataAtPlayer.GetWindSpeed(plrPos);
                surfaceWindSpeedSmoothed.X += ((float)surfacewindspeed.X - surfaceWindSpeedSmoothed.X) * dt;
                surfaceWindSpeedSmoothed.Y += ((float)surfacewindspeed.Y - surfaceWindSpeedSmoothed.Y) * dt;
                surfaceWindSpeedSmoothed.Z += ((float)surfacewindspeed.Z - surfaceWindSpeedSmoothed.Z) * dt;

                surfaceWindRandCounter = (surfaceWindRandCounter + dt) % (2000 * Math.PI);
                rndx = (2 * Math.Sin(surfaceWindRandCounter / 8) + Math.Sin(surfaceWindRandCounter / 2) + Math.Sin(0.5 + 2 * surfaceWindRandCounter)) / 10.0;
                GlobalConstants.CurrentSurfaceWindSpeedClient.Set(surfaceWindSpeedSmoothed.X, surfaceWindSpeedSmoothed.Y, surfaceWindSpeedSmoothed.Z + (float)rndx * surfaceWindSpeedSmoothed.X);


                capi.Ambient.CurrentModifiers["weather"] = WeatherDataAtPlayer.BlendedWeatherData.Ambient;

                wetnessScanAccum2s += dt;
                if (wetnessScanAccum2s > 2)
                {
                    wetnessScanAccum2s = 0;
                    double totalDays = capi.World.Calendar.TotalDays;
                    float rainSum = 0;

                    // Iterate over the last 3 hours, every 15 minutes
                    for (int i = 0; i < 12; i++)
                    {
                        float weight = 1 - i / 20f; // Weight old values less
                        rainSum += weight * capi.World.BlockAccessor.GetClimateAt(plrPos, EnumGetClimateMode.ForSuppliedDateValues, totalDays - i / 24.0 / 4).Rainfall;
                    }

                    CurrentEnvironmentWetness4h = GameMath.Clamp(rainSum, 0, 1);
                }
            }
        }




        private void OnClientGameTick(float dt)
        {
            quarterSecAccum += dt;
            if (quarterSecAccum > 0.25f || clientClimateCond == null)
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
                weatherSim.ReloadPatterns(api.World.Seed);

                for (int i = 0; i < weatherSim.WeatherPatterns.Length; i++)
                {
                    weatherSim.WeatherPatterns[i].Initialize(i, api.World.Seed);
                }
            }

            weatherSim.NewWePattern = weatherSim.WeatherPatterns[Math.Min(weatherSim.WeatherPatterns.Length - 1, msg.NewPattern.Index)];
            weatherSim.NewWePattern.State = msg.NewPattern;

            weatherSim.OldWePattern = weatherSim.WeatherPatterns[Math.Min(weatherSim.WeatherPatterns.Length - 1 , msg.OldPattern.Index)];
            weatherSim.OldWePattern.State = msg.OldPattern;

            weatherSim.TransitionDelay = msg.TransitionDelay;
            weatherSim.Transitioning = msg.Transitioning;
            weatherSim.Weight = msg.Weight;

            //bool windChanged = weatherSim.CurWindPattern.State.Index != msg.WindPattern.Index;
            weatherSim.CurWindPattern = weatherSim.WindPatterns[Math.Min(weatherSim.WindPatterns.Length - 1, msg.WindPattern.Index)];
            weatherSim.CurWindPattern.State = msg.WindPattern;

            weatherSim.CurWeatherEvent = weatherSim.WeatherEvents[Math.Min(weatherSim.WeatherEvents.Length - 1, msg.WeatherEvent.Index)];
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

            capi.Ambient.CurrentModifiers.InsertBefore("serverambient", "weather", WeatherDataAtPlayer.BlendedWeatherData.Ambient);
            haveLevelFinalize = true;

            // Pre init the clouds.             
            capi.Ambient.UpdateAmbient(0.1f);

            cloudRenderer.CloudTick(0.1f);

            capi.Logger.VerboseDebug("Done init WeatherSystemClient");
        }

        public override void Dispose()
        {
            base.Dispose();

            simSounds?.Dispose();
        }


        public double RenderOrder => -0.1;
        public int RenderRange => 999;


        private void OnLightningFlashPacket(LightningFlashPacket msg)
        {
            if (capi.World.Player == null) return; // not fully connected yet

            simLightning.genLightningFlash(msg.Pos, msg.Seed);
        }

        public override void SpawnLightningFlash(Vec3d pos)
        {
            simLightning.genLightningFlash(pos);
        }
    }
}
