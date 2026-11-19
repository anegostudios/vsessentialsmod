using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent;

[AssetSchemaMetadata("Damage", "Applies DamageEffect to hit entities.")]
public class DamageEffectDefinition : IEffectDefinition
{
    [JsonProperty(Required = Required.Always)]
    public EnumDamageType Type { get; set; }

    [JsonProperty(Required = Required.Always)]
    public Variable<float> Damage { get; set; }

    [JsonProperty]
    public Variable<float> KnockbackStrength { get; set; }

    public IEffect Build(Entity entity)
    {
        return new DamageEffect(Type, Damage.GetValue(entity), KnockbackStrength.GetValue(entity));
    }
}
