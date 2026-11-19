using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that broadcasts a message to all connected clients.
/// </summary>
public class DebugBroadcastAction(
    float weight,
    ICoreServerAPI sapi,
    EnumChatType chatType,
    string message
) : Action(weight)
{
    public override void Execute(float deltaTime)
    {
        sapi.BroadcastMessageToAllGroups(message, chatType);
    }
}
