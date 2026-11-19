using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

/// <summary>
/// Runtime representation of a standard state tree.
/// </summary>
public class StateTree : IAssetInstance
{
    public readonly string Id;
    private readonly List<Sense> senses;
    private readonly NodeList nodeList;
    private readonly Entity entity;
    private readonly EntityBehaviorStateTree stateTree;
    private readonly EntityBehaviorMemory memory;

    private readonly float maxSenseRange;

    private HashSet<Node> previousNodes = [];

    private readonly HashSet<Node> currentNodes = [];
    private readonly List<int> selectedNodeIndices = [];
    public IReadOnlyList<int> SelectedNodeIndices => selectedNodeIndices;
    public StateTree(string id, List<Sense> senses, NodeList nodeList, Entity entity)
    {
        this.Id = id;
        this.senses = senses;
        this.nodeList = nodeList;
        this.entity = entity;
        EntityBehaviorStateTree.TryGet(entity, out stateTree);
        EntityBehaviorMemory.TryGet(entity, out memory);

        foreach (var sense in senses)
        {
            var senseRange = sense.GetRange();
            if (senseRange > maxSenseRange)
            {
                maxSenseRange = senseRange;
            }
        }
    }

    public void Tick(float deltaTime)
    {
        var nearbyEntities = entity.World.GetEntitiesAround(entity.Pos.XYZ, maxSenseRange, maxSenseRange);

        foreach (var sense in senses)
        {
            if (!stateTree.HasCheckedSenseThisFrame(sense))
            {
                sense.Check(nearbyEntities, memory);
            }
        }

        currentNodes.Clear();
        selectedNodeIndices.Clear();

        nodeList.Evaluate(entity, deltaTime, currentNodes, selectedNodeIndices);

        foreach (var node in previousNodes)
        {
            if (!currentNodes.Contains(node))
            {
                node.Reset();
            }
        }

        previousNodes = currentNodes;
    }
}
