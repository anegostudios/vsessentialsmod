using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent;

public class AbilitySystem : ModSystem
{
    private readonly AssetCategory abilities = new("abilities", false, EnumAppSide.Universal);

    [RegistersAssetTypes]
    public static AssetCollection<IAbilityAsset> RegisterAssetTypes()
    {
        return AssetRegistry.RegisterCollection<IAbilityAsset>()
            .RegisterVariant<AbilityAsset>()

            // Target selectors
            .RegisterElement<MemoryTargetSelectorDefinition>()
            .RegisterElement<SelfTargetSelectorDefinition>()

            // Actions
            .RegisterElement<ConeActionDefinition>()
            .RegisterElement<DirectActionDefinition>()
            .RegisterElement<LaunchProjectileActionDefinition>()
            .RegisterElement<LaunchAoEProjectileActionDefinition>()
            .RegisterElement<RadiusActionDefinition>()
            .RegisterElement<RayActionDefinition>()

            // Effects
            .RegisterElement<DamageEffectDefinition>();
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        var collection = RegisterAssetTypes();

        var seen = new HashSet<AssetLocation>();
        for (var i = api.Assets.Origins.Count - 1; i >= 0; --i)
        {
            var origin = api.Assets.Origins[i];
            foreach (var asset in origin.GetAssets(abilities))
            {
                if (!seen.Add(asset.Location)) continue;

                var abilityAsset = asset.ToObject<AbilityAsset>();
                if (abilityAsset == null) continue;
                if (collection.TryGetAsset(abilityAsset.Id, out _)) continue;

                collection.AddAsset(abilityAsset);
            }
        }
    }
}
