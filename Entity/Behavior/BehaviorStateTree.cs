using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.Essentials;
using Vintagestory.StateTree;

namespace Vintagestory.GameContent;

/// <summary>
/// Enables the entity to build and tick a state tree asset. This and <see cref="EntityBehaviorTaskAI"/>
/// should not be used together. Usage example:
/// <code>
/// {
///     "code": "state-tree",
///     "tree": "your-state-tree-id"
/// }
/// </code>
/// </summary>
/// <param name="entity"></param>
public class EntityBehaviorStateTree(Entity entity) : EntityBehavior(entity)
{
    public StateTree.StateTree PrimaryStateTree;
    private string primaryTreeId;
    private readonly Dictionary<string, StateTree.StateTree> actionStateTrees = new();

    private JsonObject attributes;
    private ILogger logger;

    public readonly StraightLineTraverser StraightLineTraverser = new(entity as EntityAgent);

    public override string PropertyName() => "state-tree";

    private readonly HashSet<Type> sensesCheckedThisTick = [];

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        this.attributes = attributes;
        this.logger = entity.World.Logger;

        primaryTreeId = attributes["tree"]?.AsString();

        BuildStateTree();
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        StraightLineTraverser.AfterInitialized();
    }

    public void BuildStateTree()
    {
        if (!AssetRegistry.Collection<IStateTreeAsset>().TryBuildAsset(primaryTreeId, entity, attributes, out PrimaryStateTree))
        {
            logger.Error($"Failed to build state tree '{primaryTreeId}' for entity '{entity.Code}'");
        }
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.State != EnumEntityState.Active || !entity.Alive) return;

        if (StraightLineTraverser.Active)
            StraightLineTraverser.OnGameTick(deltaTime);

        entity.World.FrameProfiler.Enter("ai-state-tree");

        PrimaryStateTree?.Tick(deltaTime);

        entity.World.FrameProfiler.Leave();

        sensesCheckedThisTick.Clear();
    }

    public StateTree.StateTree RequestActionStateTree(string treeId)
    {
        if (actionStateTrees.TryGetValue(treeId, out var tree))
            return tree;

        if (primaryTreeId == treeId)
        {
            logger.Error($"Received request to build state tree '{treeId}' for entity '{entity.Code}', but the entity is already using that state tree as its primary one");
            return null;
        }

        if (!AssetRegistry.Collection<IStateTreeAsset>().TryBuildAsset(treeId, entity, attributes, out StateTree.StateTree stateTree))
        {
            logger.Error($"Failed to build state tree '{treeId}' for entity '{entity.Code}'");
            return null;
        }

        actionStateTrees.Add(treeId, stateTree);
        return stateTree;
    }

    public bool HasCheckedSenseThisFrame(Sense sense) => !sensesCheckedThisTick.Add(sense.GetType());

    public static bool TryGet(Entity entity, [NotNullWhen(true)] out EntityBehaviorStateTree? behavior)
    {
        behavior = entity.GetBehavior<EntityBehaviorStateTree>();
        if (behavior == null)
        {
            entity.World.Logger.Warning($"Failed to retrieve EntityBehaviorStateTree for entity '{entity.Code}'");
            return false;
        }

        return true;
    }
}
