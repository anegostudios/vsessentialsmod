using System.ComponentModel;
using Newtonsoft.Json;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.StateTree;

public abstract class BaseConditionDefinition : IConditionDefinition
{
    /// <summary>
    /// Whether to invert the result of this condition.
    /// </summary>
    [JsonProperty]
    [DefaultValue(false)]
    public bool Invert { get; set; }

    public abstract ICondition Build(Entity entity);
}
