using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.StateTree;

namespace Vintagestory.GameContent;

public class StateTreeSystem : ModSystem
{
    private readonly AssetCategory stateTrees = new("statetrees", false, EnumAppSide.Server);

    /// <summary>
    /// State Tree asset ID to entities using that asset.
    /// </summary>
    private readonly Dictionary<string, HashSet<Entity>> entities = [];
    public IReadOnlyDictionary<string, HashSet<Entity>> Entities => entities;

    private ICoreServerAPI? sapi;

    [RegistersAssetTypes]
    public static AssetCollection<IStateTreeAsset> RegisterAssetTypes()
    {
        return AssetRegistry.RegisterCollection<IStateTreeAsset>()
            .RegisterVariant<StateTreeAsset>()

            // Senses
            .RegisterElement<HearingSenseDefinition>()
            .RegisterElement<SightSenseDefinition>()
            .RegisterElement<SmellSenseDefinition>()
            .RegisterElement<TouchSenseDefinition>()

            // Conditions
            .RegisterElement<DebugConstantConditionDefinition>()
            .RegisterElement<HealthConditionDefinition>()
            .RegisterElement<InLiquidConditionDefinition>()
            .RegisterElement<MemoryConditionDefinition>()
            .RegisterElement<StateConditionDefinition>()
            .RegisterElement<TargetBlockConditionDefinition>()
            .RegisterElement<TargetEntityConditionDefinition>()
            .RegisterElement<TimerConditionDefinition>()
            .RegisterElement<WorldTimeConditionDefinition>()

            // Actions
            .RegisterElement<AbilityActionDefinition>()
            .RegisterElement<ClearTargetBlockActionDefinition>()
            .RegisterElement<ClearTargetEntityActionDefinition>()
            .RegisterElement<DebugBroadcastActionDefinition>()
            .RegisterElement<DelayActionDefinition>()
            .RegisterElement<MoveToActionDefinition>()
            .RegisterElement<RotateActionDefinition>()
            .RegisterElement<RotateTowardsActionDefinition>()
            .RegisterElement<StateActionDefinition>()
            .RegisterElement<StateTreeActionDefinition>()
            .RegisterElement<TargetBlockActionDefinition>()
            .RegisterElement<TargetEntityActionDefinition>()
            .RegisterElement<TimerActionDefinition>();
    }

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.OnEntityDespawn += OnEntityDespawn;

        var config = VSEssentialsModServerConfig.LoadOrCreate(api);
        if (config.EnableAssetHotReloading)
        {
            api.Assets.WatchCategory(stateTrees, asset => api.Event.EnqueueMainThreadTask(() => OnAssetChanged(asset), "state-tree-asset-reload"));
        }
    }

    AssetCollection<IStateTreeAsset>? collection;
    public override void AssetsLoaded(ICoreAPI api)
    {
        collection = RegisterAssetTypes();
        collection.OnAssetBuilt += OnAssetBuilt;

        var seen = new HashSet<AssetLocation>();
        for (var i = api.Assets.Origins.Count - 1; i >= 0; --i)
        {
            var origin = api.Assets.Origins[i];
            foreach (var asset in origin.GetAssets(stateTrees))
            {
                if (!seen.Add(asset.Location)) continue;

                var treeAsset = asset.ToObject<StateTreeAsset>();
                if (treeAsset == null) continue;
                if (collection.TryGetAsset(treeAsset.Id, out _)) continue;

                collection.AddAsset(treeAsset);
            }
        }

        Sense.Initialize(api);
    }

    public override void Dispose()
    {
        collection?.OnAssetBuilt -= OnAssetBuilt;
        sapi?.Event.OnEntityDespawn -= OnEntityDespawn;

        entities.Clear();
    }

    private void OnAssetBuilt(Entity entity, IStateTreeAsset asset)
    {
        if (entities.TryGetValue(asset.Id, out var set))
        {
            set.Add(entity);
        }
        else
        {
            entities[asset.Id] = [entity];
        }
    }

    private void OnEntityDespawn(Entity entity, EntityDespawnData reason)
    {
        var stateTree = entity.GetBehavior<EntityBehaviorStateTree>();
        if (stateTree != null && entities.TryGetValue(stateTree.PrimaryStateTree.Id, out var set))
        {
            set.Remove(entity);
        }
    }

    private void OnAssetChanged(IAsset asset)
    {
        var treeAsset = asset.ToObject<StateTreeAsset>();
        if (treeAsset == null) return;

        var collection = AssetRegistry.Collection<IStateTreeAsset>();
        collection.AddAsset(treeAsset, true);
        if (!entities.TryGetValue(treeAsset.Id, out var set)) return;

        foreach (var entity in set)
        {
            entity.GetBehavior<EntityBehaviorStateTree>()?.BuildStateTree();
        }
    }
}
