using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Condition that checks the state of a named timer.
/// </summary>
public class TimerCondition(
    bool invert,
    EntityBehaviorTimers entityTimers,
    int timerHandle,
    TimerState timerState
) : BaseCondition(invert)
{
    public override float Evaluate()
    {
        return base.Evaluate(entityTimers.GetTimerState(timerHandle) == timerState ? 1f : 0f);
    }
}
