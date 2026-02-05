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

        EntityTagSet tags = entity.Tags;

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

        EntityTagSet tags = entity.Tags;

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
    protected static EntityTagSet TagSwimming = EntityTagSet.Empty;
    protected static EntityTagSet TagFeetInLiquid = EntityTagSet.Empty;
    protected static EntityTagSet TagFlying = EntityTagSet.Empty;
    protected static EntityTagSet TagOnGround = EntityTagSet.Empty;
    protected static EntityTagSet TagMoving = EntityTagSet.Empty;
    protected static EntityTagSet TagAlive = EntityTagSet.Empty;
    protected static EntityTagSet TagAiming = EntityTagSet.Empty;
    protected static EntityTagSet TagSprinting = EntityTagSet.Empty;
    protected static EntityTagSet TagSneaking = EntityTagSet.Empty;
    protected static EntityTagSet TagArmed = EntityTagSet.Empty;
    protected static EntityTagSet TagArmedMelee = EntityTagSet.Empty;
    protected static EntityTagSet TagArmedRanged = EntityTagSet.Empty;
    protected static EntityTagSet TagHoldingOpenFire = EntityTagSet.Empty;

    // Checked collectible tags, filled in GetTagsIds() in Initialize()
    protected static TagSet CollectibleTagWeapon = TagSet.Empty;
    protected static TagSet CollectibleTagWeaponMelee = TagSet.Empty;
    protected static TagSet CollectibleTagWeaponRanged = TagSet.Empty;
    protected static TagSet CollectibleTagHasOpenFire = TagSet.Empty;

    protected float TimeSinceUpdateSec = 0;
    protected float UpdatePeriodSec = 1;


    public static void GetTagsIds(ITagsManager registry)
    {
        TagSwimming = registry.GetEntityTagSet("state-swimming");
        TagFeetInLiquid = registry.GetEntityTagSet("state-feet-in-liquid");
        TagFlying = registry.GetEntityTagSet("state-flying");
        TagOnGround = registry.GetEntityTagSet("state-on-ground");
        TagMoving = registry.GetEntityTagSet("state-moving");
        TagAlive = registry.GetEntityTagSet("state-alive");
        TagAiming = registry.GetEntityTagSet("state-aiming");
        TagSprinting = registry.GetEntityTagSet("state-sprinting");
        TagSneaking = registry.GetEntityTagSet("state-sneaking");
        TagArmed = registry.GetEntityTagSet("state-armed");
        TagArmedMelee = registry.GetEntityTagSet("state-armed-melee");
        TagArmedRanged = registry.GetEntityTagSet("state-armed-ranged");

        CollectibleTagWeapon = registry.GetGeneralTagSet("weapon");
        CollectibleTagWeaponMelee = registry.GetGeneralTagSet("weapon-melee");
        CollectibleTagWeaponRanged = registry.GetGeneralTagSet("weapon-ranged");
        CollectibleTagHasOpenFire = registry.GetGeneralTagSet("has-open-fire");
    }

    protected virtual void TagsInitialUpdate(ref EntityTagSet tags)
    {
        EntityTagsInitialUpdate(ref tags);

        if (entity is EntityAgent entityAgent)
        {
            EntityAgentTagsInitialUpdate(entityAgent, ref tags);
            EntityAgentHandItemsTagsInitialUpdate(entityAgent, ref tags);
        }
    }

    protected virtual void TagsUpdate(ref EntityTagSet tags)
    {
        EntityTagsUpdate(ref tags);

        if (entity is EntityAgent entityAgent)
        {
            EntityAgentTagsUpdate(entityAgent, ref tags);
            EntityAgentHandItemsTagsUpdate(entityAgent, ref tags);
        }
    }

    protected virtual void EntityTagsInitialUpdate(ref EntityTagSet tags)
    {
        tags = tags.Except(TagSwimming);
        tags = tags.Except(TagFeetInLiquid);
        tags = tags.Except(TagOnGround);
        tags = tags.Except(TagAlive);

        EntityTagsUpdate(ref tags);
    }

    protected virtual void EntityAgentTagsInitialUpdate(EntityAgent entityAgent, ref EntityTagSet tags)
    {
        tags = tags.Except(TagMoving);
        tags = tags.Except(TagAiming);
        tags = tags.Except(TagFlying);
        tags = tags.Except(TagSneaking);
        tags = tags.Except(TagSprinting);

        EntityAgentTagsUpdate(entityAgent, ref tags);
    }

    protected virtual void EntityAgentHandItemsTagsInitialUpdate(EntityAgent entityAgent, ref EntityTagSet tags)
    {
        ItemStack? handStack = entityAgent.RightHandItemSlot?.Itemstack;
        TagSet itemTags = handStack?.Collectible?.Tags ?? TagSet.Empty;

        handStack = entityAgent.LeftHandItemSlot?.Itemstack;
        if (handStack?.Collectible != null) itemTags = itemTags.Union(handStack.Collectible.Tags);

        Armed = itemTags.IsSupersetOf(CollectibleTagWeapon);
        ArmedMelee = itemTags.IsSupersetOf(CollectibleTagWeaponMelee);
        ArmedRanged = itemTags.IsSupersetOf(CollectibleTagWeaponRanged);
        HoldingOpenFire = itemTags.IsSupersetOf(CollectibleTagHasOpenFire);

        InitializeTag(ref tags, Armed, TagArmed);
        InitializeTag(ref tags, ArmedMelee, TagArmedMelee);
        InitializeTag(ref tags, ArmedRanged, TagArmedRanged);
        InitializeTag(ref tags, HoldingOpenFire, TagHoldingOpenFire);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void UpdateTag(ref EntityTagSet tags, ref bool storedValue, EntityTagSet mask, bool newValue)
    {
        if (storedValue == newValue) return;

        storedValue = newValue;

        if (newValue)
        {
            tags = tags.Union(mask);
        }
        else
        {
            tags = tags.Except(mask);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void InitializeTag(ref EntityTagSet tags, bool newValue, EntityTagSet mask)
    {
        if (newValue)
        {
            tags = tags.Union(mask);
        }
        else
        {
            tags = tags.Except(mask);
        }
    }

    protected virtual void EntityTagsUpdate(ref EntityTagSet tags)
    {
        UpdateTag(ref tags, ref Swimming, TagSwimming, entity.Swimming);
        UpdateTag(ref tags, ref FeetInLiquid, TagFeetInLiquid, entity.FeetInLiquid && !Swimming);
        UpdateTag(ref tags, ref OnGround, TagOnGround, entity.OnGround);
        UpdateTag(ref tags, ref Alive, TagAlive, entity.Alive);
    }

    protected virtual void EntityAgentTagsUpdate(EntityAgent entityAgent, ref EntityTagSet tags)
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

    protected virtual void EntityAgentHandItemsTagsUpdate(EntityAgent entityAgent, ref EntityTagSet tags)
    {
        ItemStack? handStack = entityAgent.ActiveHandItemSlot?.Itemstack;
        CollectibleObject? item = handStack?.Collectible;
        if (item == null) // This will be the case for 99% of entities, which do not hold anything in their hands
        {
            UpdateTag(ref tags, ref Armed, TagArmed, false);
            UpdateTag(ref tags, ref ArmedMelee, TagArmedMelee, false);
            UpdateTag(ref tags, ref ArmedRanged, TagArmedRanged, false);
            UpdateTag(ref tags, ref HoldingOpenFire, TagHoldingOpenFire, false);
        }
        else
        {
            UpdateTag(ref tags, ref Armed, TagArmed, CollectibleTagWeapon.IsSubsetOf(item.Tags));
            UpdateTag(ref tags, ref ArmedMelee, TagArmedMelee, CollectibleTagWeaponMelee.IsSubsetOf(item.Tags));
            UpdateTag(ref tags, ref ArmedRanged, TagArmedRanged, CollectibleTagWeaponRanged.IsSubsetOf(item.Tags));
            UpdateTag(ref tags, ref HoldingOpenFire, TagHoldingOpenFire, CollectibleTagHasOpenFire.IsSubsetOf(item.Tags));
        }
    }
}
