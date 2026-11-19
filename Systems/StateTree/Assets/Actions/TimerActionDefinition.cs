using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

public enum TimerOperation
{
    Start,
    Pause,
    Reset,
}

[AssetSchemaMetadata("Update timer", "Updates a named timer. Supports Start, Pause, and Reset operations.")]
public class TimerActionDefinition : ActionDefinition
{
    [JsonProperty(Required = Required.Always)]
    public string Timer { get; set; }

    [JsonProperty(Required = Required.Always)]
    public TimerOperation Operation { get; set; }

    [JsonProperty]
    public Variable<float> Duration { get; set; }

    public override Action Build(Entity entity)
    {
        if (!EntityBehaviorTimers.TryGet(entity, out EntityBehaviorTimers entityTimers))
            return null;

        int timerHandle = entityTimers.RequestTimer(Timer);
        return Build(new TimerAction(Weight, entityTimers, timerHandle, Operation, Duration.GetValue(entity)));
    }
}
