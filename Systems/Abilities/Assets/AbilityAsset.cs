using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

[JsonObject(MemberSerialization.OptIn)]
public class AbilityAsset : IAbilityAsset
{
    [JsonProperty(Required = Required.Always)]
    public string Id { get; set; }

    [JsonProperty(Required = Required.Always)]
    public string Animation { get; set; }

    [JsonProperty(Required = Required.Always)]
    public float Cooldown { get; set; }

    [JsonProperty(Required = Required.Always)]
    public ITargetSelectorDefinition TargetSelector { get; set; }

    [JsonProperty(Required = Required.Always)]
    public IActionDefinition Action { get; set; }

    [JsonProperty(Required = Required.Always)]
    public List<IEffectDefinition> OnHitEffects { get; set; }

    [JsonProperty]
    public bool EnableDebugDraw { get; set; }

    protected (EntityBehaviorTimers, int) InitTimers(Entity entity, JsonObject entityJson)
    {
        if (!EntityBehaviorTimers.TryGet(entity, out var entityTimers))
            return (null, 0);

        var cooldown = entityJson["Cooldown"].AsFloat(Cooldown);
        var cooldownTimerHandle = entityTimers.RequestTimer($"cooldown:{Id}", cooldown);

        return (entityTimers,  cooldownTimerHandle);
    }

    protected AnimationMetaData InitAnimation(Entity entity, JsonObject entityJson)
    {
        var animation = entityJson["Animation"].AsString(Animation);
        return entity.Properties.Client.AnimationsByMetaCode.GetValueOrDefault(animation).Init();
    }

    protected IList<IEffect> InitEffects(Entity entity) => OnHitEffects.Select(def => def.Build(entity)).ToList();

    protected DebugDrawSystem InitDebugDrawSystem(Entity entity, JsonObject entityJson)
    {
        var enableDebugDraw = entityJson["EnableDebugDraw"].AsBool(EnableDebugDraw);
        return enableDebugDraw
            ? entity.World.Api.ModLoader.GetModSystem<DebugDrawSystem>()
            : null;
    }

    public Ability Build(Entity entity, JsonObject entityJson)
    {
        var (entityTimers, cooldownTimerHandle) = InitTimers(entity, entityJson);
        var animationMeta = InitAnimation(entity, entityJson);
        var onHitEffects = InitEffects(entity);
        var debugDrawSystem = InitDebugDrawSystem(entity, entityJson);
        var target = TargetSelector.Build(entity);
        var action = Action.Build(entity, onHitEffects, animationMeta, debugDrawSystem);
        action.AbilityId = Id;

        return new Ability(
            entity,
            entityTimers,
            cooldownTimerHandle,
            animationMeta,
            target,
            action);
    }
}
