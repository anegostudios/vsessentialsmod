using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

/// <summary>
/// 
/// <br/>
/// Changes 1.21.0-pre.1 => 1.21.0-pre.2<br/>
/// - executionChance default value: 0.005 => 1.0<br/>
/// - useTime => UseTimeSec<br/>
/// - moved to Diet<br/>
/// - sound uses base option, add sound delay (0.75 of duration)
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskEatHeldItemConfig : AiTaskBaseConfig
{
    /// <summary>
    /// Task duration, item will be consumed at the end of the duration.
    /// </summary>
    [JsonProperty] public float DurationSec = 1.5f;

    /// <summary>
    /// Chance for entity to seek food source without eating it.
    /// </summary>
    [JsonProperty] public float ChanceToUseFoodWithoutEating = 0.004f;

    /// <summary>
    /// Determines what entity can eat, <see cref="Diet"/>. Taken from entity attributes if not specified.
    /// </summary>
    [JsonProperty] public CreatureDiet? Diet;

    /// <summary>
    /// If set to true, consumed item will restore saturation.
    /// </summary>
    [JsonProperty] public bool ConsumePortion = true;

    /// <summary>
    /// Saturation restored per portion eaten.
    /// </summary>
    [JsonProperty] public float SaturationPerPortion = 1f;

    /// <summary>
    /// What hand item slot to eat from.
    /// </summary>
    [JsonProperty] public EnumHand HandToEatFrom = EnumHand.Left;


    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        Diet ??= entity.Properties.Attributes["creatureDiet"].AsObject<CreatureDiet>();
        if (Diet == null)
        {
            entity.Api.Logger.Warning("Creature '" + entity.Code.ToShortString() + "' has AiTaskUseInventory task but no Diet specified.");
        }
    }
}

public class AiTaskEatHeldItemR : AiTaskBaseR
{
    private AiTaskEatHeldItemConfig Config => GetConfig<AiTaskEatHeldItemConfig>();

    protected float currentUseTime = 0;
    protected bool soundPlayed = false;
    protected bool isEdible;

    protected EntityBehaviorMultiplyBase? multiplyBehavior;


    public AiTaskEatHeldItemR(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskEatHeldItemConfig>(entity, taskConfig, aiConfig);
    }

    public override void AfterInitialize()
    {
        base.AfterInitialize();

        multiplyBehavior = entity.GetBehavior<EntityBehaviorMultiplyBase>();
    }

    public override bool ShouldExecute()
    {
        if (!PreconditionsSatisficed()) return false;

        // small chance go to the food source anyway just because (without eating anything).
        if (multiplyBehavior != null && !multiplyBehavior.ShouldEat && entity.World.Rand.NextDouble() >= Config.ChanceToUseFoodWithoutEating) return false;

        ItemSlot? leftSlot = GetSlot();

        if (leftSlot == null || leftSlot.Empty) return false;

        ItemStack? stack = leftSlot.Itemstack;

        if (stack == null) return false;

        if (!SuitableFoodSource(stack))
        {
            if (!leftSlot.Empty)
            {
                entity.World.SpawnItemEntity(leftSlot.TakeOutWhole(), entity.ServerPos.XYZ);
            }
            return false;
        }

        isEdible = true;

        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();

        soundPlayed = false;
        currentUseTime = 0;
    }

    public override bool ContinueExecute(float dt)
    {
        base.ContinueExecute(dt);

        currentUseTime += dt;

        ItemSlot? slot = GetSlot();

        if (slot == null || slot.Empty) return false;

        entity.World.SpawnCubeParticles(entity.ServerPos.XYZ, slot.Itemstack, 0.25f, 1, 0.25f + 0.5f * (float)entity.World.Rand.NextDouble());

        if (currentUseTime >= Config.DurationSec)
        {
            if (isEdible && Config.ConsumePortion)
            {
                ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
                if (tree == null) entity.WatchedAttributes["hunger"] = tree = new TreeAttribute();
                tree.SetFloat("saturation", Config.SaturationPerPortion + tree.GetFloat("saturation", 0));
            }

            slot.TakeOut(1);

            return false;
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);

        if (cancelled)
        {
            cooldownUntilTotalHours = 0;
        }
    }

    protected virtual bool SuitableFoodSource(ItemStack itemStack) => Config.Diet?.Matches(itemStack) ?? true;

    protected virtual ItemSlot? GetSlot()
    {
        return Config.HandToEatFrom switch
        {
            EnumHand.Left => entity.LeftHandItemSlot,
            EnumHand.Right => entity.RightHandItemSlot,
            _ => null
        };
    }
}
