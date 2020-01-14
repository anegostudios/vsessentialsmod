using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class WeatherSystemCommands : ModSystem
    {
        ICoreClientAPI capi;
        ICoreServerAPI sapi;

        ICoreAPI api;


        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void Start(ICoreAPI api)
        {
            this.api = api;
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            sapi.RegisterCommand("weather", "Show/Set current weather info", "", cmdWeatherServer, Privilege.controlserver);
        }


        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;
            capi.RegisterCommand("weather", "Show current weather info", "", cmdWeatherClient);
        }

        private void cmdWeatherClient(int groupId, CmdArgs args)
        {
            string text = getWeatherInfo<WeatherSystemClient>(capi.World.Player);
            capi.ShowChatMessage(text);
        }



        private void cmdWeatherServer(IServerPlayer player, int groupId, CmdArgs args)
        {
            WeatherSystemServer wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();

            string arg = args.PopWord();

            if (arg == "acp")
            {
                wsysServer.autoChangePatterns = !wsysServer.autoChangePatterns;
                player.SendMessage(groupId, "Ok autochange weather patterns now " + (wsysServer.autoChangePatterns ? "on" : "off"), EnumChatType.CommandSuccess);
                return;
            }

            if (arg == "lp")
            {
                string patterns = string.Join(", ", wsysServer.weatherConfigs.Select(c => c.Code));
                player.SendMessage(groupId, "Patterns: " + patterns, EnumChatType.CommandSuccess);
                return;
            }

            if (arg == "t")
            {
                foreach (var val in wsysServer.weatherSimByMapRegion)
                {
                    val.Value.TriggerTransition();
                }

                player.SendMessage(groupId, "Ok transitioning to another weather pattern", EnumChatType.CommandSuccess);
                return;
            }

            if (arg == "c")
            {
                foreach (var val in wsysServer.weatherSimByMapRegion)
                {
                    val.Value.TriggerTransition(1f);
                }
                player.SendMessage(groupId, "Ok selected another weather pattern", EnumChatType.CommandSuccess);
                return;
            }

            if (arg == "setw")
            {
                wsysServer.ReloadConfigs();
                string code = args.PopWord();
                bool ok = true;
                foreach (var val in wsysServer.weatherSimByMapRegion)
                {
                    ok &= val.Value.SetWindPattern(code, true);
                    if (ok)
                    {
                        val.Value.TickEvery25ms(0.025f);
                    }
                }

                if (!ok)
                {
                    player.SendMessage(groupId, "No such wind pattern found", EnumChatType.CommandError);
                }
                else
                {
                    player.SendMessage(groupId, "Ok wind pattern set", EnumChatType.CommandSuccess);
                }
                return;
            }

            if (arg == "set" || arg == "seti")
            {
                wsysServer.ReloadConfigs();
                string code = args.PopWord();
                bool ok = true;
                foreach (var val in wsysServer.weatherSimByMapRegion)
                {
                    ok &= val.Value.SetWeatherPattern(code, true);
                    if (ok)
                    {
                        val.Value.TickEvery25ms(0.025f);
                    }
                }

                if (!ok)
                {
                    player.SendMessage(groupId, "No such weather pattern found", EnumChatType.CommandError);
                }
                else
                {
                    player.SendMessage(groupId, "Ok weather pattern set", EnumChatType.CommandSuccess);
                }
                return;
            }

            string text = getWeatherInfo<WeatherSystemServer>(player);
            player.SendMessage(groupId, text, EnumChatType.CommandSuccess);
        }


        private string getWeatherInfo<T>(IPlayer player) where T: WeatherSystemBase
        {
            T wsys = api.ModLoader.GetModSystem<T>();

            Vec3d plrPos = player.Entity.LocalPos.XYZ;
            BlockPos pos = plrPos.AsBlockPos;

            wsys.LoadAdjacentSimsAndLerpValues(plrPos);

            int regionX = (int)pos.X / api.World.BlockAccessor.RegionSize;
            int regionZ = (int)pos.Z / api.World.BlockAccessor.RegionSize;

            WeatherSimulation weatherSim;
            long index2d = wsys.MapRegionIndex2D(regionX, regionZ);
            wsys.weatherSimByMapRegion.TryGetValue(index2d, out weatherSim);
            if (weatherSim == null)
            {
                return "weatherSim is null. No idea what to do here";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("Weather by region:")); // (lerp-lr: {0}, lerp-bt: {1}), wsys.lerpLeftRight.ToString("0.##"), wsys.lerpTopBot.ToString("0.##")));
            string[] cornerNames = new string[] { "tl", "tr", "bl", "br" };

            //topBlendedWeatherData.SetLerped(adjacentSims[0].weatherData, adjacentSims[1].weatherData, (float)lerpLeftRight);
            //botBlendedWeatherData.SetLerped(adjacentSims[2].weatherData, adjacentSims[3].weatherData, (float)lerpLeftRight);
            //blendedWeatherData.SetLerped(topBlendedWeatherData, botBlendedWeatherData, (float)lerpTopBot);

            double tlLerp = GameMath.BiLerp(1, 0, 0, 0, wsys.lerpLeftRight, wsys.lerpTopBot);
            double trLerp = GameMath.BiLerp(0, 1, 0, 0, wsys.lerpLeftRight, wsys.lerpTopBot);
            double blLerp = GameMath.BiLerp(0, 0, 1, 0, wsys.lerpLeftRight, wsys.lerpTopBot);
            double brLerp = GameMath.BiLerp(0, 0, 0, 1, wsys.lerpLeftRight, wsys.lerpTopBot);

            int[] lerps = new int[] { (int)(100*tlLerp), (int)(100 * trLerp), (int)(100 * blLerp), (int)(100 * brLerp) };

            for (int i = 0; i < 4; i++)
            {
                WeatherSimulation sim = wsys.adjacentSims[i];

                if (sim == wsys.dummySim)
                {
                    sb.AppendLine(string.Format("{0}: missing", cornerNames[i]));
                }
                else
                {
                    sb.AppendLine(string.Format("{10}% of {0}@{8}/{9}: {1}% {2}, {3}% {4}. Prec: {5}, Wind: {6} (v={7})",
                        cornerNames[i], (int)(100 * sim.Weight), sim.NewWePattern.GetWeatherName(), (int)(100 - 100 * sim.Weight),
                        sim.OldWePattern.GetWeatherName(), sim.weatherData.PrecIntensity.ToString("0.###"),
                        sim.CurWindPattern.GetWindName(), sim.GetWindSpeed(pos.Y).ToString("0.###"),
                        sim.regionX, sim.regionZ,
                        lerps[i]
                    )) ;
                }
            }

            wsys.updateAdjacentAndBlendWeatherData();

            WeatherDataSnapshot wData = wsys.blendedWeatherData;
            sb.AppendLine(string.Format(string.Format("Blended:\nPrecipitation: {0}, Particle size: {1}, Type: {2}, Wind speed: {3}", wData.PrecIntensity, wData.PrecParticleSize, wData.BlendedPrecType, wsys.GetWindSpeed(plrPos))));
            return sb.ToString();
        }
    }
}
