using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

/// <summary>
/// Provides a simple state machine to the entity. This is an optional layer on top of
/// <see cref="EntityBehaviorStateTree"/> - it's only required whe using conditions or
/// actions that need to check or set the current state explicitly.
/// States can also optionally override attribute values. Usage example:
/// <code>
/// {
///     "code": "states",
///     "states": {
///         "calm": {},
///         "alert": {
///             "hearingRadius": 10.0
///         }
///     },
///     "defaultState": "calm"
/// }
/// </code>
/// </summary>
/// <param name="entity"></param>
public class EntityBehaviorStates(Entity entity) : EntityBehavior(entity)
{
    private readonly List<string> stateKeys = [];
    private readonly List<JToken> stateValues = [];
    private int currentStateIndex = -1;

    private JsonObject attributes;

    public override string PropertyName() => "states";

    public override void Initialize(EntityProperties properties, JsonObject json)
    {
        entity.World?.Logger?.Debug("Initializing EntityBehaviorStates for entity: " + entity.GetName());

        JsonObject statesObject = json["states"];
        if (statesObject.Token is not JObject states)
        {
            entity.World?.Logger?.Warning($"EntityBehaviorStates: Expected JObject, got {statesObject.Token?.GetType().Name ?? "null"}");
            return;
        }

        foreach (var (key, state) in states)
        {
            if (stateKeys.Contains(key))
            {
                entity.World?.Logger?.Warning($"EntityBehaviorStates: Duplicate state key: {key}");
                continue;
            }

            stateKeys.Add(key);
            stateValues.Add(state);
        }

        if (json["defaultState"]?.Token is JValue defaultState)
        {
            currentStateIndex = stateKeys.IndexOf(defaultState.Value<string>());
        }

        attributes = properties.Attributes;
    }

    public int GetStateIndexFromKey(string key) => stateKeys.IndexOf(key);

    public string GetStateKeyFromIndex(int index) => stateKeys[index];

    public JToken GetState(int index) => stateValues[index];

    public int GetCurrentStateIndex() => currentStateIndex;

    public void SetCurrentStateIndex(int index)
    {
        if (index < 0 || index >= stateValues.Count)
        {
            entity.World.Logger.Warning($"EntityBehaviorStates: Received invalid state index: {index}");
            return;
        }

        currentStateIndex = index;
    }

    public JToken? GetAttribute(string key)
    {
        JToken currentState = stateValues[currentStateIndex];
        return currentState.SelectToken(key) ?? attributes.Token?.SelectToken(key);
    }

    public static bool TryGet(Entity entity, [NotNullWhen(true)] out EntityBehaviorStates? behavior)
    {
        behavior = entity.GetBehavior<EntityBehaviorStates>();
        if (behavior == null)
        {
            entity.World.Logger.Warning($"Failed to retrieve EntityBehaviorStates for entity '{entity.Code}'");
            return false;
        }

        return true;
    }
}
