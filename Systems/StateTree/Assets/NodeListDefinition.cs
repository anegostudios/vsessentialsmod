using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace Vintagestory.StateTree;

public class NodeListDefinition
{
    [JsonProperty(Required = Required.Always)]
    [DefaultValue("Sequence")]
    public SelectionMode Mode { get; set; }

    [JsonProperty(Required = Required.Always)]
    public List<NodeDefinition> Nodes { get; set; } = [];
}
