using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WeatherState
    {
        public int NewPatternIndex;
        public int OldPatternIndex;
        public float TransitionDelay;
        public float Weight;
        public bool Transitioning;
    }

    public class WeatherSystem : ModSystem
    {
        public ICoreAPI api;

        public ICoreClientAPI capi;
        public IClientNetworkChannel clientChannel;
        public CloudRendererDummy cloudRenderer;
        WeatherState initialWeatherFromServer;


        public ICoreServerAPI sapi;
        public IServerNetworkChannel serverChannel;


        public WeatherSimulation weatherSim;

        protected NormalizedSimplexNoise windNoise;


        public int CloudTileLength
        {
            get { return cloudRenderer.CloudTileLength; }
        }

        public int CloudTileX
        {
            get { return cloudRenderer.tilePosX - cloudRenderer.tileOffsetX; }
        }
        public int CloudTileZ
        {
            get { return cloudRenderer.tilePosZ - cloudRenderer.tileOffsetZ; }
        }

        public override bool ShouldLoad(EnumAppSide side)
        {
            return true;
        }


        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;
        }

        public double GetWindSpeed(BlockPos pos)
        {
            double noise = windNoise.Noise(pos.X / 10.0, api.World.Calendar.TotalHours, pos.Z / 10.0);
            double strength = GameMath.Clamp(2*(noise - 0.4), 0, 1);
            
            if (pos.Y > api.World.SeaLevel)
            {
                // Greater wind at greater heights
                strength *= Math.Max(1, (pos.Y - api.World.SeaLevel) / 100.0);

            } else
            {
                // Much muuuch lower winds at lower heights
                strength /= 1 + (api.World.SeaLevel - pos.Y) / 4;
            }

            //Console.WriteLine(api.World.Side + ": " + strength);// + " / " + api.World.Calendar.TotalHours);

            return strength;
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            this.sapi = api;

            serverChannel =
               api.Network.RegisterChannel("weather")
               .RegisterMessageType(typeof(WeatherState))
            ;

            sapi.RegisterCommand("weather", "Show current weather info", "", cmdWeatherServer, Privilege.controlserver);

            sapi.Event.RegisterGameTickListener(OnGameTick, 50);

            sapi.Event.SaveGameLoaded += OnSaveGameLoaded;
            sapi.Event.GameWorldSave += OnSaveGameSaving;

            api.Event.PlayerJoin += OnPlayerJoin;

            this.cloudRenderer = new CloudRendererDummy();
        }

        private void OnSaveGameSaving()
        {
            sapi.WorldManager.SaveGame.StoreData("weatherState", SerializerUtil.Serialize(new WeatherState()
            {
                NewPatternIndex = weatherSim.NewPattern.Index,
                OldPatternIndex = weatherSim.OldPattern.Index,
                Weight = weatherSim.Weight,
                TransitionDelay = weatherSim.TransitionDelay,
                Transitioning = weatherSim.Transitioning
            }));
        }

        private void OnSaveGameLoaded()
        {
            InitWeatherSim();

            if (sapi.WorldManager.SaveGame.IsNew)
            {
                weatherSim.LoadRandomPattern();
            } else
            {
                try
                {
                    byte[] data = sapi.WorldManager.SaveGame.GetData("weatherState");
                    if (data == null)
                    {
                        weatherSim.LoadRandomPattern();
                    }
                    else
                    {
                        WeatherState storedstate = SerializerUtil.Deserialize<WeatherState>(data);
                        weatherSim.NewPattern = weatherSim.Patterns[storedstate.NewPatternIndex];
                        weatherSim.OldPattern = weatherSim.Patterns[storedstate.OldPatternIndex];
                        weatherSim.Weight = storedstate.Weight;
                        weatherSim.TransitionDelay = storedstate.TransitionDelay;
                        weatherSim.Transitioning = storedstate.Transitioning;
                    }
                } catch (Exception)
                {
                    weatherSim.LoadRandomPattern();
                }
                
            }
        }

        private void OnPlayerJoin(IServerPlayer byPlayer)
        {
            serverChannel.SendPacket(new WeatherState()
            {
                NewPatternIndex = weatherSim.NewPattern.Index,
                OldPatternIndex = weatherSim.OldPattern.Index,
                TransitionDelay = weatherSim.TransitionDelay,
                Transitioning = weatherSim.Transitioning,
                Weight = weatherSim.Weight

            }, byPlayer);
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;
            clientChannel =
                 capi.Network.RegisterChannel("weather")
                .RegisterMessageType(typeof(WeatherState))
                .SetMessageHandler<WeatherState>(OnWeatherUpdate)
             ;

            capi.RegisterCommand("cdensity", "Set cloud density", "[density] (best values between -1 and 1)", cDensity);

            capi.RegisterCommand("clight", "Set global Cloud brightness", "global brightness (best values between 0 and 1)", cLight);
            //capi.RegisterCommand("crand", "Set large and small cloud noise.", "amp1 amp2 freq1 freq2  (best values between 0.5 and 20)", cRandom);
            capi.RegisterCommand("cviewdist", "Sets the cloud view distance. Will be reset when view distance in graphics settings are changed.", "dist (length in cloud tiles)", cTileLength);

            capi.RegisterCommand("weather", "Show current weather info", "", cmdWeatherClient);

            capi.Event.RegisterGameTickListener(OnGameTick, 50);
            capi.Event.LevelFinalize += InitWeatherSim;
            capi.Event.LeaveWorld += () => (cloudRenderer as CloudRenderer)?.Dispose();
        }

        private void OnWeatherUpdate(WeatherState msg)
        {
            if (weatherSim == null)
            {
                initialWeatherFromServer = msg;
                return;
            }

            weatherSim.NewPattern = weatherSim.Patterns[msg.NewPatternIndex];
            weatherSim.OldPattern = weatherSim.Patterns[msg.OldPatternIndex];
            weatherSim.TransitionDelay = msg.TransitionDelay;
            weatherSim.Transitioning = msg.Transitioning;
            weatherSim.Weight = msg.Weight;

            if (msg.Transitioning)
            {
                weatherSim.Weight = 0;
            }

            if (weatherSim.NewPattern != weatherSim.OldPattern)
            {
                weatherSim.NewPattern.OnBeginUse();
            }
        }

        private void OnGameTick(float dt)
        {
            weatherSim?.Update(dt);
        }

        private void InitWeatherSim()
        {
            windNoise = NormalizedSimplexNoise.FromDefaultOctaves(5, 0.1, 0.8, api.World.Seed + 2323182);


            weatherSim = new WeatherSimulation(this);
            if (api.Side == EnumAppSide.Client)
            {
                cloudRenderer = new CloudRenderer(capi, weatherSim);
            }

            weatherSim.Initialize();

            if (initialWeatherFromServer != null)
            {
                OnWeatherUpdate(initialWeatherFromServer);
                initialWeatherFromServer = null;
            }

            // Pre init the clouds.             
            if (api.Side == EnumAppSide.Client)
            {
                capi.Ambient.UpdateAmbient(0.1f);
                CloudRenderer renderer = this.cloudRenderer as CloudRenderer;

                renderer.blendedCloudDensity = capi.Ambient.BlendedCloudDensity;
                renderer.blendedGlobalCloudBrightness = capi.Ambient.BlendedCloudBrightness;
                renderer.UpdateWindAndClouds(0.1f);
            }
        }

        private void cmdWeatherServer(IServerPlayer player, int groupId, CmdArgs args)
        {
            string arg = args.PopWord();

            if (arg == "t")
            {
                weatherSim.TriggerTransition();

                player.SendMessage(groupId, "Ok transitioning to another weather pattern", EnumChatType.CommandSuccess);
                return;
            }

            if (arg == "c")
            {
                weatherSim.TriggerTransition(1f);
                player.SendMessage(groupId, "Ok selected another weather pattern", EnumChatType.CommandSuccess);
                return;
            }

            if (arg == "set")
            {
                string code = args.PopWord();
                if (weatherSim.SetWeatherPattern(code))
                {
                    player.SendMessage(groupId, "Ok weather pattern set", EnumChatType.CommandSuccess);
                } else
                {
                    player.SendMessage(groupId, "No such weather pattern found", EnumChatType.CommandError);
                }
                return;
            }

            player.SendMessage(
                groupId, 
                string.Format("{0}% {1}, {2}% {3}", (int)(100 * weatherSim.Weight), weatherSim.NewPattern.GetWeatherName(), (int)(100 - 100 * weatherSim.Weight), weatherSim.OldPattern.GetWeatherName()),
                EnumChatType.Notification
            );
        }


        private void cmdWeatherClient(int groupId, CmdArgs args)
        {
            capi.ShowChatMessage(
                string.Format("{0}% {1}, {2}% {3}", (int)(100 * weatherSim.Weight), weatherSim.NewPattern.GetWeatherName(), (int)(100 - 100 * weatherSim.Weight), weatherSim.OldPattern.GetWeatherName())
            );
        }



        private void cTileLength(int groupId, CmdArgs args)
        {
            CloudRenderer renderer = this.cloudRenderer as CloudRenderer;

            if (args.Length == 0)
            {
                capi.ShowChatMessage(string.Format("Current view distance: {0}", cloudRenderer.CloudTileLength * renderer.CloudTileSize));
                return;
            }

            try
            {
                int dist = int.Parse(args[0]);
                

                renderer.InitCloudTiles(dist);
                renderer.UpdateCloudTiles();
                renderer.LoadCloudModel();
                capi.ShowChatMessage(string.Format("New view distance {0} set.", dist));
            }
            catch (Exception)
            {
                capi.ShowChatMessage("Exception when parsing params");
            }

        }
        

        private void cLight(int groupId, CmdArgs args)
        {
            if (args.Length > 0)
            {
                capi.Ambient.Base.CloudBrightness.Value = float.Parse(args[0]);
                capi.ShowChatMessage("Cloud brightness set");
            }
        }

        private void cDensity(int groupId, CmdArgs args)
        {
            if (args.Length > 0)
            {
                capi.Ambient.Base.CloudDensity.Value = float.Parse(args[0]);
                capi.ShowChatMessage("New cloud densities set");
            }
        }
    }
}
