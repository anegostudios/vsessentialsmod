using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent;

/// <summary>
/// Updates dynamically assigned entity tags that have 'state-' prefix.
/// </summary>
public class EntityBehaviorEntityStateTags : EntityBehavior
{
    public EntityBehaviorEntityStateTags(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => "entityStateTags";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        if (attributes.KeyExists("updatePeriodSec"))
        {
            UpdatePeriodSec = attributes["updatePeriodSec"].AsFloat(UpdatePeriodSec);
        }

        EntityTagArray tags = entity.Tags;

        TagsInitialUpdate(ref tags);

        if (entity.Tags != tags)
        {
            entity.Tags = tags;
            entity.MarkTagsDirty();
        }

        TimeSinceUpdateSec = (float)entity.World.Rand.NextDouble() * UpdatePeriodSec;
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.Api.Side != EnumAppSide.Server) return;

        TimeSinceUpdateSec += deltaTime;
        if (TimeSinceUpdateSec < UpdatePeriodSec) return;
        TimeSinceUpdateSec = 0;

        EntityTagArray tags = entity.Tags;

        TagsUpdate(ref tags);

        if (entity.Tags != tags)
        {
            entity.Tags = tags;
            entity.MarkTagsDirty();
        }
        entity.World.FrameProfiler.Mark("statetagsupdate");
    }

    // Previous entity state
    protected bool Swimming = false;
    protected bool FeetInLiquid = false;
    protected bool OnGround = false;
    protected bool Flying = false;
    protected bool Aiming = false;
    protected bool Moving = false;
    protected bool Alive = false;
    protected bool Sprinting = false;
    protected bool Sneaking = false;
    protected bool Armed = false;
    protected bool ArmedMelee = false;
    protected bool ArmedRanged = false;
    protected bool HoldingOpenFire = false;

    // Assigned entity tags, filled in GetTagsIds() in Initialize()
    protected static EntityTagArray TagSwimming = EntityTagArray.Empty;
    protected static EntityTagArray TagFeetInLiquid = EntityTagArray.Empty;
    protected static EntityTagArray TagFlying = EntityTagArray.Empty;
    protected static EntityTagArray TagOnGround = EntityTagArray.Empty;
    protected static EntityTagArray TagMoving = EntityTagArray.Empty;
    protected static EntityTagArray TagAlive = EntityTagArray.Empty;
    protected static EntityTagArray TagAiming = EntityTagArray.Empty;
    protected static EntityTagArray TagSprinting = EntityTagArray.Empty;
    protected static EntityTagArray TagSneaking = EntityTagArray.Empty;
    protected static EntityTagArray TagArmed = EntityTagArray.Empty;
    protected static EntityTagArray TagArmedMelee = EntityTagArray.Empty;
    protected static EntityTagArray TagArmedRanged = EntityTagArray.Empty;
    protected static EntityTagArray TagHoldingOpenFire = EntityTagArray.Empty;

    // Checked item tags, filled in GetTagsIds() in Initialize()
    protected static ItemTagArray ItemTagWeapon = ItemTagArray.Empty;
    protected static ItemTagArray ItemTagWeaponMelee = ItemTagArray.Empty;
    protected static ItemTagArray ItemTagWeaponRanged = ItemTagArray.Empty;
    protected static ItemTagArray ItemTagHasOpenFire = ItemTagArray.Empty;

    // Checked block tags, filled in GetTagsIds() in Initialize()
    protected static BlockTagArray BlockTagHasOpenFire = BlockTagArray.Empty;

    protected float TimeSinceUpdateSec = 0;
    protected float UpdatePeriodSec = 1;


    public static void GetTagsIds(ITagRegistry registry)
    {
        TagSwimming = new(registry.EntityTagToTagId("state-swimming"));
        TagFeetInLiquid = new(registry.EntityTagToTagId("state-feet-in-liquid"));
        TagFlying = new(registry.EntityTagToTagId("state-flying"));
        TagOnGround = new(registry.EntityTagToTagId("state-on-ground"));
        TagMoving = new(registry.EntityTagToTagId("state-moving"));
        TagAlive = new(registry.EntityTagToTagId("state-alive"));
        TagAiming = new(registry.EntityTagToTagId("state-aiming"));
        TagSprinting = new(registry.EntityTagToTagId("state-sprinting"));
        TagSneaking = new(registry.EntityTagToTagId("state-sneaking"));
        TagArmed = new(registry.EntityTagToTagId("state-armed"));
        TagArmedMelee = new(registry.EntityTagToTagId("state-armed-melee"));
        TagArmedRanged = new(registry.EntityTagToTagId("state-armed-ranged"));

        ItemTagWeapon = new(registry.ItemTagToTagId("weapon"));
        ItemTagWeaponMelee = new(registry.ItemTagToTagId("weapon-melee"));
        ItemTagWeaponRanged = new(registry.ItemTagToTagId("weapon-ranged"));
        ItemTagHasOpenFire = new(registry.ItemTagToTagId("has-open-fire"));

        BlockTagHasOpenFire = new(registry.BlockTagToTagId("has-open-fire"));
    }

    protected virtual void TagsInitialUpdate(ref EntityTagArray tags)
    {
        EntityTagsInitialUpdate(ref tags);

        if (entity is EntityAgent entityAgent)
        {
            EntityAgentTagsInitialUpdate(entityAgent, ref tags);
            EntityAgentHandItemsTagsInitialUpdate(entityAgent, ref tags);
        }
    }

    protected virtual void TagsUpdate(ref EntityTagArray tags)
    {
        EntityTagsUpdate(ref tags);

        if (entity is EntityAgent entityAgent)
        {
            EntityAgentTagsUpdate(entityAgent, ref tags);
            EntityAgentHandItemsTagsUpdate(entityAgent, ref tags);
        }
    }

    protected virtual void EntityTagsInitialUpdate(ref EntityTagArray tags)
    {
        tags = tags.Remove(TagSwimming);
        tags = tags.Remove(TagFeetInLiquid);
        tags = tags.Remove(TagOnGround);
        tags = tags.Remove(TagAlive);

        EntityTagsUpdate(ref tags);
    }

    protected virtual void EntityAgentTagsInitialUpdate(EntityAgent entityAgent, ref EntityTagArray tags)
    {
        tags = tags.Remove(TagMoving);
        tags = tags.Remove(TagAiming);
        tags = tags.Remove(TagFlying);
        tags = tags.Remove(TagSneaking);
        tags = tags.Remove(TagSprinting);

        EntityAgentTagsUpdate(entityAgent, ref tags);
    }

    protected virtual void EntityAgentHandItemsTagsInitialUpdate(EntityAgent entityAgent, ref EntityTagArray tags)
    {
        ItemStack? handStack = entityAgent.RightHandItemSlot?.Itemstack;
        ItemTagArray itemTags = handStack?.Item?.Tags ?? ItemTagArray.Empty;
        BlockTagArray blockTags = handStack?.Block?.Tags ?? BlockTagArray.Empty;

        handStack = entityAgent.LeftHandItemSlot?.Itemstack;
        if (handStack?.Item != null) itemTags |= handStack.Item.Tags;
        if (handStack?.Block != null) blockTags |= handStack.Block.Tags;

        InitializeTag(ref tags, Armed = itemTags.ContainsAll(ItemTagWeapon), TagArmed);
        InitializeTag(ref tags, ArmedMelee = itemTags.ContainsAll(ItemTagWeaponMelee), TagArmedMelee);
        InitializeTag(ref tags, ArmedRanged = itemTags.ContainsAll(ItemTagWeaponRanged), TagArmedRanged);
        InitializeTag(ref tags, HoldingOpenFire = itemTags.ContainsAll(ItemTagHasOpenFire) || blockTags.ContainsAll(BlockTagHasOpenFire), TagHoldingOpenFire);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void UpdateTag(ref EntityTagArray tags, ref bool storedValue, EntityTagArray mask, bool newValue)
    {
        if (storedValue == newValue) return;

        storedValue = newValue;

        if (newValue)
        {
            tags |= mask;
        }
        else
        {
            tags &= ~mask;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void InitializeTag(ref EntityTagArray tags, bool newValue, EntityTagArray mask)
    {
        if (newValue)
        {
            tags |= mask;
        }
        else
        {
            tags &= ~mask;
        }
    }

    protected virtual void EntityTagsUpdate(ref EntityTagArray tags)
    {
        UpdateTag(ref tags, ref Swimming, TagSwimming, entity.Swimming);
        UpdateTag(ref tags, ref FeetInLiquid, TagFeetInLiquid, entity.FeetInLiquid && !Swimming);
        UpdateTag(ref tags, ref OnGround, TagOnGround, entity.OnGround);
        UpdateTag(ref tags, ref Alive, TagAlive, entity.Alive);
    }

    protected virtual void EntityAgentTagsUpdate(EntityAgent entityAgent, ref EntityTagArray tags)
    {
        EntityControls controls = entityAgent.Controls;

        UpdateTag(ref tags, ref Moving, TagMoving,
            controls.Forward || controls.Backward || controls.Right || controls.Left || controls.Jump || controls.Gliding
        );
        UpdateTag(ref tags, ref Aiming, TagAiming, controls.IsAiming);
        UpdateTag(ref tags, ref Flying, TagFlying, controls.IsFlying);
        UpdateTag(ref tags, ref Sneaking, TagSneaking, controls.Sneak);
        UpdateTag(ref tags, ref Sprinting, TagSprinting, controls.Sprint);
    }

    protected virtual void EntityAgentHandItemsTagsUpdate(EntityAgent entityAgent, ref EntityTagArray tags)
    {
        ItemStack? handStack = entityAgent.ActiveHandItemSlot?.Itemstack;
        Item? item = handStack?.Item;
        if (item == null) // This will be the case for 99% of entities, which do not hold anything in their hands
        {
            UpdateTag(ref tags, ref Armed, TagArmed, false);
            UpdateTag(ref tags, ref ArmedMelee, TagArmedMelee, false);
            UpdateTag(ref tags, ref ArmedRanged, TagArmedRanged, false);
        }
        else
        {
            UpdateTag(ref tags, ref Armed, TagArmed, ItemTagWeapon.isPresentIn(ref item.Tags));
            UpdateTag(ref tags, ref ArmedMelee, TagArmedMelee, ItemTagWeaponMelee.isPresentIn(ref item.Tags));
            UpdateTag(ref tags, ref ArmedRanged, TagArmedRanged, ItemTagWeaponRanged.isPresentIn(ref item.Tags));
        }

        Block? block = handStack?.Block;
        UpdateTag(ref tags, ref HoldingOpenFire, TagHoldingOpenFire,
            item != null && ItemTagHasOpenFire.isPresentIn(ref item.Tags) ||
            block != null && BlockTagHasOpenFire.isPresentIn(ref block.Tags)
        );
    }
}
