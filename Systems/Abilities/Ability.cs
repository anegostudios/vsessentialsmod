using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

public class Ability(
    Entity user,
    EntityBehaviorTimers entityTimers,
    int cooldownTimerHandle,
    AnimationMetaData animation,
    ITargetSelector targetSelector,
    AbilityAction action
) : IAssetInstance
{
    private bool hasRegisteredAnimationCallback = false;

    private void RegisterAnimationCallback()
    {
        if (hasRegisteredAnimationCallback) return;

        float damageAtFrame = animation.Attributes?["damageAtFrame"].AsFloat(0) ?? 0;

        user.AnimManager.RegisterFrameCallback(new AnimFrameCallback
        {
            Animation = animation.Animation,
            Frame = damageAtFrame,
            Callback = OnActionFrame
        });

        hasRegisteredAnimationCallback = true;
        action.OnFinish += () => hasRegisteredAnimationCallback = false;
    }

    private bool TryConsumeCooldown()
    {
        if (entityTimers == null)
            return true;

        var cooldownState = entityTimers.GetTimerState(cooldownTimerHandle);
        if (cooldownState == TimerState.Running)
            return false;

        entityTimers.ResetTimer(cooldownTimerHandle);
        entityTimers.StartTimer(cooldownTimerHandle);

        return true;
    }

    public bool Execute()
    {
        if (!TryConsumeCooldown())
            return false;

        action.OnAbilityExecute();

        RegisterAnimationCallback();

        if (!user.AnimManager.IsAnimationActive(animation.Animation))
        {
            user.AnimManager.StartAnimation(animation);
        }

        return true;
    }

    private void OnActionFrame() => action.Run(targetSelector);
}
