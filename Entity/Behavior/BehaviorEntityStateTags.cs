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

        GetTagsIds();

        EntityTagArray tags = entity.Tags;

        TagsInitialUpdate(ref tags);

        if (entity.Tags != tags)
        {
            entity.Tags = tags;
            entity.MarkTagsDirty();
        }
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
    protected EntityTagArray TagSwimming = EntityTagArray.Empty;
    protected EntityTagArray TagFeetInLiquid = EntityTagArray.Empty;
    protected EntityTagArray TagFlying = EntityTagArray.Empty;
    protected EntityTagArray TagOnGround = EntityTagArray.Empty;
    protected EntityTagArray TagMoving = EntityTagArray.Empty;
    protected EntityTagArray TagAlive = EntityTagArray.Empty;
    protected EntityTagArray TagAiming = EntityTagArray.Empty;
    protected EntityTagArray TagSprinting = EntityTagArray.Empty;
    protected EntityTagArray TagSneaking = EntityTagArray.Empty;
    protected EntityTagArray TagArmed = EntityTagArray.Empty;
    protected EntityTagArray TagArmedMelee = EntityTagArray.Empty;
    protected EntityTagArray TagArmedRanged = EntityTagArray.Empty;
    protected EntityTagArray TagHoldingOpenFire = EntityTagArray.Empty;

    // Checked item tags, filled in GetTagsIds() in Initialize()
    protected ItemTagArray ItemTagWeapon = ItemTagArray.Empty;
    protected ItemTagArray ItemTagWeaponMelee = ItemTagArray.Empty;
    protected ItemTagArray ItemTagWeaponRanged = ItemTagArray.Empty;
    protected ItemTagArray ItemTagHasOpenFire = ItemTagArray.Empty;

    // Checked block tags, filled in GetTagsIds() in Initialize()
    protected BlockTagArray BlockTagHasOpenFire = BlockTagArray.Empty;

    protected float TimeSinceUpdateSec = 0;
    protected float UpdatePeriodSec = 1;


    protected virtual void GetTagsIds()
    {
        TagSwimming = new(entity.Api.TagRegistry.EntityTagToTagId("state-swimming"));
        TagFeetInLiquid = new(entity.Api.TagRegistry.EntityTagToTagId("state-feet-in-liquid"));
        TagFlying = new(entity.Api.TagRegistry.EntityTagToTagId("state-flying"));
        TagOnGround = new(entity.Api.TagRegistry.EntityTagToTagId("state-on-ground"));
        TagMoving = new(entity.Api.TagRegistry.EntityTagToTagId("state-moving"));
        TagAlive = new(entity.Api.TagRegistry.EntityTagToTagId("state-alive"));
        TagAiming = new(entity.Api.TagRegistry.EntityTagToTagId("state-aiming"));
        TagSprinting = new(entity.Api.TagRegistry.EntityTagToTagId("state-sprinting"));
        TagSneaking = new(entity.Api.TagRegistry.EntityTagToTagId("state-sneaking"));
        TagArmed = new(entity.Api.TagRegistry.EntityTagToTagId("state-armed"));
        TagArmedMelee = new(entity.Api.TagRegistry.EntityTagToTagId("state-armed-melee"));
        TagArmedRanged = new(entity.Api.TagRegistry.EntityTagToTagId("state-armed-ranged"));

        ItemTagWeapon = new(entity.Api.TagRegistry.ItemTagToTagId("weapon"));
        ItemTagWeaponMelee = new(entity.Api.TagRegistry.ItemTagToTagId("weapon-melee"));
        ItemTagWeaponRanged = new(entity.Api.TagRegistry.ItemTagToTagId("weapon-ranged"));
        ItemTagHasOpenFire = new(entity.Api.TagRegistry.ItemTagToTagId("has-open-fire"));

        BlockTagHasOpenFire = new(entity.Api.TagRegistry.BlockTagToTagId("has-open-fire"));
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
        Swimming = entity.Swimming;
        if (Swimming)
        {
            tags |= TagSwimming;
        }
        else
        {
            tags &= ~TagSwimming;
        }

        FeetInLiquid = entity.FeetInLiquid && !Swimming;
        if (FeetInLiquid)
        {
            tags |= TagFeetInLiquid;
        }
        else
        {
            tags &= ~TagFeetInLiquid;
        }

        OnGround = entity.OnGround;
        if (OnGround)
        {
            tags |= TagOnGround;
        }
        else
        {
            tags &= ~TagOnGround;
        }

        Alive = entity.Alive;
        if (Alive)
        {
            tags |= TagAlive;
        }
        else
        {
            tags &= ~TagAlive;
        }
    }

    protected virtual void EntityAgentTagsInitialUpdate(EntityAgent entityAgent, ref EntityTagArray tags)
    {
        bool moving = entityAgent.Controls.Forward || entityAgent.Controls.Backward || entityAgent.Controls.Right || entityAgent.Controls.Left || entityAgent.Controls.Jump || entityAgent.Controls.Gliding;
        Moving = moving;
        if (Moving)
        {
            tags |= TagMoving;
        }
        else
        {
            tags &= ~TagMoving;
        }

        bool aiming = entityAgent.Controls.IsAiming;
        Aiming = aiming;
        if (Aiming)
        {
            tags |= TagAiming;
        }
        else
        {
            tags &= ~TagAiming;
        }

        bool flying = entityAgent.Controls.IsFlying;
        Flying = flying;
        if (Flying)
        {
            tags |= TagFlying;
        }
        else
        {
            tags &= ~TagFlying;
        }

        bool sneaking = entityAgent.Controls.Sneak;
        Sneaking = sneaking;
        if (Sneaking)
        {
            tags |= TagSneaking;
        }
        else
        {
            tags &= ~TagSneaking;
        }

        bool sprinting = entityAgent.Controls.Sprint;
        Sprinting = sprinting;
        if (Sprinting)
        {
            tags |= TagSprinting;
        }
        else
        {
            tags &= ~TagSprinting;
        }
    }

    protected virtual void EntityAgentHandItemsTagsInitialUpdate(EntityAgent entityAgent, ref EntityTagArray tags)
    {
        ItemTagArray itemTags = entityAgent.RightHandItemSlot?.Itemstack?.Item?.Tags ?? ItemTagArray.Empty;
        BlockTagArray blockTags = entityAgent.RightHandItemSlot?.Itemstack?.Block?.Tags ?? BlockTagArray.Empty;
        itemTags |= entityAgent.LeftHandItemSlot?.Itemstack?.Item?.Tags ?? ItemTagArray.Empty;
        blockTags |= entityAgent.LeftHandItemSlot?.Itemstack?.Block?.Tags ?? BlockTagArray.Empty;

        bool weapon = itemTags.ContainsAll(ItemTagWeapon);
        bool weaponMelee = itemTags.ContainsAll(ItemTagWeaponMelee);
        bool weaponRanged = itemTags.ContainsAll(ItemTagWeaponRanged);
        bool hasOpenFire = itemTags.ContainsAll(ItemTagHasOpenFire) || blockTags.ContainsAll(BlockTagHasOpenFire);

        Armed = weapon;
        ArmedMelee = weaponMelee;
        ArmedRanged = weaponRanged;
        HoldingOpenFire = hasOpenFire;

        if (Armed)
        {
            tags |= TagArmed;
        }
        else
        {
            tags &= ~TagArmed;
        }

        if (ArmedMelee)
        {
            tags |= TagArmedMelee;
        }
        else
        {
            tags &= ~TagArmedMelee;
        }

        if (ArmedRanged)
        {
            tags |= TagArmedRanged;
        }
        else
        {
            tags &= ~TagArmedRanged;
        }

        if (HoldingOpenFire)
        {
            tags |= TagHoldingOpenFire;
        }
        else
        {
            tags &= ~TagHoldingOpenFire;
        }
    }

    protected virtual void EntityTagsUpdate(ref EntityTagArray tags)
    {
        if (entity.Swimming != Swimming)
        {
            Swimming = entity.Swimming;
            if (Swimming)
            {
                tags |= TagSwimming;
            }
            else
            {
                tags &= ~TagSwimming;
            }
        }

        if (FeetInLiquid != entity.FeetInLiquid && !Swimming)
        {
            FeetInLiquid = entity.FeetInLiquid && !Swimming;
            if (FeetInLiquid)
            {
                tags |= TagFeetInLiquid;
            }
            else
            {
                tags &= ~TagFeetInLiquid;
            }
        }

        if (OnGround != entity.OnGround)
        {
            OnGround = entity.OnGround;
            if (OnGround)
            {
                tags |= TagOnGround;
            }
            else
            {
                tags &= ~TagOnGround;
            }
        }

        if (entity.Alive != Alive)
        {
            Alive = entity.Alive;
            if (Alive)
            {
                tags |= TagAlive;
            }
            else
            {
                tags &= ~TagAlive;
            }
        }
    }

    protected virtual void EntityAgentTagsUpdate(EntityAgent entityAgent, ref EntityTagArray tags)
    {
        bool moving = entityAgent.Controls.Forward || entityAgent.Controls.Backward || entityAgent.Controls.Right || entityAgent.Controls.Left || entityAgent.Controls.Jump || entityAgent.Controls.Gliding;
        if (moving != Moving)
        {
            Moving = moving;
            if (Moving)
            {
                tags |= TagMoving;
            }
            else
            {
                tags &= ~TagMoving;
            }
        }

        bool aiming = entityAgent.Controls.IsAiming;
        if (aiming != Aiming)
        {
            Aiming = aiming;
            if (Aiming)
            {
                tags |= TagAiming;
            }
            else
            {
                tags &= ~TagAiming;
            }
        }

        bool flying = entityAgent.Controls.IsFlying;
        if (flying != Flying)
        {
            Flying = flying;
            if (Flying)
            {
                tags |= TagFlying;
            }
            else
            {
                tags &= ~TagFlying;
            }
        }

        bool sneaking = entityAgent.Controls.Sneak;
        if (sneaking != Sneaking)
        {
            Sneaking = sneaking;
            if (Sneaking)
            {
                tags |= TagSneaking;
            }
            else
            {
                tags &= ~TagSneaking;
            }
        }

        bool sprinting = entityAgent.Controls.Sprint;
        if (sprinting != Sprinting)
        {
            Sprinting = sprinting;
            if (Sprinting)
            {
                tags |= TagSprinting;
            }
            else
            {
                tags &= ~TagSprinting;
            }
        }
    }

    protected virtual void EntityAgentHandItemsTagsUpdate(EntityAgent entityAgent, ref EntityTagArray tags)
    {
        ItemTagArray itemTags = entityAgent.ActiveHandItemSlot?.Itemstack?.Item?.Tags ?? ItemTagArray.Empty;
        BlockTagArray blockTags = entityAgent.ActiveHandItemSlot?.Itemstack?.Block?.Tags ?? BlockTagArray.Empty;

        bool weapon = itemTags.ContainsAll(ItemTagWeapon);
        bool weaponMelee = itemTags.ContainsAll(ItemTagWeaponMelee);
        bool weaponRanged = itemTags.ContainsAll(ItemTagWeaponRanged);
        bool hasOpenFire = itemTags.ContainsAll(ItemTagHasOpenFire) || blockTags.ContainsAll(BlockTagHasOpenFire);

        if (Armed != weapon)
        {
            Armed = weapon;
            if (Armed)
            {
                tags |= TagArmed;
            }
            else
            {
                tags &= ~TagArmed;
            }
        }

        if (ArmedMelee != weaponMelee)
        {
            ArmedMelee = weaponMelee;
            if (ArmedMelee)
            {
                tags |= TagArmedMelee;
            }
            else
            {
                tags &= ~TagArmedMelee;
            }
        }

        if (ArmedRanged != weaponRanged)
        {
            ArmedRanged = weaponRanged;
            if (ArmedRanged)
            {
                tags |= TagArmedRanged;
            }
            else
            {
                tags &= ~TagArmedRanged;
            }
        }

        if (HoldingOpenFire != hasOpenFire)
        {
            HoldingOpenFire = hasOpenFire;
            if (HoldingOpenFire)
            {
                tags |= TagHoldingOpenFire;
            }
            else
            {
                tags &= ~TagHoldingOpenFire;
            }
        }
    }
}
