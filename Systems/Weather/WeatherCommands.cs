using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AnimatedGif;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

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
            sapi.ChatCommands.GetOrCreate("debug")
            .BeginSubCommand("prectest")
                .WithDescription("recipitation test export")
                .RequiresPrivilege(Privilege.controlserver)

                .BeginSubCommand("pos")
                    .RequiresPlayer()
                    .HandleWith(CmdPrecTestServerPos)
                .EndSubCommand()

                .BeginSubCommand("here")
                    .RequiresPlayer()
                    .HandleWith(CmdPrecTestServerHere)
                .EndSubCommand()

                .BeginSubCommand("climate")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.OptionalBool("climate"))
                    .HandleWith(CmdPrecTestServerClimate)
                .EndSubCommand()
            .EndSubCommand()
            ;

            sapi.ChatCommands.GetOrCreate("debug")
            .BeginSubCommand("snowaccum")
                .WithDescription("Snow accum test")
                .RequiresPrivilege(Privilege.controlserver)

                .BeginSubCommand("on")
                    .HandleWith(CmdSnowAccumOn)
                .EndSubCommand()

                .BeginSubCommand("off")
                    .HandleWith(CmdSnowAccumOff)
                .EndSubCommand()

                .BeginSubCommand("processhere")
                    .RequiresPlayer()
                    .HandleWith(CmdSnowAccumProcesshere)
                .EndSubCommand()

                .BeginSubCommand("info")
                    .RequiresPlayer()
                    .HandleWith(CmdSnowAccumInfo)
                .EndSubCommand()

                .BeginSubCommand("here")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.OptionalFloat("amount"))
                    .HandleWith(CmdSnowAccumHere)
                .EndSubCommand()
            .EndSubCommand()
            ;
#endif

            sapi.Event.ServerRunPhase(EnumServerRunPhase.GameReady, () =>
            {
                sapi.ChatCommands.Create("whenwillitstopraining")
                    .WithDescription("When does it finally stop to rain around here?!")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .HandleWith(CmdWhenWillItStopRaining);

                var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
                sapi.ChatCommands.Create("weather")
                    .WithDescription("Show/Set current weather info")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(CmdWeatherinfo)

                    .BeginSubCommand("setprecip")
                        .WithDescription("Running with no arguments returns the current precip. override, if one is set. Including an argument overrides the precipitation intensity and in turn also the rain cloud overlay. '-1' removes all rain clouds, '0' stops any rain but keeps some rain clouds, while '1' causes the heaviest rain and full rain clouds. The server will remain indefinitely in that rain state until reset with '/weather setprecipa'.")
                        .RequiresPlayer()
                        .WithArgs(api.ChatCommands.Parsers.OptionalFloat("level"))
                        .HandleWith(CmdWeatherSetprecip)
                    .EndSubCommand()

                    .BeginSubCommand("setprecipa")
                        .WithDescription("Resets the current precip override to auto mode.")
                        .RequiresPlayer()
                        .HandleWith(CmdWeatherSetprecipa)
                    .EndSubCommand()

                    .BeginSubCommand("cloudypos")
                        .WithAlias("cyp")
                        .RequiresPlayer()
                        .WithArgs(api.ChatCommands.Parsers.OptionalFloat("level"))
                        .HandleWith(CmdWeatherCloudypos)
                    .EndSubCommand()

                    .BeginSubCommand("stoprain")
                        .WithDescription("Stops any current rain by forwarding to a time in the future where there is no rain.")
                        .RequiresPlayer()
                        .HandleWith(CmdWeatherStoprain)
                    .EndSubCommand()

                    .BeginSubCommand("acp")
                        .WithDescription("Toggles auto-changing weather patterns.")
                        .RequiresPlayer()
                        .WithArgs(sapi.ChatCommands.Parsers.OptionalBool("mode"))
                        .HandleWith(CmdWeatherAcp)
                    .EndSubCommand()

                    .BeginSubCommand("lp")
                        .WithDescription("Lists all loaded weather patterns.")
                        .RequiresPlayer()
                        .HandleWith(CmdWeatherLp)
                    .EndSubCommand()

                    .BeginSubCommand("t")
                        .WithDescription("Transitions to a random weather pattern.")
                        .RequiresPlayer()
                        .HandleWith(CmdWeatherT)
                    .EndSubCommand()

                    .BeginSubCommand("c")
                        .WithDescription("Quickly transitions to a random weather pattern.")
                        .RequiresPlayer()
                        .HandleWith(CmdWeatherC)
                    .EndSubCommand()

                    .BeginSubCommand("setw")
                        .WithDescription("Sets the current wind pattern to the given wind pattern.")
                        .RequiresPlayer()
                        .WithArgs(api.ChatCommands.Parsers.WordRange("windpattern", wsysServer.WindConfigs.Select(w => w.Code).ToArray()))
                        .HandleWith(CmdWeatherSetw)
                    .EndSubCommand()

                    .BeginSubCommand("randomevent")
                        .RequiresPlayer()
                        .HandleWith(CmdWeatherRandomevent)
                    .EndSubCommand()

                    .BeginSubCommand("setev")
                        .WithAlias("setevr")
                        .WithDescription("setev - Sets a weather event globally.\n  setevr - Set a weather event only in the player's region.")
                        .RequiresPlayer()
                        .WithArgs(api.ChatCommands.Parsers.WordRange("weather_event", wsysServer.WeatherEventConfigs.Select(w => w.Code).ToArray()), api.ChatCommands.Parsers.OptionalBool("allowStop"))
                        .HandleWith(CmdWeatherSetev)
                    .EndSubCommand()

                    .BeginSubCommand("set")
                        .WithAlias("seti")
                        .RequiresPlayer()
                        .WithArgs(api.ChatCommands.Parsers.WordRange("weatherpattern", wsysServer.WeatherConfigs.Select(w => w.Code).ToArray()))
                        .HandleWith(CmdWeatherSet)
                    .EndSubCommand()

                    .BeginSubCommand("setirandom")
                        .RequiresPlayer()
                        .HandleWith(CmdWeatherSetirandom)
                    .EndSubCommand()

                    .BeginSubCommand("setir")
                        .RequiresPlayer()
                        .WithArgs(api.ChatCommands.Parsers.WordRange("weatherpattern", wsysServer.WeatherConfigs.Select(w => w.Code).ToArray()))
                        .HandleWith(CmdWeatherSetir)
                    .EndSubCommand()
                    ;
            });
        }

        private TextCommandResult CmdWeatherinfo(TextCommandCallingArgs args)
        {
            var text = GetWeatherInfo<WeatherSystemServer>(args.Caller.Player);
            return TextCommandResult.Success(text);
        }

        private TextCommandResult CmdWeatherSetir(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            wsysServer.ReloadConfigs();
            var code = args.Parsers[0].GetValue() as string;

            var player = args.Caller.Player as IServerPlayer;
            var pos = player.Entity.SidedPos.XYZ.AsBlockPos;

            var regionX = pos.X / api.World.BlockAccessor.RegionSize;
            var regionZ = pos.Z / api.World.BlockAccessor.RegionSize;

            long index2d = wsysServer.MapRegionIndex2D(regionX, regionZ);
            wsysServer.weatherSimByMapRegion.TryGetValue(index2d, out WeatherSimulationRegion weatherSim);
            if (weatherSim == null)
            {
                return TextCommandResult.Success("Weather sim not loaded (yet) for this region");
            }

            if (weatherSim.SetWeatherPattern(code, true))
            {
                weatherSim.TickEvery25ms(0.025f);
                return TextCommandResult.Success("Ok weather pattern set for current region");
            }

            return TextCommandResult.Error("No such weather pattern found");
        }

        private TextCommandResult CmdWeatherSetirandom(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
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

            if (ok)
            {
                return TextCommandResult.Success("Ok random weather pattern set");
            }
            return TextCommandResult.Error("No such weather pattern found");
        }

        private TextCommandResult CmdWeatherSet(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            var code = args.Parsers[0].GetValue() as string;
            wsysServer.ReloadConfigs();

            var ok = true;
            foreach (var val in wsysServer.weatherSimByMapRegion)
            {
                val.Value.ReloadPatterns(api.World.Seed);
                ok &= val.Value.SetWeatherPattern(code, true);
                if (ok)
                {
                    val.Value.TickEvery25ms(0.025f);
                }
            }
            if (ok)
            {
                return TextCommandResult.Success("Ok weather pattern set for all loaded regions");
            }

            return TextCommandResult.Error("No such weather pattern found");
        }

        private TextCommandResult CmdWeatherSetev(TextCommandCallingArgs args)
        {
            var code = args.Parsers[0].GetValue() as string;
            var allowStop = (bool)args.Parsers[1].GetValue();
            var arg = args.SubCmdCode;
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            wsysServer.ReloadConfigs();

            var player = args.Caller.Player as IServerPlayer;
            var pos = player.Entity.SidedPos.XYZ.AsBlockPos;

            var regionX = pos.X / api.World.BlockAccessor.RegionSize;
            var regionZ = pos.Z / api.World.BlockAccessor.RegionSize;


            if (arg == "setevr")
            {
                var index2d = wsysServer.MapRegionIndex2D(regionX, regionZ);
                wsysServer.weatherSimByMapRegion.TryGetValue(index2d, out WeatherSimulationRegion weatherSim);
                if (weatherSim == null)
                {
                    return TextCommandResult.Success("Weather sim not loaded (yet) for this region");
                }

                if (weatherSim.SetWeatherEvent(code, true))
                {
                    weatherSim.CurWeatherEvent.AllowStop = allowStop;

                    weatherSim.CurWeatherEvent.OnBeginUse();
                    weatherSim.TickEvery25ms(0.025f);
                    return TextCommandResult.Success("Ok weather event for this region set");
                }

                return TextCommandResult.Error("No such weather event found");
            }

            var ok = true;
            foreach (var val in wsysServer.weatherSimByMapRegion)
            {
                ok &= val.Value.SetWeatherEvent(code, true);

                val.Value.CurWeatherEvent.AllowStop = allowStop;

                if (!ok) continue;

                val.Value.CurWeatherEvent.OnBeginUse();
                val.Value.TickEvery25ms(0.025f);
            }

            if (ok)
            {
                return TextCommandResult.Success("Ok weather event set for all loaded regions");
            }
            return TextCommandResult.Error("No such weather event found");
        }

        private TextCommandResult CmdWeatherRandomevent(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            foreach (var val in wsysServer.weatherSimByMapRegion)
            {
                val.Value.selectRandomWeatherEvent();
                val.Value.sendWeatherUpdatePacket();
            }

            return TextCommandResult.Success("Random weather event selected for all regions");
        }

        private TextCommandResult CmdWeatherSetw(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            wsysServer.ReloadConfigs();
            var code = args.Parsers[0].GetValue() as string;
            var ok = true;
            foreach (var val in wsysServer.weatherSimByMapRegion)
            {
                val.Value.ReloadPatterns(api.World.Seed);

                ok &= val.Value.SetWindPattern(code, true);
                if (ok)
                {
                    val.Value.TickEvery25ms(0.025f);
                }
            }

            if (ok)
            {
                return TextCommandResult.Success("Ok wind pattern set");
            }

            return TextCommandResult.Error("No such wind pattern found");
        }

        private TextCommandResult CmdWeatherC(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            foreach (var val in wsysServer.weatherSimByMapRegion)
            {
                val.Value.TriggerTransition(1f);
            }
            return TextCommandResult.Success("Ok selected another weather pattern");
        }

        private TextCommandResult CmdWeatherT(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            foreach (var val in wsysServer.weatherSimByMapRegion)
            {
                val.Value.TriggerTransition();
            }

            return TextCommandResult.Success("Ok transitioning to another weather pattern");
        }

        private TextCommandResult CmdWeatherLp(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            var patterns = string.Join(", ", wsysServer.WeatherConfigs.Select(c => c.Code));
            return TextCommandResult.Success( "Patterns: " + patterns);
        }

        private TextCommandResult CmdWeatherAcp(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            if (args.Parsers[0].IsMissing)
            {
                wsysServer.autoChangePatterns = !wsysServer.autoChangePatterns;
            }
            else
            {
                wsysServer.autoChangePatterns = (bool)args[0];
            }

            return TextCommandResult.Success("Ok autochange weather patterns now " + (wsysServer.autoChangePatterns ? "on" : "off"));
        }

        private TextCommandResult CmdWeatherStoprain(TextCommandCallingArgs args)
        {
            var res  = RainStopFunc(args.Caller.Player, true);
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            wsysServer.broadCastConfigUpdate();
            return res;
        }

        private TextCommandResult CmdWeatherCloudypos(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            if (args.Parsers[0].IsMissing)
            {
                return TextCommandResult.Success("Cloud level rel = " + wsysServer.CloudLevelRel);
            }

            wsysServer.CloudLevelRel = (float)args.Parsers[0].GetValue();

            wsysServer.serverChannel.BroadcastPacket(new WeatherCloudYposPacket { CloudYRel = wsysServer.CloudLevelRel });

            return TextCommandResult.Success(string.Format("Cloud level rel {0:0.##} set. (y={1})", wsysServer.CloudLevelRel, (int)(wsysServer.CloudLevelRel*wsysServer.api.World.BlockAccessor.MapSizeY)));
        }

        private TextCommandResult CmdWeatherSetprecip(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            var level = (float)args.Parsers[0].GetValue();
            if (args.Parsers[0].IsMissing)
            {
                if (wsysServer.OverridePrecipitation == null)
                {
                    return TextCommandResult.Success("Currently no precipitation override active.");
                }
                return TextCommandResult.Success(string.Format("Override precipitation value is currently at {0}.", wsysServer.OverridePrecipitation));
            }

            wsysServer.OverridePrecipitation = level;

            wsysServer.serverChannel.BroadcastPacket(new WeatherConfigPacket() {
                OverridePrecipitation = wsysServer.OverridePrecipitation,
                RainCloudDaysOffset = wsysServer.RainCloudDaysOffset
            });

            return TextCommandResult.Success(string.Format("Ok precipitation set to {0}", level));
        }

        private TextCommandResult CmdWeatherSetprecipa(TextCommandCallingArgs args)
        {
            var wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            wsysServer.OverridePrecipitation = null;
            wsysServer.serverChannel.BroadcastPacket(new WeatherConfigPacket() {
                OverridePrecipitation = wsysServer.OverridePrecipitation,
                RainCloudDaysOffset = wsysServer.RainCloudDaysOffset
            });

            return TextCommandResult.Success("Ok auto precipitation on");
        }

        private TextCommandResult CmdPrecTestServerClimate(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player;
            var climate = (bool)args.Parsers[0].GetValue();
            var wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            var pos = player.Entity.Pos;

            var wdt = 400;
            var hourStep = 4f;
            var days = 1f;
            var posStep = 2f;

            var totaldays = api.World.Calendar.TotalDays;

            var conds = api.World.BlockAccessor.GetClimateAt(new BlockPos((int)pos.X, (int)pos.Y, (int)pos.Z), EnumGetClimateMode.WorldGenValues, totaldays);

            int offset = wdt / 2;
            int[] pixels;

            if (RuntimeEnv.OS != OS.Windows)
            {
                return TextCommandResult.Success("Command only supported on windows, try sub argument \"here\"");
            }

            var bmpgif =  new Bitmap(wdt, wdt);
            pixels = new int[wdt * wdt];

            using (var gif = new AnimatedGifCreator("precip.gif", 100, -1))
            {
                for (int i = 0; i < days * 24f; i++) {
                    if (climate)
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
            return TextCommandResult.Success("Ok exported");
        }

        private TextCommandResult CmdPrecTestServerHere(TextCommandCallingArgs args)
        {
            var wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            var pos = args.Caller.Player.Entity.Pos;

            var totaldays = api.World.Calendar.TotalDays;

            api.World.BlockAccessor.GetClimateAt(new BlockPos((int)pos.X, (int)pos.Y, (int)pos.Z), EnumGetClimateMode.WorldGenValues, totaldays);

            var wdt = 400;
            var offset = wdt / 2;
            SKBitmap bmp;
            int[] pixels;

            bmp = new SKBitmap(wdt, wdt);
            pixels = new int[wdt * wdt];
            var posStep = 3f;

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
            return TextCommandResult.Success("Ok exported");
        }

        private TextCommandResult CmdPrecTestServerPos(TextCommandCallingArgs args)
        {
            var wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            var pos = args.Caller.Player.Entity.Pos;
            var precip = wsys.GetPrecipitation(pos.X, pos.Y, pos.Z, api.World.Calendar.TotalDays);
            return TextCommandResult.Success("Prec here: " + precip);
        }

        private TextCommandResult CmdSnowAccumHere(TextCommandCallingArgs args)
        {
            var wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            float amount = (float)args.Parsers[0].GetValue();
            var player = args.Caller.Player;

            BlockPos plrPos = player.Entity.Pos.AsBlockPos;
            const int chunksize = GlobalConstants.ChunkSize;
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

            return TextCommandResult.Success("Ok, test snow accum gen complete");
        }

        private TextCommandResult CmdSnowAccumInfo(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            BlockPos plrPos = player.Entity.Pos.AsBlockPos;
            const int chunksize = GlobalConstants.ChunkSize;
            Vec2i chunkPos = new Vec2i(plrPos.X / chunksize, plrPos.Z / chunksize);
            IServerMapChunk mc = sapi.WorldManager.GetMapChunk(chunkPos.X, chunkPos.Y);

            double lastSnowAccumUpdateTotalHours = mc.GetModdata<double>("lastSnowAccumUpdateTotalHours");

            player.SendMessage(GlobalConstants.GeneralChatGroup, "lastSnowAccumUpdate: " + (api.World.Calendar.TotalHours - lastSnowAccumUpdateTotalHours) + " hours ago", EnumChatType.CommandSuccess);

            int regionX = (int)player.Entity.Pos.X / sapi.World.BlockAccessor.RegionSize;
            int regionZ = (int)player.Entity.Pos.Z / sapi.World.BlockAccessor.RegionSize;

            WeatherSystemServer wsysServer = sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            long index2d = wsysServer.MapRegionIndex2D(regionX, regionZ);
            wsysServer.weatherSimByMapRegion.TryGetValue(index2d, out WeatherSimulationRegion simregion);

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

                if(hoursnapshot == null) continue;
                float[] snowaccumdata = hoursnapshot.SnowAccumulationByRegionCorner.Data;
                for (int j = 0; j < snowaccumdata.Length; j++)
                {
                    sumdata[j] = GameMath.Clamp(sumdata[j] + snowaccumdata[j], -max, max);
                }

                lastSnowAccumUpdateTotalHours = Math.Max(lastSnowAccumUpdateTotalHours, hoursnapshot.TotalHours);
            }

            for (int j = 0; j < sumdata.Length; j++)
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, j + ": " + sumdata[j], EnumChatType.CommandSuccess);
            }
            return TextCommandResult.Success();
        }

        private TextCommandResult CmdSnowAccumProcesshere(TextCommandCallingArgs args)
        {
            var wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            var player = args.Caller.Player;
            var plrPos = player.Entity.Pos.AsBlockPos;
            const int chunksize = GlobalConstants.ChunkSize;
            var chunkPos = new Vec2i(plrPos.X / chunksize, plrPos.Z / chunksize);

            wsys.snowSimSnowAccu.AddToCheckQueue(chunkPos);
            return TextCommandResult.Success("Ok, added to check queue");
        }

        private TextCommandResult CmdSnowAccumOff(TextCommandCallingArgs args)
        {
            var wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            wsys.snowSimSnowAccu.ProcessChunks = false;
            return TextCommandResult.Success("Snow accum process chunks off");
        }

        private TextCommandResult CmdSnowAccumOn(TextCommandCallingArgs args)
        {
            var wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            wsys.snowSimSnowAccu.ProcessChunks = true;
            return TextCommandResult.Success("Snow accum process chunks on");
        }

        private TextCommandResult CmdWhenWillItStopRaining(TextCommandCallingArgs args)
        {
            return RainStopFunc(args.Caller.Player);
        }

        private TextCommandResult RainStopFunc(IPlayer player, bool skipForward = false)
        {
            WeatherSystemServer wsys = api.ModLoader.GetModSystem<WeatherSystemServer>();
            if (wsys.OverridePrecipitation != null)
            {
                return TextCommandResult.Success("Override precipitation set, rain pattern will not change. Fix by typing /weather setprecipa.");
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
                    return TextCommandResult.Success( string.Format("Ok, forwarded rain simulation by {0:0.##} days. The rain should stop for about {1:0.##} days now", firstRainLessDay, daysrainless), EnumChatType.CommandSuccess);
                }

                return TextCommandResult.Success(string.Format("In about {0:0.##} days the rain should stop for about {1:0.##} days", firstRainLessDay, daysrainless));
            }
            else
            {
                return TextCommandResult.Success("No rain less days found for the next 3 in-game weeks :O");
            }
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;
            this.capi.ChatCommands.Create("weather")
                .WithDescription("Show current weather info")
                .HandleWith(CmdWeatherClient);
        }

        private TextCommandResult CmdWeatherClient(TextCommandCallingArgs textCommandCallingArgs)
        {
            var text = GetWeatherInfo<WeatherSystemClient>(capi.World.Player);
            return TextCommandResult.Success(text);
        }

        private string GetWeatherInfo<T>(IPlayer player) where T: WeatherSystemBase
        {
            T wsys = api.ModLoader.GetModSystem<T>();

            Vec3d plrPos = player.Entity.SidedPos.XYZ;
            BlockPos pos = plrPos.AsBlockPos;

            var wreader = wsys.getWeatherDataReaderPreLoad();

            wreader.LoadAdjacentSimsAndLerpValues(plrPos, 1);

            int regionX = pos.X / api.World.BlockAccessor.RegionSize;
            int regionZ = pos.Z / api.World.BlockAccessor.RegionSize;

            long index2d = wsys.MapRegionIndex2D(regionX, regionZ);
            wsys.weatherSimByMapRegion.TryGetValue(index2d, out WeatherSimulationRegion weatherSim);
            if (weatherSim == null)
            {
                return "weatherSim is null. No idea what to do here";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Weather by region:"); // (lerp-lr: {0}, lerp-bt: {1}), wsys.lerpLeftRight.ToString("0.##"), wsys.lerpTopBot.ToString("0.##")));
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
