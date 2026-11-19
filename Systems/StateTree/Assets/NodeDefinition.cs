using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

public class ConditionList
{
    [JsonProperty(Required = Required.Always)]
    [DefaultValue("MatchAll")]
    public ConditionListMode Mode { get; set; }

    [JsonProperty(Required = Required.Always)]
    public List<IConditionDefinition> Conditions { get; set; }
}

public class ActionList
{
    [JsonProperty(Required = Required.Always)]
    [DefaultValue("Sequence")]
    public ActionListMode Mode { get; set; }

    [JsonProperty(Required = Required.Always)]
    public List<ActionDefinition> Actions { get; set; }
}

public class NodeDefinition
{
    [JsonProperty]
    public string Label { get; set; }

    [JsonProperty]
    [DefaultValue("#303030")]
    public string Color { get; set; } = "#303030";

    [JsonProperty]
    [AssetSchemaMetadata(null, "Determines whether this node (and its children) should be evaluated")]
    [DefaultValue(true)]
    public bool IsEnabled { get; set; }

    [JsonProperty(Required = Required.Always)]
    public ConditionList ConditionList { get; set; }

    [JsonProperty(Required = Required.Always)]
    public ActionList ActionList { get; set; }

    [JsonProperty]
    public NodeListDefinition Children { get; set; }

    public Node Build(Entity entity)
    {
        List<ICondition> conditions = new List<ICondition>();
        foreach (var condDef in ConditionList.Conditions)
        {
            conditions.Add(condDef.Build(entity));
        }

        List<Action> actions = new List<Action>();
        foreach (var actionDef in ActionList.Actions)
        {
            actions.Add(actionDef.Build(entity));
        }

        NodeList childNodeList = null;
        if (Children != null)
        {
            List<Node> childNodes = new List<Node>();
            foreach (var nodeDef in Children.Nodes)
            {
                childNodes.Add(nodeDef.Build(entity));
            }

            childNodeList = new NodeList(Children.Mode, childNodes);
        }

        return new Node(
            IsEnabled,
            ConditionList.Mode,
            conditions,
            ActionList.Mode,
            actions,
            childNodeList);
    }
}
