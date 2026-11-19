using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

public class EntityBehaviorDamageReactions : EntityBehavior
{
    enum EnumReactionDirection
    {
        Front,
        FrontRight,
        Right,
        BackRight,
        Back,
        BackLeft,
        Left,
        FrontLeft
    }

    string? baseHurtAnimation;
    string? baseDeathAnimation;
    string?[]? directionalHurtAnimationCodes;
    string?[]? directionalDeathAnimationCodes;
    AnimationMetaData?[]? directionalHurtAnimations;
    AnimationMetaData?[]? directionalDeathAnimations;
    EnumReactionDirection? pendingDirection;
    EnumReactionDirection? fallbackDirection;
    bool directionalDeathActive;

    public EntityBehaviorDamageReactions(Entity entity) : base(entity) { }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        var directions = Enum.GetValues<EnumReactionDirection>();
        var hurtAnimations = attributes["hurtAnimations"];
        var deathAnimations = attributes["deathAnimations"];

        if (hurtAnimations.Exists) directionalHurtAnimationCodes = new string?[directions.Length];
        if (deathAnimations.Exists) directionalDeathAnimationCodes = new string?[directions.Length];

        if (directionalHurtAnimationCodes != null || directionalDeathAnimationCodes != null)
        {
            foreach (var direction in directions)
            {
                string directionCode = direction.ToString().ToLowerInvariant();
                directionalHurtAnimationCodes?[(int)direction] = hurtAnimations[directionCode].AsString();
                directionalDeathAnimationCodes?[(int)direction] = deathAnimations[directionCode].AsString();
            }
        }

        baseHurtAnimation = properties.Client.AnimationsByMetaCode.TryGetValue("hurt")?.Animation;
        baseDeathAnimation = properties.Client.AnimationsByMetaCode.TryGetValue("die")?.Animation;

        entity.AnimManager.OnStartAnimation += onStartAnimation;
        entity.AnimManager.OnAnimationReceived += onAnimationReceived;
        entity.Api.Event.OnEntityDeath += onEntityDeath;
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        directionalHurtAnimations = directionalHurtAnimationCodes?.Select(resolveAnimation).ToArray();
        directionalDeathAnimations = directionalDeathAnimationCodes?.Select(resolveAnimation).ToArray();
    }

    AnimationMetaData? resolveAnimation(string? animationCode)
    {
        if (animationCode == null || !entity.Properties.Client.AnimationsByMetaCode.TryGetValue(animationCode, out var animation)) return null;

        if (entity.Properties.Client.LoadedShapeForEntity?.AnimationsByCrc32.ContainsKey(AnimationMetaData.GetCrc32(animation.Animation)) != true) return null;
        return animation;
    }

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        fallbackDirection = null;
        pendingDirection = damage > 0.05f && damageSource.Type != EnumDamageType.Heal && damageSource.TicksPerDuration < 2
            ? getReactionDirection(damageSource, out fallbackDirection)
            : null;
    }

    void onEntityDeath(Entity diedEntity, DamageSource? damageSource)
    {
        if (diedEntity != entity) return;

        pendingDirection = getReactionDirection(damageSource, out fallbackDirection);
    }

    bool onStartAnimation(ref AnimationMetaData animationMeta, ref EnumHandling handling)
    {
        if (!entity.Alive && animationMeta.Code == "hurt")
        {
            handling = EnumHandling.PreventSubsequent;
            return false;
        }

        if (animationMeta.Code == "die")
        {
            if (directionalDeathActive || hasActiveDirectionalDeathAnimation())
            {
                directionalDeathActive = true;
                handling = EnumHandling.PreventSubsequent;
                return false;
            }

            if (pendingDirection.HasValue
                && baseDeathAnimation != null
                && baseDeathAnimation.Equals(animationMeta.Animation, StringComparison.OrdinalIgnoreCase))
            {
                var deathAnimation = directionalDeathAnimations?[(int)pendingDirection.Value];
                if (fallbackDirection.HasValue) deathAnimation ??= directionalDeathAnimations?[(int)fallbackDirection.Value];
                pendingDirection = fallbackDirection = null;
                if (deathAnimation != null)
                {
                    animationMeta = deathAnimation;
                    directionalDeathActive = true;
                }
            }

            animationMeta = getDeathAnimation(animationMeta);
            return false;
        }

        if (!pendingDirection.HasValue || baseHurtAnimation == null) return false;
        if (!baseHurtAnimation.Equals(animationMeta.Animation, StringComparison.OrdinalIgnoreCase)) return false;

        var hurtAnimation = directionalHurtAnimations?[(int)pendingDirection.Value];
        if (fallbackDirection.HasValue) hurtAnimation ??= directionalHurtAnimations?[(int)fallbackDirection.Value];
        pendingDirection = fallbackDirection = null;
        if (hurtAnimation == null) return false;

        animationMeta = hurtAnimation;
        return false;
    }

    bool onAnimationReceived(ref AnimationMetaData animationMeta, ref EnumHandling handling)
    {
        bool isDirectionalDeathAnimation = animationMeta.Code != "die";
        if (isDirectionalDeathAnimation)
        {
            if (directionalDeathAnimationCodes == null) return true;

            bool isDeathAnimation = false;
            foreach (string? code in directionalDeathAnimationCodes)
            {
                if (code == null) continue;
                if (!animationMeta.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) continue;

                isDeathAnimation = true;
                break;
            }

            if (!isDeathAnimation) return true;
        }

        var deathAnimation = getDeathAnimation(animationMeta);
        if (isDirectionalDeathAnimation) directionalDeathActive = true;

        if (baseDeathAnimation != null
            && !baseDeathAnimation.Equals(deathAnimation.Animation, StringComparison.OrdinalIgnoreCase)
            && entity.AnimManager.ActiveAnimationsByAnimCode.TryGetValue(baseDeathAnimation)?.Code == "die")
        {
            entity.AnimManager.StopAnimation(baseDeathAnimation);
            entity.AnimManager.Animator?.GetAnimationState(baseDeathAnimation)?.Stop();
        }

        animationMeta = deathAnimation;
        return false;
    }

    AnimationMetaData getDeathAnimation(AnimationMetaData animation)
    {
        if (animation.Code == "die")
        {
            foreach (var activeAnimation in entity.AnimManager.ActiveAnimationsByAnimCode.Values)
            {
                if (activeAnimation.Code == "die") return activeAnimation;
            }
            return animation;
        }

        if (entity.AnimManager.ActiveAnimationsByAnimCode.TryGetValue(animation.Animation, out var activeDeathAnimation) && activeDeathAnimation.Code == "die") return activeDeathAnimation;

        animation = animation.Clone();
        animation.Code = "die";
        return animation;
    }

    bool hasActiveDirectionalDeathAnimation()
    {
        if (baseDeathAnimation == null) return false;

        foreach (var animation in entity.AnimManager.ActiveAnimationsByAnimCode.Values)
        {
            if (animation.Code != "die") continue;
            if (!baseDeathAnimation.Equals(animation.Animation, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    EnumReactionDirection? getReactionDirection(DamageSource? damageSource, out EnumReactionDirection? fallbackDirection)
    {
        fallbackDirection = null;
        if (damageSource == null) return null;
        var sourcePos = damageSource.GetCauseEntity()?.Pos.XYZ ?? damageSource.GetSourcePosition();
        if (sourcePos == null) return null;

        double offsetX = sourcePos.X - entity.Pos.X;
        double offsetZ = sourcePos.Z - entity.Pos.Z;
        var forward = EntityPos.GetViewVector(0, entity.Pos.Yaw);
        double rotatedX = offsetX * forward.Z - offsetZ * forward.X;
        double rotatedZ = offsetX * forward.X + offsetZ * forward.Z;

        fallbackDirection = Math.Abs(rotatedX) > Math.Abs(rotatedZ)
            ? rotatedX > 0 ? EnumReactionDirection.Right : EnumReactionDirection.Left
            : rotatedZ > 0 ? EnumReactionDirection.Back : EnumReactionDirection.Front;

        return (EnumReactionDirection?)Cardinal.FromVector(rotatedX, 0, rotatedZ)?.Index;
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        entity.AnimManager.OnStartAnimation -= onStartAnimation;
        entity.AnimManager.OnAnimationReceived -= onAnimationReceived;
        entity.Api.Event.OnEntityDeath -= onEntityDeath;
    }

    public override void OnEntityRevive()
    {
        pendingDirection = fallbackDirection = null;
        directionalDeathActive = false;
    }

    public override string PropertyName() => "damagereactions";
}
