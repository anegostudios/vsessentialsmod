using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Updates a named timer. Supports start, pause, and reset operations.
/// </summary>
public class TimerAction(
    float weight,
    EntityBehaviorTimers entityTimers,
    int timerHandle,
    TimerOperation operation,
    float duration
) : Action(weight)
{
    public override void Execute(float deltaTime)
    {
        switch (operation)
        {
            case TimerOperation.Start:
                if (duration > 0.0f)
                {
                    entityTimers.StartTimer(timerHandle, duration);
                }
                else
                {
                    entityTimers.StartTimer(timerHandle);
                }
                break;
            case TimerOperation.Pause:
                entityTimers.PauseTimer(timerHandle);
                break;
            case TimerOperation.Reset:
                entityTimers.ResetTimer(timerHandle);
                break;
        }
    }
}
