using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Action that simulates a delay using a timer. Actions that come after this one will be blocked as long as
/// ActionListType is set to Sequence.
/// </summary>
/// <param name="timers">The entity's Timers behavior</param>
/// <param name="timerHandle">The timer handle</param>
public class DelayAction(float weight, EntityBehaviorTimers timers, int timerHandle) : Action(weight)
{
    public override void Execute(float deltaTime)
    {
        TimerState state = timers.GetTimerState(timerHandle);
        float remaining = timers.GetTimerRemaining(timerHandle);
        if (state == TimerState.Stopped && remaining > 0)
        {
            timers.StartTimer(timerHandle);
        }
    }

    public override bool HasFinished()
    {
        float remaining = timers.GetTimerRemaining(timerHandle);
        return remaining <= 0;
    }

    public override void Reset()
    {
        timers.ResetTimer(timerHandle);
    }
}
