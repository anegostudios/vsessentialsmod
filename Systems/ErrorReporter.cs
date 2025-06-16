using System;
using ProtoBuf;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ServerLogEntries
    {
        public string[] LogEntries;
    }

    public class ErrorReporter : ModSystem
    {
        ICoreAPI api;

        bool clientEnabled;
        int readyFlags;
        ICoreClientAPI capi;
        GuiDialog dialog;

        IServerNetworkChannel serverChannel;

        private const int maxLogEntries = 180;

        object logEntiresLock = new();
        LimitedList<string> logEntries = new(maxLogEntries);

        public override double ExecuteOrder()
        {
            return 0;
        }

        public override bool ShouldLoad(EnumAppSide side)
        {
            return true;
        }

        public override void StartPre(ICoreAPI api)
        {
            this.api = api;
            api.World.Logger.EntryAdded += Logger_EntryAdded;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.ChatCommands.Create("errorreporter")
                .WithDescription("Toggles on/off the error reporting dialog on startup")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.Bool("activate"))
                .HandleWith(OnCmdErrRep);

            serverChannel =
                api.Network.RegisterChannel("errorreporter")
                    .RegisterMessageType(typeof(ServerLogEntries))
                ;

            api.Event.PlayerJoin += OnPlrJoin;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;

            api.ChatCommands.Create("errorreporter")
                .WithDescription("Reopens the error reporting dialog")
                .HandleWith(ClientCmdErrorRep);


            api.Event.LevelFinalize += OnClientReady;
            api.Network.RegisterChannel("errorreporter")
               .RegisterMessageType(typeof(ServerLogEntries))
               .SetMessageHandler<ServerLogEntries>(OnServerLogEntriesReceived)
            ;
        }

        private TextCommandResult ClientCmdErrorRep(TextCommandCallingArgs textCommandCallingArgs)
        {
            if (dialog != null && dialog.IsOpened())
            {
                dialog.TryClose();
            }
            else
            {
                ShowDialog();
            }

            return TextCommandResult.Success();
        }

        private void OnClientReady()
        {
            readyFlags++;
            if (readyFlags == 2 && logEntries.Count > 0) ShowDialog();
        }

        private void OnServerLogEntriesReceived(ServerLogEntries msg)
        {
            clientEnabled = true;
            readyFlags++;
            lock (logEntiresLock)
            {
                foreach (var entry in msg.LogEntries)
                {
                    logEntries.Add(entry);
                }
            }

            if (readyFlags == 2 && logEntries.Count > 0) ShowDialog();
        }

        private void ShowDialog()
        {
            lock (logEntiresLock)
            {
                if (!clientEnabled)
                {
                    logEntries.Clear();
                    return;
                }

                var printedEntries = logEntries.ToList();
                if (logEntries.Count > maxLogEntries)
                {
                    printedEntries = logEntries.Take(maxLogEntries).ToList();
                    printedEntries.Add(string.Format("...{0} more", logEntries.Count - 0));
                }

                dialog = new GuiDialogLogViewer(string.Join("\n", printedEntries), capi);
            }

            dialog.TryOpen();
        }

        private void OnPlrJoin(IServerPlayer byPlayer)
        {
            byPlayer.ServerData.CustomPlayerData.TryGetValue("errorReporting", out string val);
            if (val == "1" && logEntries.Count > 0)
            {
                lock (logEntiresLock)
                {
                    serverChannel.SendPacket(new ServerLogEntries() { LogEntries = logEntries.ToArray() }, byPlayer);
                }
            }
        }

        private TextCommandResult OnCmdErrRep(TextCommandCallingArgs args)
        {
            var on = (bool)args.Parsers[0].GetValue();
            var player = args.Caller.Player as IServerPlayer;
            player.ServerData.CustomPlayerData["errorReporting"] = on ? "1" : "0";
            return TextCommandResult.Success(Lang.Get("Error reporting now {0}", on ? "on" : "off"));
        }


        private void Logger_EntryAdded(EnumLogType logType, string message, params object[] args)
        {
            if (logType == EnumLogType.Error ||
                logType == EnumLogType.Fatal ||
                logType == EnumLogType.Warning)
            {
                string log;
                try
                {
                    log = string.Format("[{0} {1}] {2}", api.Side, logType, string.Format(message, args));
                }
                catch (Exception)
                {
                    log = string.Format("[{0} {1}] {2}", api.Side, logType, "Error reporter failed formatting for \"" + message + "\"");
                }

                lock (logEntiresLock)
                {
                    logEntries.Add(log);
                }
            }
        }
    }
}
