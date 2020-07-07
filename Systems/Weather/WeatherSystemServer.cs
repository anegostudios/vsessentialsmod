using System;
using System.Collections.Generic;
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


        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            this.sapi = api;

            serverChannel = api.Network.GetChannel("weather");

            sapi.Event.RegisterGameTickListener(OnServerGameTick, 200);
            sapi.Event.GameWorldSave += OnSaveGameSaving;
            sapi.Event.SaveGameLoaded += Event_SaveGameLoaded;
            sapi.Event.OnGetClimate += Event_OnGetClimate;
            
           
            snowSimSnowAccu = new WeatherSimulationSnowAccum(sapi, this);
        }


        private void Event_SaveGameLoaded()
        {
            base.Initialize();
            base.InitDummySim();
            WeatherDataSlowAccess = getWeatherDataReader();

            GeneralConfig.Init(api.World);
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
                mapregion.ModData["weather"] = SerializerUtil.Serialize(state);
            }
        }


        object updateSnowlayerQueueLock = new object();
        Dictionary<BlockPos, Block> updateSnowLayerQueue = new Dictionary<BlockPos, Block>();


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
                    mapregion.ModData["weather"] = val.Value.ToBytes();
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




    }



}
