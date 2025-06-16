using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{
    /// <summary>
    /// A type of in-game collectible object. Extends from <see cref="RegistryObjectType"/>.
    /// This is mainly used to control items (and block's items) when they are in the inventory.
    /// <see cref="ItemType"/>s get most of their data from here, <see cref="BlockType"/>s use this as well as their own specific data.
    /// </summary>
    [DocumentAsJson]
    [JsonObject(MemberSerialization.OptIn)]
    public abstract class CollectibleType : RegistryObjectType
    {
        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Modifiers that can alter the behavior of the item or block, mostly for held interactions.
        /// </summary>
        [JsonProperty]
        public CollectibleBehaviorType[] Behaviors = Array.Empty<CollectibleBehaviorType>();

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>[0, 0, 0]</jsondefault>-->
        /// For light emitting collectibles: hue, saturation and brightness value.
        /// </summary>
        [JsonProperty]
        public byte[] LightHsv = new byte[] { 0, 0, 0 };
        
        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.05</jsondefault>-->
        /// Alpha test value for rendering in gui, fp hand, tp hand or on the ground.
        /// </summary>
        [JsonProperty]
        public float RenderAlphaTest = 0.05f;
        
        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>1</jsondefault>-->
        /// Determines in which kind of bags the item can be stored in.
        /// </summary>
        [JsonProperty]
        public int StorageFlags = 1;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>1</jsondefault>-->
        /// Max amount of collectible that one default inventory slot can hold.
        /// </summary>
        [JsonProperty]
        public int MaxStackSize = 1;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.5</jsondefault>-->
        /// How much damage this collectible deals when used as a weapon.
        /// </summary>
        [JsonProperty]
        public float AttackPower = 0.5f;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0</jsondefault>-->
        /// How many uses does this collectible has when being used. Item disappears at durability 0.
        /// </summary>
        [JsonProperty]
        public int Durability;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.5, 0.5, 0.5</jsondefault>-->
        /// Notional physical size of this collectible, 0.5 x 0.5 x 0.5 meters by default. Explicitly setting a null value in JSON will result in the default 0.5m size
        /// </summary>
        [JsonProperty]
        [Obsolete("Use Size instead from game version 1.20.4 onwards, with the same values")]
        public Size3f Dimensions { get { return Size; } set { Size = value; } }

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.5, 0.5, 0.5</jsondefault>-->
        /// Notional physical size of this collectible, 0.5 x 0.5 x 0.5 meters by default. Explicitly setting a null value in JSON will result in the default 0.5m size
        /// </summary>
        [JsonProperty]
        public Size3f Size = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// From which damage sources does the item takes durability damage.
        /// </summary>
        [JsonProperty]
        public EnumItemDamageSource[] DamagedBy;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// If set, this item will be classified as given tool.
        /// </summary>
        [JsonProperty]
        public EnumTool? Tool = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>1.5</jsondefault>-->
        /// The maximum distance an entity can be for you to attack it with this object.
        /// </summary>
        [JsonProperty]
        public float AttackRange = GlobalConstants.DefaultAttackRange;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Modifies how fast the player can break a block when holding this item
        /// </summary>
        [JsonProperty]
        public Dictionary<EnumBlockMaterial, float> MiningSpeed;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0</jsondefault>-->
        /// The object can mine any blocks with the same or lower tier than this. 
        /// If this object is a weapon, this also determines the object's damage tier.
        /// </summary>
        [JsonProperty]
        public int ToolTier;

        /// <summary>
        /// <!--<jsonoptional>Obsolete</jsonoptional>-->
        /// Deprecated. Use <see cref="ToolTier"/>.
        /// </summary>
        [JsonProperty]
        [Obsolete("Use tool tier")]
        public int MiningTier { get { return ToolTier; } set { ToolTier = value; } }

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>Solid</jsondefault>-->
        /// What kind of matter is this collectible? Liquids are handled and rendered differently than solid blocks.
        /// </summary>
        [JsonProperty]
        public EnumMatterState MatterState = EnumMatterState.Solid;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// If set, defines a specific sound set for this collectible.
        /// </summary>
        [JsonProperty]
        public HeldSounds HeldSounds;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>9999</jsondefault>-->
        /// Determines on whether an object floats on liquids or not. Water has a density of 1000.
        /// </summary>
        [JsonProperty]
        public int MaterialDensity = 9999;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Custom Attributes that're always associated with this collectible.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        public JsonObject Attributes;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// Details about the 3D model of this collectible.
        /// </summary>
        [JsonProperty]
        public CompositeShape Shape = null;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// Used for scaling, rotation or offseting the block when rendered in guis.
        /// </summary>
        [JsonProperty]
        public ModelTransform GuiTransform;

        /// <summary>
        /// <!--<jsonoptional>Obsolete</jsonoptional>-->
        /// Deprecated - Use <see cref="TpHandTransform"/> instead. 
        /// Used for scaling, rotation or offseting the block when rendered in the first person mode hand.
        /// </summary>
        [JsonProperty]
        public ModelTransform FpHandTransform;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// Used for scaling, rotation or offseting the block when rendered in the third person mode hand.
        /// </summary>
        [JsonProperty]
        public ModelTransform TpHandTransform;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// Used for scaling, rotation or offseting the block when rendered in the third person mode offhand.
        /// </summary>
        [JsonProperty]
        public ModelTransform TpOffHandTransform;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// Used for scaling, rotation or offseting the rendered as a dropped item on the ground.
        /// </summary>
        [JsonProperty]
        public ModelTransform GroundTransform;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Details about the texture of this collectible. Used if the shape only has one texture. Use <see cref="Textures"/> if using more than one texture.
        /// </summary>
        [JsonProperty]
        public CompositeTexture Texture;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Details about a set of textures of this collectible. Each string key should correlate to a texture value in this the collectible's shape's textures. You can use <see cref="Texture"/> if only using one texture.
        /// </summary>
        [JsonProperty]
        public Dictionary<string, CompositeTexture> Textures;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Information about the burnable states and results from cooking.
        /// </summary>
        [JsonProperty]
        public CombustibleProperties CombustibleProps = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Information about the nutrition states (e.g. edible properties). Setting this will make the collectible edible.
        /// </summary>
        [JsonProperty]
        public FoodNutritionProperties NutritionProps = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Information about the transitionable states - Should this collectible turn into another item after a period of time?
        /// </summary>
        [JsonProperty]
        public TransitionableProperties[] TransitionableProps = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// If set, the collectible can be ground into something else using a quern.
        /// </summary>
        [JsonProperty]
        public GrindingProperties GrindingProps = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// If set, the collectible can be crushed into something else using a pulverizer.
        /// </summary>
        [JsonProperty]
        public CrushingProperties CrushingProps = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>False</jsondefault>-->
        /// When this item is held, can the player select liquids?
        /// </summary>
        [JsonProperty]
        public bool LiquidSelectable = false;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// A list of creative tabs and variant codes for each. 
        /// </summary>
        [JsonProperty]
        public Dictionary<string, string[]> CreativeInventory = new Dictionary<string, string[]>();

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A list of specific item stacks to place in specific creative tabs.
        /// </summary>
        [JsonProperty]
        public CreativeTabAndStackList[] CreativeInventoryStacks;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>"breakhand"</jsondefault>-->
        /// The animation to play in 3rd person mode when hitting with this collectible
        /// </summary>
        [JsonProperty]
        public string HeldTpHitAnimation = "breakhand";

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// The animation to play in 3rd person mode when holding this collectible in the right hand
        /// </summary>
        [JsonProperty]
        public string HeldRightTpIdleAnimation;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// The animation to play in 3rd person mode when holding this collectible in the left hand
        /// </summary>
        [JsonProperty]
        public string HeldLeftTpIdleAnimation;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>"helditemready"</jsondefault>-->
        /// The animation to play in 3rd person when returning to idle from use in the left hand.
        /// </summary>
        [JsonProperty]
        public string HeldLeftReadyAnimation = "helditemready";

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>"helditemready"</jsondefault>-->
        /// The animation to play in 3rd person when returning to idle from use in the right hand.
        /// </summary>
        [JsonProperty]
        public string HeldRightReadyAnimation = "helditemready";

        /// <summary>
        /// <!--<jsonoptional>Obsolete</jsonoptional>-->
        /// Deprecated. Use <see cref="HeldRightTpIdleAnimation"/> instead. 
        /// </summary>
        [JsonProperty("heldTpIdleAnimation")]
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        private string HeldOldTpIdleAnimation;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>"interactstatic"</jsondefault>-->
        /// The animation to play in 3rd person mod when using this collectible
        /// </summary>
        [JsonProperty]
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
        public string HeldTpUseAnimation = "interactstatic";

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Particles that should spawn in regular intervals from this block or item when held in hands
        /// </summary>
        [JsonProperty]
        public AdvancedParticleProperties[] ParticleProperties = null;

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            OnDeserialized();
        }

        virtual internal void OnDeserialized()
        {
            if (Texture != null)
            {
                if (Textures == null) Textures = new Dictionary<string, CompositeTexture>(1);
                Textures["all"] = Texture;
            }

            if (HeldOldTpIdleAnimation != null && HeldRightTpIdleAnimation == null)
            {
                HeldRightTpIdleAnimation = HeldOldTpIdleAnimation;
            }
        }
    }
}
