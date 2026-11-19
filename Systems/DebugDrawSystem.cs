using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent;

public class DebugDrawSystem : ModSystem
{
    private const string NetworkChannelId = "debug-draw";

    private ICoreClientAPI capi;

    private IServerNetworkChannel serverChannel;

    private DebugShapeRenderer debugShapeRenderer = null;

    private long currentDrawRequestHandle = -1;

    public override bool ShouldLoad(EnumAppSide forSide) => true;

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType(typeof(DebugDrawLine))
            .RegisterMessageType(typeof(DebugDrawCone))
            .RegisterMessageType(typeof(DebugDrawSphere))
            .RegisterMessageType(typeof(DebugDrawOBB));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        api.Network.GetChannel(NetworkChannelId)
            .SetMessageHandler<DebugDrawLine>(HandleDebugDrawLine)
            .SetMessageHandler<DebugDrawCone>(HandleDebugDrawCone)
            .SetMessageHandler<DebugDrawSphere>(HandleDebugDrawSphere)
            .SetMessageHandler<DebugDrawOBB>(HandleDebugDrawOBB);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverChannel = api.Network.GetChannel(NetworkChannelId);
    }

    private long AllocateHandle()
    {
        return ++currentDrawRequestHandle;
    }

    // TODO: Support client->server handle requests
    public long RequestHandle()
    {
        return AllocateHandle();
    }

    public List<long> RequestHandles(int count)
    {
        List<long> handles = new(count);
        for (int i = 0; i < count; ++i)
            handles.Add(AllocateHandle());

        return handles;
    }

    public void RequestDebugDrawLine(DebugDrawLine packet)
    {
        if (serverChannel != null) serverChannel.BroadcastPacket(packet);
        else if (capi != null) HandleDebugDrawLine(packet);
    }

    public void RequestDebugDrawCone(DebugDrawCone packet)
    {
        if (serverChannel != null) serverChannel.BroadcastPacket(packet);
        else if (capi != null) HandleDebugDrawCone(packet);
    }

    public void RequestDebugDrawSphere(DebugDrawSphere packet)
    {
        if (serverChannel != null) serverChannel.BroadcastPacket(packet);
        else if (capi != null) HandleDebugDrawSphere(packet);
    }

    public void RequestDebugDrawOBB(DebugDrawOBB packet)
    {
        if (serverChannel != null) serverChannel.BroadcastPacket(packet);
        else if (capi != null) HandleDebugDrawOBB(packet);
    }

    private void RegisterRenderer()
    {
        debugShapeRenderer = new DebugShapeRenderer(capi);
        capi.Event.RegisterRenderer(debugShapeRenderer, EnumRenderStage.Opaque);
    }

    public override void Dispose()
    {
        debugShapeRenderer?.Dispose();
        base.Dispose();
    }

    private void HandleDebugDrawLine(DebugDrawLine packet)
    {
        if (debugShapeRenderer == null) RegisterRenderer();
        debugShapeRenderer.RequestDraw(packet);
    }

    private void HandleDebugDrawCone(DebugDrawCone packet)
    {
        if (debugShapeRenderer == null) RegisterRenderer();
        debugShapeRenderer.RequestDraw(packet);
    }

    private void HandleDebugDrawSphere(DebugDrawSphere packet)
    {
        if (debugShapeRenderer == null) RegisterRenderer();
        debugShapeRenderer.RequestDraw(packet);
    }

    private void HandleDebugDrawOBB(DebugDrawOBB packet)
    {
        if (debugShapeRenderer == null) RegisterRenderer();
        debugShapeRenderer.RequestDraw(packet);
    }
}
