using System;
using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[AssetSchemaMetadata("Timer", "Checks the current state of a named timer on the NPC.")]
public class TimerConditionDefinition : BaseConditionDefinition
{
    [JsonProperty(Required = Required.Always)]
    public string Timer { get; set; }

    [JsonProperty(Required = Required.Always)]
    [DefaultValue("Running")]
    public TimerState State { get; set; }

    public override ICondition Build(Entity entity)
    {
        if (!EntityBehaviorTimers.TryGet(entity, out var entityTimers))
            throw new ArgumentException("Missing required EntityBehaviorTimers");

        int timerHandle = entityTimers.RequestTimer(Timer);
        return new TimerCondition(Invert, entityTimers, timerHandle, State);
    }
}
