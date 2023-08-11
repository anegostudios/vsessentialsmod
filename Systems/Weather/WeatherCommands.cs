using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AnimatedGif;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

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

#if DEBUG
            sapi.RegisterCommand("prectest", "Precipitation test export", "", cmdPrecTestServer, Privilege.controlserver);
            sapi.RegisterCommand("snowaccum", "Snow accum test", "", cmdSnowAccum, Privilege.controlserver);
#endif

            sapi.RegisterCommand("whenwillitstopraining", "When does it finally stop to rain around here?!", "", cmdWhenWillItStopRaining, Privilege.controlserver);
            sapi.RegisterCommand("weather", "Show/Set current weather info", "", cmdWeatherServer, Privilege.controlserver);
        }

        private void cmdWhenWillItStopRaining(IServerPlayer player, int groupId, CmdArgs args)
        {
            rainStopFunc(player, groupId);
        }

        private void rainStopFunc(IServerPlayer player, int groupId, bool skipForward = false)
        {
            WeatherSystemServer wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            if (wsys.OverridePrecipitation != null)
            {
                player.SendMessage(groupId, string.Format("Override precipitation set, rain pattern will not change. Fix by typing /weather setprecip auto."), EnumChatType.CommandSuccess);
                return;
            }

            Vec3d pos = player.Entity.Pos.XYZ;

            float days = 0;
            float daysrainless = 0f;
            float firstRainLessDay = 0f;
            bool found = false;
            while (days < 21)
            {
                float precip = wsys.GetPrecipitation(pos.X, pos.Y, pos.Z, sapi.World.Calendar.TotalDays + days);
                if (precip < 0.04f)
                {
                    if (!found)
                    {
                        firstRainLessDay = days;
                    }

                    found = true;

                    daysrainless += 1f / sapi.World.Calendar.HoursPerDay;

                }
                else
                {
                    if (found) break;
                }

                days += 1f / sapi.World.Calendar.HoursPerDay;
            }


            if (daysrainless > 0)
            {
                if (skipForward)
                {
                    wsys.RainCloudDaysOffset += daysrainless;
                    player.SendMessage(groupId, string.Format("Ok, forwarded rain simulation by {0:0.##} days. The rain should stop for about {1:0.##} days now", firstRainLessDay, daysrainless), EnumChatType.CommandSuccess);
                    return;
                }

                player.SendMessage(groupId, string.Format("In about {0:0.##} days the rain should stop for about {1:0.##} days", firstRainLessDay, daysrainless), EnumChatType.CommandSuccess);
            }
            else
            {
                player.SendMessage(groupId, string.Format("No rain less days found for the next 3 in-game weeks :O"), EnumChatType.CommandSuccess);
            }
        }

        private void cmdSnowAccum(IServerPlayer player, int groupId, CmdArgs args)
        {
            WeatherSystemServer wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();

            string cmd = args.PopWord();

            if (cmd == "on")
            {
                wsys.snowSimSnowAccu.ProcessChunks = true;
                player.SendMessage(groupId, "Snow accum process chunks on", EnumChatType.CommandSuccess);
                return;
            }

            if (cmd == "off")
            {
                wsys.snowSimSnowAccu.ProcessChunks = false;
                player.SendMessage(groupId, "Snow accum process chunks off", EnumChatType.CommandSuccess);
                return;
            }

            if (cmd == "processhere")
            {
                BlockPos plrPos = player.Entity.Pos.AsBlockPos;
                int chunksize = api.World.BlockAccessor.ChunkSize;
                Vec2i chunkPos = new Vec2i(plrPos.X / chunksize, plrPos.Z / chunksize); 

                wsys.snowSimSnowAccu.AddToCheckQueue(chunkPos);
                player.SendMessage(groupId, "Ok, added to check queue", EnumChatType.CommandSuccess);
                return;
            }

            if (cmd == "info")
            {
                BlockPos plrPos = player.Entity.Pos.AsBlockPos; 
                int chunksize = api.World.BlockAccessor.ChunkSize;
                Vec2i chunkPos = new Vec2i(plrPos.X / chunksize, plrPos.Z / chunksize);
                IServerMapChunk mc = sapi.WorldManager.GetMapChunk(chunkPos.X, chunkPos.Y);

                double lastSnowAccumUpdateTotalHours = mc.GetModdata<double>("lastSnowAccumUpdateTotalHours");

                player.SendMessage(groupId, "lastSnowAccumUpdate: " + (api.World.Calendar.TotalHours - lastSnowAccumUpdateTotalHours) + " hours ago", EnumChatType.CommandSuccess);

                int regionX = (int)player.Entity.Pos.X / sapi.World.BlockAccessor.RegionSize;
                int regionZ = (int)player.Entity.Pos.Z / sapi.World.BlockAccessor.RegionSize;

                WeatherSystemServer wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
                long index2d = wsysServer.MapRegionIndex2D(regionX, regionZ);
                WeatherSimulationRegion simregion;
                wsysServer.weatherSimByMapRegion.TryGetValue(index2d, out simregion);

                int reso = WeatherSimulationRegion.snowAccumResolution;

                SnowAccumSnapshot sumsnapshot = new SnowAccumSnapshot()
                {
                    //SumTemperatureByRegionCorner = new API.FloatDataMap3D(reso, reso, reso),
                    SnowAccumulationByRegionCorner = new FloatDataMap3D(reso, reso, reso)
                };
                float[] sumdata = sumsnapshot.SnowAccumulationByRegionCorner.Data;

                // Can't grow bigger than one full snow block
                float max = 3 + 0.5f;

                int len = simregion.SnowAccumSnapshots.Length;
                int i = simregion.SnowAccumSnapshots.EndPosition;
                
                // This code here causes wacky snow patterns
                // The lerp itself is fine!!!
                while (len-- > 0)
                {
                    SnowAccumSnapshot hoursnapshot = simregion.SnowAccumSnapshots[i];
                    i = (i + 1) % simregion.SnowAccumSnapshots.Length;

                    float[] snowaccumdata = hoursnapshot.SnowAccumulationByRegionCorner.Data;
                    for (int j = 0; j < snowaccumdata.Length; j++)
                    {
                        sumdata[j] = GameMath.Clamp(sumdata[j] + snowaccumdata[j], -max, max);
                    }

                    lastSnowAccumUpdateTotalHours = Math.Max(lastSnowAccumUpdateTotalHours, hoursnapshot.TotalHours);
                }

                

                for (int j = 0; j < sumdata.Length; j++)
                {
                    player.SendMessage(groupId, j + ": " + sumdata[j], EnumChatType.CommandSuccess);
                }

                return;
            }

            if (cmd == "here")
            {
                float amount = (float)args.PopFloat(0);

                BlockPos plrPos = player.Entity.Pos.AsBlockPos;
                int chunksize = api.World.BlockAccessor.ChunkSize;
                Vec2i chunkPos = new Vec2i(plrPos.X / chunksize, plrPos.Z / chunksize);
                IServerMapChunk mc = sapi.WorldManager.GetMapChunk(chunkPos.X, chunkPos.Y);
                int reso = WeatherSimulationRegion.snowAccumResolution;

                SnowAccumSnapshot sumsnapshot = new SnowAccumSnapshot()
                {
                    SumTemperatureByRegionCorner = new FloatDataMap3D(reso, reso, reso),
                    SnowAccumulationByRegionCorner = new FloatDataMap3D(reso, reso, reso)
                };

                sumsnapshot.SnowAccumulationByRegionCorner.Data.Fill(amount);

                var updatepacket = wsys.snowSimSnowAccu.UpdateSnowLayer(sumsnapshot, true, mc, chunkPos, null);
                wsys.snowSimSnowAccu.accum = 1f;

                var ba = sapi.World.GetBlockAccessorBulkMinimalUpdate(true, false);
                ba.UpdateSnowAccumMap = false;

                wsys.snowSimSnowAccu.processBlockUpdates(mc, updatepacket, ba);
                ba.Commit();

                player.SendMessage(groupId, "Ok, test snow accum gen complete", EnumChatType.CommandSuccess);
                return;
            }
        }



        private void cmdPrecTestServer(IServerPlayer player, int groupId, CmdArgs args)
        {
            WeatherSystemServer wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            EntityPos pos = player.Entity.Pos;

            int wdt = 400;
            float hourStep = 4f;
            float days = 1f;
            float posStep = 2f;

            double totaldays = api.World.Calendar.TotalDays;
            

            string subarg = args.PopWord();
            bool climateTest = subarg == "climate";

            if (subarg == "pos")
            {
                float precip = wsys.GetPrecipitation(pos.X, pos.Y, pos.Z, totaldays);
                player.SendMessage(groupId, "Prec here: " + precip, EnumChatType.CommandSuccess);
                return;
            }

            ClimateCondition conds = api.World.BlockAccessor.GetClimateAt(new BlockPos((int)pos.X, (int)pos.Y, (int)pos.Z), EnumGetClimateMode.WorldGenValues, totaldays);

            int offset = wdt / 2;
            SKBitmap bmp;
            int[] pixels;

            if (subarg == "here")
            {
                wdt = 400;
                bmp = new SKBitmap(wdt, wdt);
                pixels = new int[wdt * wdt];
                posStep = 3f;
                offset = wdt / 2;

                for (int dx = 0; dx < wdt; dx++)
                {
                    for (int dz = 0; dz < wdt; dz++)
                    {
                        float x = dx * posStep - offset;
                        float z = dz * posStep - offset;

                        if ((int)x == 0 && (int)z == 0)
                        {
                            pixels[dz * wdt + dx] = ColorUtil.ColorFromRgba(255, 0, 0, 255);
                            continue;
                        }

                        float precip = wsys.GetPrecipitation(pos.X + x, pos.Y, pos.Z + z, totaldays);
                        int precipi = (int)GameMath.Clamp(255 * precip, 0, 254);
                        pixels[dz * wdt + dx] = ColorUtil.ColorFromRgba(precipi, precipi, precipi, 255);
                    }
                }

                bmp.SetPixels(pixels);
                bmp.Save("preciphere.png");
                player.SendMessage(groupId, "Ok exported", EnumChatType.CommandSuccess);

                return;

            }
            
            if (RuntimeEnv.OS != OS.Windows)
            {
                player.SendMessage(groupId, "Command only supported on windows, try sub argument \"here\"", EnumChatType.CommandError);
                return;
            }

            var bmpgif =  new Bitmap(wdt, wdt);
            pixels = new int[wdt * wdt];
            
            using (var gif = new AnimatedGifCreator("precip.gif", 100, -1))
            {
                for (int i = 0; i < days * 24f; i++) {
            
                    if (climateTest)
                    {
                        for (int dx = 0; dx < wdt; dx++)
                        {
                            for (int dz = 0; dz < wdt; dz++)
                            {
                                conds.Rainfall = (float)i / (days * 24f);
                                float precip = wsys.GetRainCloudness(conds, pos.X + dx * posStep - offset, pos.Z + dz * posStep - offset, api.World.Calendar.TotalDays);
                                int precipi = (int)GameMath.Clamp(255 * precip, 0, 254);
                                pixels[dz * wdt + dx] = ColorUtil.ColorFromRgba(precipi, precipi, precipi, 255);
                            }
                        }
            
                    }
                    else
                    {
                        for (int dx = 0; dx < wdt; dx++)
                        {
                            for (int dz = 0; dz < wdt; dz++)
                            {
                                float precip = wsys.GetPrecipitation(pos.X + dx * posStep - offset, pos.Y, pos.Z + dz * posStep - offset, totaldays);
                                int precipi = (int)GameMath.Clamp(255 * precip, 0, 254);
                                pixels[dz * wdt + dx] = ColorUtil.ColorFromRgba(precipi, precipi, precipi, 255);
                            }
                        }
                    }
            
            
                    totaldays += hourStep / 24f;
            
                    bmpgif.SetPixels(pixels);
                    
                    gif.AddFrame(bmpgif, 100, GifQuality.Grayscale);
                    
                }
            
                
            }

            player.SendMessage(groupId, "Ok exported", EnumChatType.CommandSuccess);
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

            int regionX = (int)player.Entity.Pos.X / sapi.World.BlockAccessor.RegionSize;
            int regionZ = (int)player.Entity.Pos.Z / sapi.World.BlockAccessor.RegionSize;

            string arg = args.PopWord();

            if (arg == "setprecip")
            {
                if (args.Length == 0)
                {
                    if (wsysServer.OverridePrecipitation == null)
                    {
                        player.SendMessage(groupId, "Currently no precipitation override active.", EnumChatType.CommandSuccess);
                    } else
                    {
                        player.SendMessage(groupId, string.Format("Override precipitation value is currently at {0}.", wsysServer.OverridePrecipitation), EnumChatType.CommandSuccess);
                    }
                    return;
                }

                string val = args.PopWord();

                if (val == "auto")
                {
                    wsysServer.OverridePrecipitation = null;
                    player.SendMessage(groupId, "Ok auto precipitation on", EnumChatType.CommandSuccess);

                } else
                {
                    float level = val.ToFloat(0);
                    wsysServer.OverridePrecipitation = level;
                    player.SendMessage(groupId, string.Format("Ok precipitation set to {0}", level), EnumChatType.CommandSuccess);
                }

                wsysServer.serverChannel.BroadcastPacket(new WeatherConfigPacket() { 
                    OverridePrecipitation = wsysServer.OverridePrecipitation,
                    RainCloudDaysOffset = wsysServer.RainCloudDaysOffset
                });

                return;
            }

            if (arg == "cloudypos" || arg == "cyp")
            {
                if (args.Length==0)
                {
                    player.SendMessage(groupId, "Cloud level rel = " + wsysServer.CloudLevelRel, EnumChatType.CommandSuccess);
                    return;
                }

                wsysServer.CloudLevelRel = (float)args.PopDouble(0.95f);

                wsysServer.serverChannel.BroadcastPacket(new WeatherCloudYposPacket() { CloudYRel = wsysServer.CloudLevelRel });

                player.SendMessage(groupId, string.Format("Cloud level rel {0:0.##} set. (y={1})", wsysServer.CloudLevelRel, (int)(wsysServer.CloudLevelRel*wsysServer.api.World.BlockAccessor.MapSizeY)), EnumChatType.CommandSuccess);
                return;
            }

            if (arg == "stoprain")
            {
                rainStopFunc(player, groupId, true);
                wsysServer.broadCastConfigUpdate();
                return;
            }

            if (arg == "acp")
            {
                wsysServer.autoChangePatterns = !wsysServer.autoChangePatterns;
                player.SendMessage(groupId, "Ok autochange weather patterns now " + (wsysServer.autoChangePatterns ? "on" : "off"), EnumChatType.CommandSuccess);
                return;
            }

            if (arg == "lp")
            {
                string patterns = string.Join(", ", wsysServer.WeatherConfigs.Select(c => c.Code));
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
                    val.Value.ReloadPatterns(api.World.Seed);

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

            if (arg == "randomevent")
            {
                foreach (var val in wsysServer.weatherSimByMapRegion)
                {
                    val.Value.selectRandomWeatherEvent();
                    val.Value.sendWeatherUpdatePacket();
                }

                player.SendMessage(groupId, "Random weather event selected for all regions", EnumChatType.CommandError);
            }
            if (arg == "events")
            {

            }

            if (arg == "setev" || arg == "setevr" || arg == "setevf")
            {
                wsysServer.ReloadConfigs();
                string code = args.PopWord();

                WeatherSimulationRegion weatherSim;

                if (arg == "setevr")
                {
                    long index2d = wsysServer.MapRegionIndex2D(regionX, regionZ);
                    wsysServer.weatherSimByMapRegion.TryGetValue(index2d, out weatherSim);
                    if (weatherSim == null)
                    {
                        player.SendMessage(groupId, "Weather sim not loaded (yet) for this region", EnumChatType.CommandError);
                        return;
                    }

                    if (weatherSim.SetWeatherEvent(code, true))
                    {
                        weatherSim.CurWeatherEvent.AllowStop = arg != "setevf";

                        weatherSim.CurWeatherEvent.OnBeginUse();
                        weatherSim.TickEvery25ms(0.025f);
                        player.SendMessage(groupId, "Ok weather event for this region set", EnumChatType.CommandSuccess);
                    }
                    else
                    {
                        player.SendMessage(groupId, "No such weather event found", EnumChatType.CommandError);
                    }
                } else
                {
                    bool ok = true;
                    foreach (var val in wsysServer.weatherSimByMapRegion)
                    {
                        ok &= val.Value.SetWeatherEvent(code, true);

                        val.Value.CurWeatherEvent.AllowStop = arg != "setevf";

                        if (ok)
                        {
                            val.Value.CurWeatherEvent.OnBeginUse();
                            val.Value.TickEvery25ms(0.025f);
                        }
                    }

                    if (!ok)
                    {
                        player.SendMessage(groupId, "No such weather event found", EnumChatType.CommandError);
                    }
                    else
                    {
                        player.SendMessage(groupId, "Ok weather event set for all loaded regions", EnumChatType.CommandSuccess);
                    }
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
                    val.Value.ReloadPatterns(api.World.Seed);
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
                    player.SendMessage(groupId, "Ok weather pattern set for all loaded regions", EnumChatType.CommandSuccess);
                }
                return;
            }

            if (arg == "setirandom")
            {
                wsysServer.ReloadConfigs();
                
                bool ok = true;
                foreach (var val in wsysServer.weatherSimByMapRegion)
                {
                    ok &= val.Value.SetWeatherPattern(val.Value.RandomWeatherPattern().config.Code, true);
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
                    player.SendMessage(groupId, "Ok random weather pattern set", EnumChatType.CommandSuccess);
                }
                return;
            }

            if (arg == "setir")
            {
                wsysServer.ReloadConfigs();
                string code = args.PopWord();

                WeatherSimulationRegion weatherSim;
                long index2d = wsysServer.MapRegionIndex2D(regionX, regionZ);
                wsysServer.weatherSimByMapRegion.TryGetValue(index2d, out weatherSim);
                if (weatherSim == null)
                {
                    player.SendMessage(groupId, "Weather sim not loaded (yet) for this region", EnumChatType.CommandError);
                    return;
                }

                if (weatherSim.SetWeatherPattern(code, true))
                {
                    weatherSim.TickEvery25ms(0.025f);
                    player.SendMessage(groupId, "Ok weather pattern set for current region", EnumChatType.CommandSuccess);
                } else
                {
                    player.SendMessage(groupId, "No such weather pattern found", EnumChatType.CommandError);
                }
                return;
            }


            string text = getWeatherInfo<WeatherSystemServer>(player);
            player.SendMessage(groupId, text, EnumChatType.CommandSuccess);
        }


        private string getWeatherInfo<T>(IPlayer player) where T: WeatherSystemBase
        {
            T wsys = api.ModLoader.GetModSystem<T>();

            Vec3d plrPos = player.Entity.SidedPos.XYZ;
            BlockPos pos = plrPos.AsBlockPos;

            var wreader = wsys.getWeatherDataReaderPreLoad();

            wreader.LoadAdjacentSimsAndLerpValues(plrPos, 1);

            int regionX = (int)pos.X / api.World.BlockAccessor.RegionSize;
            int regionZ = (int)pos.Z / api.World.BlockAccessor.RegionSize;

            WeatherSimulationRegion weatherSim;
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

            double tlLerp = GameMath.BiLerp(1, 0, 0, 0, wreader.LerpLeftRight, wreader.LerpTopBot);
            double trLerp = GameMath.BiLerp(0, 1, 0, 0, wreader.LerpLeftRight, wreader.LerpTopBot);
            double blLerp = GameMath.BiLerp(0, 0, 1, 0, wreader.LerpLeftRight, wreader.LerpTopBot);
            double brLerp = GameMath.BiLerp(0, 0, 0, 1, wreader.LerpLeftRight, wreader.LerpTopBot);

            int[] lerps = new int[] { (int)(100*tlLerp), (int)(100 * trLerp), (int)(100 * blLerp), (int)(100 * brLerp) };

            for (int i = 0; i < 4; i++)
            {
                WeatherSimulationRegion sim = wreader.AdjacentSims[i];

                if (sim == wsys.dummySim)
                {
                    sb.AppendLine(string.Format("{0}: missing", cornerNames[i]));
                }
                else
                {
                    string weatherpattern = sim.OldWePattern.GetWeatherName();
                    if (sim.Weight < 1)
                    {
                        weatherpattern = string.Format("{0} transitioning to {1} ({2}%)", sim.OldWePattern.GetWeatherName(), sim.NewWePattern.GetWeatherName(), (int)(100*sim.Weight));
                    }

                    sb.AppendLine(string.Format("{0}: {1}% {2}. Wind: {3} (str={4}), Event: {5}",
                        cornerNames[i],
                        lerps[i],
                        weatherpattern,
                        sim.CurWindPattern.GetWindName(),
                        sim.GetWindSpeed(pos.Y).ToString("0.###"),
                        sim.CurWeatherEvent.config.Code
                    ));
                }
            }

            //wsys.updateAdjacentAndBlendWeatherData();
            //WeatherDataSnapshot wData = wsys.blendedWeatherData;
            //sb.AppendLine(string.Format(string.Format("Blended:\nPrecipitation: {0}, Particle size: {1}, Type: {2}, Wind speed: {3}", wData.PrecIntensity, wData.PrecParticleSize, wData.BlendedPrecType, wsys.GetWindSpeed(plrPos))));
            ClimateCondition climate = api.World.BlockAccessor.GetClimateAt(player.Entity.Pos.AsBlockPos, EnumGetClimateMode.NowValues);
            sb.AppendLine(string.Format("Current precipitation: {0}%", (int)(climate.Rainfall * 100f)));
            sb.AppendLine(string.Format("Current wind: {0}", GlobalConstants.CurrentWindSpeedClient));

            return sb.ToString();
        }
    }
}

public static class BitmapExtensions
{
    public static void SetPixels(this Bitmap bmp, int[] pixels)
    {
        if (bmp.Width * bmp.Height != pixels.Length) throw new ArgumentException("Pixel array must be width*height length");

        Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var bitmapData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, bmp.PixelFormat);

        Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);

        bmp.UnlockBits(bitmapData);
    }
}
