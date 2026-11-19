using System.Collections.Generic;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.StateTree;

/// <summary>
/// Runtime representation of a collection of nodes (essentially a "branch" in a state tree).
/// </summary>
/// <param name="selectionMode"></param>
/// <param name="nodes"></param>
public class NodeList(SelectionMode selectionMode, List<Node> nodes)
{
    /// <summary>
    /// Evaluates the nodes according to the selection type.
    /// </summary>
    /// <param name="entity">The entity that owns the tree</param>
    /// <param name="deltaTime">Delta time for the current frame</param>
    /// <param name="selectedNodes">The nodes selected during the current tree evaluation</param>
    /// <param name="selectedNodeIndices">The indices of the nodes selected during the current tree evaluation</param>
    public void Evaluate(Entity entity, float deltaTime, HashSet<Node> selectedNodes, List<int> selectedNodeIndices)
    {
        for (var i = 0; i < nodes.Count; ++i)
        {
            var node = nodes[i];

            if (!node.IsEnabled || !node.Evaluate(entity, deltaTime, selectedNodes, selectedNodeIndices)) continue;

            selectedNodes.Add(node);
            selectedNodeIndices.Add(i);

            if (selectionMode == SelectionMode.Sequence)
            {
                return;
            }
        }
    }
}
