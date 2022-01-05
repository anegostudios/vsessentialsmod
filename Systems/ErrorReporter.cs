using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

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

        object logEntiresLock = new object();
        List<string> logEntries = new List<string>();


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

        public override void Start(ICoreAPI api)
        {
            this.api = api;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;

            api.RegisterCommand("errorreporter", "Reopens the error reporting dialog", "[on|off]", ClientCmdErrorRep);

            api.Event.LevelFinalize += OnClientReady;
            api.Network.RegisterChannel("errorreporter")
               .RegisterMessageType(typeof(ServerLogEntries))
               .SetMessageHandler<ServerLogEntries>(OnServerLogEntriesReceived)
            ;
        }

        private void ClientCmdErrorRep(int groupId, CmdArgs args)
        {
            if (dialog != null && dialog.IsOpened())
            {
                dialog.TryClose();
                return;
            }

            ShowDialog();
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
                logEntries.AddRange(msg.LogEntries);
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

                List<string> printedEntries = logEntries;
                if (logEntries.Count > 180)
                {
                    printedEntries = logEntries.Take(140).ToList();
                    printedEntries.Add(string.Format("...{0} more", logEntries.Count - 0));
                }

                dialog = new GuiDialogLogViewer(string.Join("\n", printedEntries), capi);
            }

            dialog.TryOpen();
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            api.RegisterCommand("errorreporter", "Toggles on/off the error reporting dialog on startup", "[on|off]", OnCmdErrRep, Privilege.controlserver);

            serverChannel =
               api.Network.RegisterChannel("errorreporter")
               .RegisterMessageType(typeof(ServerLogEntries))
            ;

            api.Event.PlayerJoin += OnPlrJoin;
        }

        private void OnPlrJoin(IServerPlayer byPlayer)
        {
            string val = "0";
            byPlayer.ServerData.CustomPlayerData.TryGetValue("errorReporting", out val);
            if (val == "1" && logEntries.Count > 0)
            {
                lock (logEntiresLock)
                {
                    serverChannel.SendPacket(new ServerLogEntries() { LogEntries = logEntries.ToArray() }, byPlayer);
                }
            }
        }

        private void OnCmdErrRep(IServerPlayer player, int groupId, CmdArgs args)
        {
            bool on = (bool)args.PopBool(true);
            player.ServerData.CustomPlayerData["errorReporting"] = on ? "1" : "0";
            player.SendMessage(groupId, Lang.Get("Error reporting now {0}", on ? "on" : "off"), EnumChatType.Notification);
        }
        

        private void Logger_EntryAdded(EnumLogType logType, string message, params object[] args)
        {
            if (logType == EnumLogType.Error || logType == EnumLogType.Fatal || logType == EnumLogType.Warning)
            {
                lock (logEntiresLock)
                {
                    logEntries.Add(string.Format("[{0} {1}] {2}", api.Side, logType, string.Format(message, args)));
                }
            }
        }
    }
}
