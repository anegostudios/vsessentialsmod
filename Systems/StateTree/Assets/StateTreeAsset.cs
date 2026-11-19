using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Vintagestory.StateTree;

[JsonObject(MemberSerialization.OptIn)]
public class StateTreeAsset : IStateTreeAsset
{
    [JsonProperty(Required = Required.Always)]
    public string Id { get; set; }

    [JsonProperty]
    public List<SenseDefinition> Senses { get; set; }

    [JsonProperty(Required = Required.Always)]
    public NodeListDefinition Tree { get; set; }

    public StateTree Build(Entity entity, JsonObject entityJson)
    {
        if (!EntityBehaviorStates.TryGet(entity, out var states))
        {
            throw new InvalidOperationException("Missing required behavior");
        }

        List<Sense> senses = [];
        if (Senses != null)
        {
            senses = Senses.Select(sensorDef => sensorDef.Build(entity, states)).ToList();
        }

        List<Node> nodes = Tree.Nodes.Select(nodeDef => nodeDef.Build(entity)).ToList();
        NodeList nodeList = new NodeList(Tree.Mode, nodes);

        return new StateTree(Id, senses, nodeList, entity);
    }
}
