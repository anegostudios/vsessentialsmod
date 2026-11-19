using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

public class DefaultExpressionEvaluator : IExpressionEvaluator
{
    private JToken? GetAttribute(Entity entity, string key)
    {
        var states = entity.GetBehavior<EntityBehaviorStates>();
        return states?.GetAttribute(key) ?? entity.Properties.Attributes.Token?.SelectToken(key);
    }

    /// <summary>
    /// Parses an expression. Supports parsing a single variable by looking it up in the entity's attributes.
    /// </summary>
    /// <inheritdoc/>
    public T? ParseExpression<T>(Entity entity, string expression) where T : unmanaged
    {
        if (expression.Length < 2 || expression[0] != '{' || expression[^1] != '}')
        {
            return null;
        }

        var key = expression.Substring(1, expression.Length - 2);
        var value = GetAttribute(entity, key);
        return value?.ToObject<T>();
    }
}

public class AssetExpressionEvaluation : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        ExpressionEvaluator.Instance = new DefaultExpressionEvaluator();
    }
}
