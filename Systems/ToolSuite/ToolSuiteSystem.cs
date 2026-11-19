using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace Vintagestory.GameContent;

/// <summary>
/// Provides real-time integration for Tool Suite modules.
/// </summary>
public class ToolSuiteSystem : ModSystem
{
    // Incoming operations
    private const string SubscribeOperation = "StateTreeSubscribe";

    // Outgoing operations
    private const string ListOperation = "StateTreeList";

    private ICoreServerAPI sapi;

    private StateTreeSystem stateTreeSystem;

    private readonly DebugServer debugServer = new();
    private IPlayer? debugUser;
    private StateTree.StateTree targetStateTree;

    private const string StateTreeMMFName = "vs-state-tree";
    private const long StateTreeMMFSize = 256;
    private MemoryMappedFile? stateTreeMMF;

    private long tickListenerId = -1;

    public override void StartServerSide(ICoreServerAPI api)
    {
        var config = VSEssentialsModServerConfig.LoadOrCreate(api);
        if (!config.EnableToolSuiteIntegration)
        {
            return;
        }

        sapi = api;

        try
        {
            stateTreeMMF = api.PlatformUtils.CreateOrOpenMemoryMappedFile(StateTreeMMFName, StateTreeMMFSize);
        }
        catch (NotSupportedException)
        {
            api.Logger.Warning("Memory mapped files are not supported on this platform; Tool suite integration is disabled");
            return;
        }

        stateTreeSystem = api.ModLoader.GetModSystem<StateTreeSystem>();

        debugServer.OnClientConnected += OnClientConnected;
        debugServer.OnClientDisconnected += OnClientDisconnected;
        debugServer.OnReceiveMessage += OnReceiveMessage;

        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands
            .Create("ts")
            .WithDescription("Tool Suite commands")
            .RequiresPrivilege(Privilege.chat)
            .RequiresPlayer()
            .BeginSub("debug")
                .BeginSub("start")
                    .WithDesc("Start the local debugging server")
                    .WithArgs(parsers.OptionalInt("port"))
                    .HandleWith(StartDebugServer)
                .EndSub()
                .BeginSub("stop")
                    .WithDesc("Stop the local debugging server")
                    .HandleWith(StopDebugServer)
                .EndSub()
            .EndSub();

        api.Logger.Event("Tool Suite integration enabled");
    }

    public override void Dispose()
    {
        base.Dispose();
        debugServer.Stop();
        debugUser = null;
        stateTreeMMF?.Dispose();
    }

    private void OnGameTick(float dt)
    {
        if (stateTreeMMF == null) return;
        using MemoryMappedViewAccessor accessor = stateTreeMMF.CreateViewAccessor();
        accessor.Write(0, targetStateTree.SelectedNodeIndices.Count);
        accessor.WriteArray(sizeof(int), targetStateTree.SelectedNodeIndices.ToArray(), 0, targetStateTree.SelectedNodeIndices.Count);
    }

    private TextCommandResult StartDebugServer(TextCommandCallingArgs args)
    {
        if (debugServer.IsRunning)
            return TextCommandResult.Error("Debug server is already running");

        const string host = "localhost";
        var port = (int)(args[0] is int p && p != 0 ? (object)p : 8989);

        debugServer.Start(host, port);
        debugUser = args.Caller.Player;

        return TextCommandResult.Success($"Debug server started on {host}:{port}");
    }

    private TextCommandResult StopDebugServer(TextCommandCallingArgs args)
    {
        if (!debugServer.IsRunning)
            return TextCommandResult.Error("Debug server is not running");

        debugServer.Stop();
        debugUser = null;

        return TextCommandResult.Success("Debug server stopped");
    }

    private void OnClientConnected(StreamWriter client)
    {
        sapi.SendMessage(debugUser, GlobalConstants.GeneralChatGroup, "Debug client connected", EnumChatType.Notification);

        foreach (var (id, entities) in stateTreeSystem.Entities)
        {
            var entityIds = string.Join(",", entities.Select(e => e.EntityId));
            client.WriteLineAsync($"{ListOperation}:{id}:{entityIds}");
        }
    }

    private void OnReceiveMessage(StreamWriter client, string message)
    {
        var parts = message.Split(':');
        var op = parts[0];

        switch (op)
        {
            case SubscribeOperation:
                var entityId = parts[1];
                foreach (var entities in stateTreeSystem.Entities.Values)
                {
                    foreach (var entity in entities)
                    {
                        if (entity.EntityId == entityId.ToLong())
                        {
                            var stateTreeBehavior = entity.GetBehavior<EntityBehaviorStateTree>();
                            targetStateTree = stateTreeBehavior.PrimaryStateTree;
                            sapi.Event.EnqueueMainThreadTask(EnableTickListener, "toolsuite:enableticklistener");
                            return;
                        }
                    }
                }
                break;
        }
    }

    private void OnClientDisconnected(StreamWriter client)
    {
        sapi.SendMessage(debugUser, GlobalConstants.GeneralChatGroup, "Debug client disconnected", EnumChatType.Notification);
        sapi.Event.EnqueueMainThreadTask(DisableTickListener, "toolsuite:disableticklistener");
    }

    private void EnableTickListener()
    {
        if (tickListenerId > -1) return;
        tickListenerId = sapi.Event.RegisterGameTickListener(OnGameTick, 10);
    }

    private void DisableTickListener()
    {
        if (tickListenerId == -1) return;
        sapi.Event.UnregisterGameTickListener(tickListenerId);
        tickListenerId = -1;
    }
}
