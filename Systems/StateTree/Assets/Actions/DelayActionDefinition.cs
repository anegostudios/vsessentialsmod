using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Delay", "Delays subsequent actions when using ActionListMode=Sequence.")]
public class DelayActionDefinition : ActionDefinition
{
    [JsonProperty(Required = Required.Always)]
    [AssetSchemaMetadata(null, "Time until the next action can execute (in seconds).")]
    [DefaultValue(0.0f)]
    public Variable<float> Duration { get; set; }

    public override Action Build(Entity entity)
    {
        if (!EntityBehaviorTimers.TryGet(entity, out EntityBehaviorTimers entityTimers))
            return null;

        int timerHandle = entityTimers.RequestTimer(Duration.GetValue(entity));
        return new DelayAction(Weight, entityTimers, timerHandle);
    }
}
