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

        TagSetFast tags = entity.Tags;

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

        TagSetFast tags = entity.Tags;

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
    protected static TagSetFast TagSwimming;
    protected static TagSetFast TagFeetInLiquid;
    protected static TagSetFast TagFlying;
    protected static TagSetFast TagOnGround;
    protected static TagSetFast TagMoving;
    protected static TagSetFast TagAlive;
    protected static TagSetFast TagAiming;
    protected static TagSetFast TagSprinting;
    protected static TagSetFast TagSneaking;
    protected static TagSetFast TagArmed;
    protected static TagSetFast TagArmedMelee;
    protected static TagSetFast TagArmedRanged;
    protected static TagSetFast TagHoldingOpenFire;

    protected static TagSetFast TagMaskInitialUpdate;
    protected static TagSetFast TagMaskAgentInitialUpdate;


    // Checked collectible tags, filled in GetTagsIds() in Initialize()
    protected static TagSet CollectibleTagWeapon = TagSet.Empty;
    protected static TagSet CollectibleTagWeaponMelee = TagSet.Empty;
    protected static TagSet CollectibleTagWeaponRanged = TagSet.Empty;
    protected static TagSet CollectibleTagHasOpenFire = TagSet.Empty;

    protected float TimeSinceUpdateSec = 0;
    protected float UpdatePeriodSec = 1;


    public static void SetupTagIds(ICoreAPI api)
    {
        // These are pre-registered, otherwise we would need to RegisterAndCreate here
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagSwimming, "state-swimming");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagFeetInLiquid, "state-feet-in-liquid");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagFlying, "state-flying");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagOnGround, "state-on-ground");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagMoving, "state-moving");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagAlive, "state-alive");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagAiming, "state-aiming");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagSprinting, "state-sprinting");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagSneaking, "state-sneaking");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagArmed, "state-armed");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagArmedMelee, "state-armed-melee");
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out TagArmedRanged, "state-armed-ranged");

        TagMaskInitialUpdate = ~(TagSwimming | TagFeetInLiquid | TagOnGround | TagAlive);
        TagMaskAgentInitialUpdate = ~(TagMoving | TagAiming | TagFlying | TagSneaking | TagSprinting);

        // These are pre-registered, otherwise we would need to RegisterAndCreate here
        api.CollectibleTagRegistry.TryCreateTagSetAndLogIssues(out CollectibleTagWeapon, "weapon");
        api.CollectibleTagRegistry.TryCreateTagSetAndLogIssues(out CollectibleTagWeaponMelee, "weapon-melee");
        api.CollectibleTagRegistry.TryCreateTagSetAndLogIssues(out CollectibleTagWeaponRanged, "weapon-ranged");
        api.CollectibleTagRegistry.TryCreateTagSetAndLogIssues(out CollectibleTagHasOpenFire, "has-open-fire");
    }

    protected virtual void TagsInitialUpdate(ref TagSetFast tags)
    {
        EntityTagsInitialUpdate(ref tags);

        if (entity is EntityAgent entityAgent)
        {
            EntityAgentTagsInitialUpdate(entityAgent, ref tags);
            EntityAgentHandItemsTagsInitialUpdate(entityAgent, ref tags);
        }
    }

    protected virtual void TagsUpdate(ref TagSetFast tags)
    {
        EntityTagsUpdate(ref tags);

        if (entity is EntityAgent entityAgent)
        {
            EntityAgentTagsUpdate(entityAgent, ref tags);
            EntityAgentHandItemsTagsUpdate(entityAgent, ref tags);
        }
    }

    protected virtual void EntityTagsInitialUpdate(ref TagSetFast tags)
    {
        tags &= TagMaskInitialUpdate;

        EntityTagsUpdate(ref tags);
    }

    protected virtual void EntityAgentTagsInitialUpdate(EntityAgent entityAgent, ref TagSetFast tags)
    {
        tags &= TagMaskAgentInitialUpdate;

        EntityAgentTagsUpdate(entityAgent, ref tags);
    }

    protected virtual void EntityAgentHandItemsTagsInitialUpdate(EntityAgent entityAgent, ref TagSetFast tags)
    {
        ItemStack? rightHandStack = entityAgent.RightHandItemSlot?.Itemstack;
        TagSet rightHandItemTags = rightHandStack?.Collectible?.Tags ?? TagSet.Empty;
        Armed           = rightHandItemTags.Overlaps(CollectibleTagWeapon);
        ArmedMelee      = rightHandItemTags.Overlaps(CollectibleTagWeaponMelee);
        ArmedRanged     = rightHandItemTags.Overlaps(CollectibleTagWeaponRanged);
        HoldingOpenFire = rightHandItemTags.Overlaps(CollectibleTagHasOpenFire);

        ItemStack? leftHandStack = entityAgent.LeftHandItemSlot?.Itemstack;
        TagSet leftHandItemTags = leftHandStack?.Collectible?.Tags ?? TagSet.Empty;
        Armed           = Armed           || leftHandItemTags.Overlaps(CollectibleTagWeapon);
        ArmedMelee      = ArmedMelee      || leftHandItemTags.Overlaps(CollectibleTagWeaponMelee);
        ArmedRanged     = ArmedRanged     || leftHandItemTags.Overlaps(CollectibleTagWeaponRanged);
        HoldingOpenFire = HoldingOpenFire || leftHandItemTags.Overlaps(CollectibleTagHasOpenFire);


        InitializeTag(ref tags, Armed, TagArmed);
        InitializeTag(ref tags, ArmedMelee, TagArmedMelee);
        InitializeTag(ref tags, ArmedRanged, TagArmedRanged);
        InitializeTag(ref tags, HoldingOpenFire, TagHoldingOpenFire);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void UpdateTag(ref TagSetFast tags, ref bool storedValue, TagSetFast mask, bool newValue)
    {
        if (storedValue == newValue) return;

        storedValue = newValue;

        if (newValue)   tags |=  mask;
        else            tags &= ~mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void InitializeTag(ref TagSetFast tags, bool newValue, TagSetFast mask)
    {
        if (newValue)   tags |=  mask;
        else            tags &= ~mask;
    }

    protected virtual void EntityTagsUpdate(ref TagSetFast tags)
    {
        UpdateTag(ref tags, ref Swimming, TagSwimming, entity.Swimming);
        UpdateTag(ref tags, ref FeetInLiquid, TagFeetInLiquid, entity.FeetInLiquid && !Swimming);
        UpdateTag(ref tags, ref OnGround, TagOnGround, entity.OnGround);
        UpdateTag(ref tags, ref Alive, TagAlive, entity.Alive);
    }

    protected virtual void EntityAgentTagsUpdate(EntityAgent entityAgent, ref TagSetFast tags)
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

    protected virtual void EntityAgentHandItemsTagsUpdate(EntityAgent entityAgent, ref TagSetFast tags)
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
            UpdateTag(ref tags, ref Armed, TagArmed, CollectibleTagWeapon.Overlaps(item.Tags));
            UpdateTag(ref tags, ref ArmedMelee, TagArmedMelee, CollectibleTagWeaponMelee.Overlaps(item.Tags));
            UpdateTag(ref tags, ref ArmedRanged, TagArmedRanged, CollectibleTagWeaponRanged.Overlaps(item.Tags));
            UpdateTag(ref tags, ref HoldingOpenFire, TagHoldingOpenFire, CollectibleTagHasOpenFire.Overlaps(item.Tags));
        }
    }
}
