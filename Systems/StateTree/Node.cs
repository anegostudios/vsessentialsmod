using System;
using System.Collections.Generic;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.StateTree;

public class RandomActionPicker(List<Action> actions)
{
    private int lastPicked = -1;
    private float timeUntilNextPick = PickIntervalSeconds;

    /// <summary>
    /// Minimum interval between picks. Prevents an action from being continuously used for too long while the node
    /// selection doesn't change. This could in future be exposed as a property that can be edited per action, similar
    /// to the Weight property.
    /// </summary>
    private const float PickIntervalSeconds = 1f;

    public int Evaluate(float deltaTime)
    {
        timeUntilNextPick -= deltaTime;
        if (lastPicked == -1 || timeUntilNextPick <= 0f)
        {
            lastPicked = RandomPick();
            timeUntilNextPick = PickIntervalSeconds;
        }

        return lastPicked;
    }

    public void Reset()
    {
        lastPicked = -1;
        timeUntilNextPick = PickIntervalSeconds;
    }

    private int RandomPick()
    {
        float totalWeight = 0f;
        foreach (var action in actions)
            totalWeight += action.Weight;

        float roll = Random.Shared.NextSingle() * totalWeight;

        float cumulative = 0f;
        for (var i = 0; i < actions.Count; ++i)
        {
            cumulative += actions[i].Weight;
            if (roll < cumulative)
                return i;
        }

        return 0;
    }
}

/// <summary>
/// Runtime representation of a single node in a state tree.
/// </summary>
public class Node
{
    public bool IsEnabled { get; }

    private readonly ConditionListMode conditionListMode;
    private readonly List<ICondition> conditions;
    private readonly ActionListMode actionListMode;
    private readonly List<Action> actions;
    private readonly NodeList children;

    /// <summary>
    /// Used when <see cref="actionListMode"/> is set to <see cref="ActionListMode.Random"/>.
    /// </summary>
    private readonly RandomActionPicker randomActionPicker;

    /// <summary>
    /// Runtime representation of a single node in a state tree.
    /// </summary>
    /// <param name="isEnabled">Whether this node is enabled</param>
    /// <param name="conditionListMode">How the conditions are evaluated</param>
    /// <param name="conditions">The conditions to evaluate for this node to be selected</param>
    /// <param name="actionListMode">How the actions are executed</param>
    /// <param name="actions">The actions to execute if this node is selected</param>
    /// <param name="children">The child nodes of this node (if any) - these will be evaluated if this node is selected</param>
    public Node(bool isEnabled,
        ConditionListMode conditionListMode,
        List<ICondition> conditions,
        ActionListMode actionListMode,
        List<Action> actions,
        NodeList children)
    {
        IsEnabled = isEnabled;
        this.conditionListMode = conditionListMode;
        this.conditions = conditions;
        this.actionListMode = actionListMode;
        this.actions = actions;
        this.children = children;

        if (actionListMode == ActionListMode.Random)
        {
            randomActionPicker = new RandomActionPicker(actions);
        }
    }

    /// <summary>
    /// Evaluates the node according to its conditions. If the conditions are met, this node is selected.
    /// If the node is selected, its actions are executed and its children are evaluated.
    /// </summary>
    /// <param name="entity">The entity that owns the tree</param>
    /// <param name="deltaTime">Delta time for the current frame</param>
    /// <param name="selectedNodes">The nodes selected during the current tree evaluation</param>
    /// <param name="selectedNodeIndices">The indices of the nodes selected during the current tree evaluation</param>
    /// <returns>True if this node was selected, false otherwise</returns>
    public bool Evaluate(Entity entity, float deltaTime, HashSet<Node> selectedNodes, List<int> selectedNodeIndices)
    {
        bool select = true;
        foreach (var condition in conditions)
        {
            float score = condition.Evaluate();
            if (conditionListMode == ConditionListMode.MatchAll)
            {
                if (score < 0.5f)
                {
                    select = false;
                    break;
                }
            }
            else if (conditionListMode == ConditionListMode.MatchAny)
            {
                select = score > 0.5f;
                if (select) break;
            }
        }

        if (select)
        {
            switch (actionListMode)
            {
                case ActionListMode.Sequence:
                    foreach (var action in actions)
                    {
                        if (action.ShouldExecute()) action.Execute(deltaTime);
                        if (!action.HasFinished()) break;
                    }
                    break;
                case ActionListMode.Random:
                    var i = randomActionPicker.Evaluate(deltaTime);
                    if (actions[i].ShouldExecute()) actions[i].Execute(deltaTime);
                    break;
            }

            children?.Evaluate(entity, deltaTime, selectedNodes, selectedNodeIndices);
        }

        return select;
    }

    public void Reset()
    {
        foreach (var action in actions)
        {
            action.Reset();
        }

        randomActionPicker?.Reset();
    }
}
