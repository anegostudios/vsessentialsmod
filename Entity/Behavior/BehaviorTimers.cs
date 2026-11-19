using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

public enum TimerState
{
    Running,
    Paused,
    Stopped,
}

public class Timer
{
    public TimerState State;
    public float Duration;
    public float Remaining;
    public string? Name;

    public void Start() => State = TimerState.Running;

    public void Start(float duration)
    {
        State = TimerState.Running;
        Duration = duration;
        Remaining = duration;
    }

    public void Pause() => State = TimerState.Paused;

    public void Reset()
    {
        State = TimerState.Stopped;
        Remaining = Duration;
    }
}

/// <summary>
/// Enables an entity to use named (accessible by name) or anonymous (accessible by handle only) timers.
/// Timers can be released by owning code so they can be re-used.
/// </summary>
/// <param name="entity"></param>
public class EntityBehaviorTimers(Entity entity) : EntityBehavior(entity)
{
    private readonly List<Timer?> timers = [];
    private readonly Stack<int> freeHandles = new();
    private readonly Dictionary<string, int> namedTimers = new();

    /// <summary>
    /// Requests a new anonymous timer.
    /// </summary>
    /// <param name="duration">The duration this timer should have</param>
    /// <returns>The timer handle</returns>
    public int RequestTimer(float duration)
    {
        return AllocateTimer(duration, null);
    }

    /// <summary>
    /// Requests a named timer. If it already exists, returns the existing handle.
    /// </summary>
    /// <param name="name">The unique timer name</param>
    /// <param name="duration">The duration this timer should have (only applies to new timers)</param>
    /// <returns>The timer handle</returns>
    public int RequestTimer(string name, float duration = 0.0f)
    {
        if (namedTimers.TryGetValue(name, out int existingHandle))
        {
            return existingHandle;
        }

        int handle = AllocateTimer(duration, name);
        namedTimers[name] = handle;
        return handle;
    }

    private int AllocateTimer(float duration, string? name)
    {
        Timer timer = new Timer
        {
            State = TimerState.Stopped,
            Duration = duration,
            Remaining = duration,
            Name = name
        };

        if (freeHandles.Count > 0)
        {
            int handle = freeHandles.Pop();
            timers[handle] = timer;
            return handle;
        }

        timers.Add(timer);
        return timers.Count - 1;
    }

    private Timer GetTimer(int index)
    {
        if (index < 0 || index >= timers.Count || timers[index] == null)
        {
            throw new KeyNotFoundException($"Timer handle {index} is not valid.");
        }

        return timers[index]!;
    }

    public TimerState GetTimerState(int index) => GetTimer(index).State;
    public float GetTimerRemaining(int index) => GetTimer(index).Remaining;

    public void StartTimer(int index) => GetTimer(index).Start();
    public void StartTimer(int index, float duration) => GetTimer(index).Start(duration);
    public void PauseTimer(int index) => GetTimer(index).Pause();
    public void ResetTimer(int index) => GetTimer(index).Reset();

    public void ReleaseTimer(int index)
    {
        if (index < 0 || index >= timers.Count)
        {
            return;
        }

        Timer? timer = timers[index];
        if (timer == null)
        {
            return;
        }

        if (timer.Name != null)
        {
            namedTimers.Remove(timer.Name);
        }

        timers[index] = null;
        freeHandles.Push(index);
    }

    public bool ReleaseTimer(string name)
    {
        if (!namedTimers.TryGetValue(name, out int handle))
        {
            return false;
        }

        ReleaseTimer(handle);
        return true;
    }

    public override string PropertyName() => "timers";

    public override void OnGameTick(float deltaTime)
    {
        foreach (var timer in timers.Where(timer => timer is { State: TimerState.Running }))
        {
            timer.Remaining -= deltaTime;
            if (timer.Remaining <= 0.0f)
            {
                timer.State = TimerState.Stopped;
            }
        }
    }

    public static bool TryGet(Entity entity, [NotNullWhen(true)] out EntityBehaviorTimers? behavior)
    {
        behavior = entity.GetBehavior<EntityBehaviorTimers>();
        if (behavior == null)
        {
            entity.World.Logger.Warning($"Failed to retrieve EntityBehaviorTimers for entity '{entity.Code}'");
            return false;
        }

        return true;
    }
}
