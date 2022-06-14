using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class WeatherSystemServer : WeatherSystemBase
    {
        public ICoreServerAPI sapi;
        public IServerNetworkChannel serverChannel;
        
        internal WeatherSimulationSnowAccum snowSimSnowAccu;

        protected WeatherPatternAssetsPacket packetForClient;

        float? overrideprecip;
        public override float? OverridePrecipitation {
            get
            {
                return overrideprecip;
            }
            set
            {
                overrideprecip = value;
                sapi.WorldManager.SaveGame.StoreData("overrideprecipitation", overrideprecip == null ? null : SerializerUtil.Serialize((float)overrideprecip));
            }
        }

        double daysoffset;
        public override double RainCloudDaysOffset
        {
            get
            {
                return daysoffset;
            }
            set
            {
                daysoffset = value;
                sapi.WorldManager.SaveGame.StoreData("precipitationdaysoffset", SerializerUtil.Serialize(daysoffset));
            }
        }




        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            this.sapi = api;
            LoadConfigs();

            serverChannel = api.Network.GetChannel("weather");

            sapi.Event.RegisterGameTickListener(OnServerGameTick, 200);
            sapi.Event.GameWorldSave += OnSaveGameSaving;
            sapi.Event.SaveGameLoaded += Event_SaveGameLoaded;
            sapi.Event.OnGetClimate += Event_OnGetClimate;
            sapi.Event.PlayerJoin += Event_PlayerJoin;


            snowSimSnowAccu = new WeatherSimulationSnowAccum(sapi, this);
        }


        public void ReloadConfigs()
        {
            api.Assets.Reload(new AssetLocation("config/"));
            LoadConfigs(true);

            foreach (var val in sapi.World.AllOnlinePlayers)
            {
                serverChannel.SendPacket(packetForClient, val as IServerPlayer);
            }
        }

        public void LoadConfigs(bool isReload = false)
        {
            // Thread safe assignment
            var nowconfig = api.Assets.Get<WeatherSystemConfig>(new AssetLocation("config/weather.json"));
            if (isReload) nowconfig.Init(api.World);

            GeneralConfig = nowconfig;

            var dictWeatherPatterns = api.Assets.GetMany<WeatherPatternConfig[]>(api.World.Logger, "config/weatherpatterns/");
            var orderedWeatherPatterns = dictWeatherPatterns.OrderBy(val => val.Key.ToString()).Select(val => val.Value).ToArray();

            WeatherConfigs = new WeatherPatternConfig[0];
            foreach (var val in orderedWeatherPatterns)
            {
                WeatherConfigs = WeatherConfigs.Append(val);
            }

            var dictWindPatterns = api.Assets.GetMany<WindPatternConfig[]>(api.World.Logger, "config/windpatterns/");
            var orderedWindPatterns = dictWindPatterns.OrderBy(val => val.Key.ToString()).Select(val => val.Value).ToArray();

            WindConfigs = new WindPatternConfig[0];
            foreach (var val in orderedWindPatterns)
            {
                WindConfigs = WindConfigs.Append(val);
            }

            var dictweatherEventConfigs = api.Assets.GetMany<WeatherEventConfig[]>(api.World.Logger, "config/weatherevents/");
            var orderedweatherEventConfigs = dictweatherEventConfigs.OrderBy(val => val.Key.ToString()).Select(val => val.Value).ToArray();

            WeatherEventConfigs = new WeatherEventConfig[0];
            foreach (var val in orderedweatherEventConfigs)
            {
                WeatherEventConfigs = WeatherEventConfigs.Append(val);
            }

            api.World.Logger.Notification("Reloaded {0} weather patterns, {1} wind patterns and {2} weather events", WeatherConfigs.Length, WindConfigs.Length, WeatherEventConfigs.Length);

            WeatherPatternAssets p = new WeatherPatternAssets()
            {
                GeneralConfig = GeneralConfig,
                WeatherConfigs = WeatherConfigs,
                WindConfigs = WindConfigs,
                WeatherEventConfigs = WeatherEventConfigs
            };

            packetForClient = new WeatherPatternAssetsPacket() { Data = JsonUtil.ToString(p) };
        }

        private void Event_PlayerJoin(IServerPlayer byPlayer)
        {
            serverChannel.SendPacket(packetForClient, byPlayer);
            serverChannel.SendPacket(new WeatherCloudYposPacket() { CloudYRel = CloudLevelRel }, byPlayer);
            sendConfigUpdate(byPlayer);
        }

        public void sendConfigUpdate(IServerPlayer byPlayer)
        {
            serverChannel.SendPacket(new WeatherConfigPacket()
            {
                OverridePrecipitation = OverridePrecipitation,
                RainCloudDaysOffset = RainCloudDaysOffset
            }, byPlayer);
        }

        public void broadCastConfigUpdate()
        {
            serverChannel.BroadcastPacket(new WeatherConfigPacket()
            {
                OverridePrecipitation = OverridePrecipitation,
                RainCloudDaysOffset = RainCloudDaysOffset
            });
        }

        private void Event_SaveGameLoaded()
        {
            byte[] data = sapi.WorldManager.SaveGame.GetData("overrideprecipitation");
            if (data != null)
            {
                overrideprecip = SerializerUtil.Deserialize<float>(data);
            }
            data = sapi.WorldManager.SaveGame.GetData("precipitationdaysoffset");
            if (data != null) {
                daysoffset = SerializerUtil.Deserialize<double>(data);
            }

            base.Initialize();
            base.InitDummySim();
            WeatherDataSlowAccess = getWeatherDataReader();

            GeneralConfig.Init(api.World);

            if (sapi.WorldManager.SaveGame.WorldConfiguration != null)
            {
                CloudLevelRel = sapi.WorldManager.SaveGame.WorldConfiguration.GetString("cloudypos", "1").ToFloat(1);
            }
        }

        public void SendWeatherStateUpdate(WeatherState state)
        {
            int regionSize = sapi.WorldManager.RegionSize;

            foreach (var plr in sapi.World.AllOnlinePlayers)
            {
                int plrRegionX = (int)plr.Entity.ServerPos.X / regionSize;
                int plrRegionZ = (int)plr.Entity.ServerPos.Z / regionSize;

                if (Math.Abs(state.RegionX - plrRegionX) <= 1 && Math.Abs(state.RegionZ - plrRegionZ) <= 1)
                {
                    serverChannel.SendPacket(state, plr as IServerPlayer);
                }
            }

            // Instanty store the change, so that players that connect shortly after also get the update
            IMapRegion mapregion = sapi.WorldManager.GetMapRegion(state.RegionX, state.RegionZ);
            if (mapregion != null)
            {
                mapregion.SetModdata("weather", SerializerUtil.Serialize(state));
            }
        }



        private void OnServerGameTick(float dt)
        {
            foreach (var val in sapi.WorldManager.AllLoadedMapRegions)
            {
                WeatherSimulationRegion weatherSim = getOrCreateWeatherSimForRegion(val.Key, val.Value);
                weatherSim.TickEvery25ms(dt);
                weatherSim.UpdateWeatherData();
            }

            rainOverlaySnap.SetAmbient(rainOverlayPattern, 0);
        }


        private void OnSaveGameSaving()
        {
            HashSet<long> toRemove = new HashSet<long>();

            foreach (var val in weatherSimByMapRegion)
            {
                IMapRegion mapregion = sapi.WorldManager.GetMapRegion(val.Key);
                if (mapregion != null)
                {
                    mapregion.SetModdata("weather", val.Value.ToBytes());
                } else
                {
                    toRemove.Add(val.Key);
                }
            }

            foreach (var key in toRemove)
            {
                weatherSimByMapRegion.Remove(key);
            }   
        }

        public override void SpawnLightningFlash(Vec3d pos)
        {
            TriggerOnLightningImpactStart(ref pos, out var handling);

            if (handling == EnumHandling.PassThrough)
            {
                var pkt = new LightningFlashPacket()
                {
                    Pos = pos,
                    Seed = api.World.Rand.Next()
                };
                serverChannel.BroadcastPacket(pkt);

                var lflash = new LightningFlash(this, api, pkt.Seed, pkt.Pos);

                simLightning.lightningFlashes.Add(lflash);
            }
        }


    }



}
