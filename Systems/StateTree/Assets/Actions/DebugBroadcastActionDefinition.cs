using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Debug broadcast", "Broadcasts a message to the chat window. Intended for debugging.")]
public class DebugBroadcastActionDefinition : ActionDefinition
{
    [JsonProperty]
    [DefaultValue("Notification")]
    public EnumChatType ChatType { get; set; } = EnumChatType.Notification;

    [JsonProperty(Required = Required.Always)]
    public string Message { get; set; }

    public override Action Build(Entity entity)
    {
        var sapi = entity.Api as ICoreServerAPI;
        var message = $"{entity.GetName()}: {Message}";
        return new OnceAction(Weight, new DebugBroadcastAction(Weight, sapi, ChatType, message));
    }
}
