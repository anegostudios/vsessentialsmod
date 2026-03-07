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
        /// Modifiers that can alter the behavior of the item or block, mostly for held interactions.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public CollectibleBehaviorType[] Behaviors = Array.Empty<CollectibleBehaviorType>();

        /// <summary>
        /// For light emitting collectibles: hue, saturation and brightness value. See also http://tyron.at/vs/vslightwheel.html for all possible values.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "[0, 0, 0]")]
        public byte[] LightHsv = new byte[] { 0, 0, 0 };
        
        /// <summary>
        /// Alpha test value for rendering in gui, fp hand, tp hand or on the ground.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0.05")]
        public float RenderAlphaTest = 0.05f;
        
        /// <summary>
        /// Determines in which kind of bags the item can be stored in.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "1")]
        public int StorageFlags = 1;

        /// <summary>
        /// Max amount of collectible that one default inventory slot can hold.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "1")]
        public int MaxStackSize = 1;

        /// <summary>
        /// How much damage this collectible deals when used as a weapon.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0.5")]
        public float AttackPower = 0.5f;

        /// <summary>
        /// How many uses does this collectible has when being used. Item disappears at durability 0.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0")]
        public int Durability;

        /// <summary>
        /// Notional physical size of this collectible, 0.5 x 0.5 x 0.5 meters by default. Explicitly setting a null value in JSON will result in the default 0.5m size
        /// </summary>
        [JsonProperty]
        [Obsolete("Use Size instead from game version 1.20.4 onwards, with the same values")]
        [DocumentAsJson("Optional", "0.5, 0.5, 0.5")]
        public Size3f Dimensions { get { return Size; } set { Size = value; } }

        /// <summary>
        /// Notional physical size of this collectible, 0.5 x 0.5 x 0.5 meters by default. Explicitly setting a null value in JSON will result in the default 0.5m size
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0.5, 0.5, 0.5")]
        public Size3f Size = null;

        /// <summary>
        /// From which damage sources does the item takes durability damage.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public EnumItemDamageSource[] DamagedBy;

        /// <summary>
        /// If set, this item will be classified as given tool.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public EnumTool? Tool = null;

        /// <summary>
        /// The maximum distance an entity can be for you to attack it with this object.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "1.5")]
        public float AttackRange = GlobalConstants.DefaultAttackRange;

        /// <summary>
        /// Modifies how fast the player can break a block when holding this item
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public Dictionary<EnumBlockMaterial, float> MiningSpeed;

        /// <summary>
        /// The object can mine any blocks with the same or lower tier than this. 
        /// If this object is a weapon, this also determines the object's damage tier.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0")]
        public int ToolTier;

        /// <summary>
        /// Deprecated. Use <see cref="ToolTier"/>.
        /// </summary>
        [JsonProperty]
        [Obsolete("Use tool tier")]
        [DocumentAsJson("Obsolete")]
        public int MiningTier { get { return ToolTier; } set { ToolTier = value; } }

        /// <summary>
        /// What kind of matter is this collectible? Liquids are handled and rendered differently than solid blocks.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "Solid")]
        public EnumMatterState MatterState = EnumMatterState.Solid;

        /// <summary>
        /// If set, defines a specific sound set for this collectible.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public HeldSounds HeldSounds;

        /// <summary>
        /// Determines on whether an object floats on liquids or not. Water has a density of 1000.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "9999")]
        public int MaterialDensity = 9999;

        /// <summary>
        /// Custom Attributes that're always associated with this collectible.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        [DocumentAsJson("Optional", "None")]
        public JsonObject Attributes;

        /// <summary>
        /// Details about the 3D model of this collectible.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "None")]
        public CompositeShape Shape = null;

        /// <summary>
        /// Used for scaling, rotation or offseting the block when rendered in guis.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "None")]
        public ModelTransform GuiTransform;

        /// <summary>
        /// Deprecated - Use <see cref="TpHandTransform"/> instead. 
        /// Used for scaling, rotation or offseting the block when rendered in the first person mode hand.
        /// </summary>
        [JsonProperty]
        [Obsolete("Use TpHandTransform instead")]
        [DocumentAsJson("Obsolete")]
        public ModelTransform FpHandTransform;

        /// <summary>
        /// Used for scaling, rotation or offseting the block when rendered in the third person mode hand.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "None")]
        public ModelTransform TpHandTransform;

        /// <summary>
        /// Used for scaling, rotation or offseting the block when rendered in the third person mode offhand.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "None")]
        public ModelTransform TpOffHandTransform;

        /// <summary>
        /// Used for scaling, rotation or offseting the rendered as a dropped item on the ground.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "None")]
        public ModelTransform GroundTransform;

        /// <summary>
        /// Details about the texture of this collectible. Used if the shape only has one texture. Use <see cref="Textures"/> if using more than one texture.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public CompositeTexture Texture;

        /// <summary>
        /// Details about a set of textures of this collectible. Each string key should correlate to a texture value in this the collectible's shape's textures. You can use <see cref="Texture"/> if only using one texture.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public Dictionary<string, CompositeTexture> Textures;

        /// <summary>
        /// Information about the burnable states and results from cooking.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public CombustibleProperties CombustibleProps = null;

        /// <summary>
        /// Information about the nutrition states (e.g. edible properties). Setting this will make the collectible edible.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public FoodNutritionProperties NutritionProps = null;

        /// <summary>
        /// Information about the transitionable states - Should this collectible turn into another item after a period of time?
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public TransitionableProperties[] TransitionableProps = null;

        /// <summary>
        /// If set, the collectible can be ground into something else using a quern.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public GrindingProperties GrindingProps = null;

        /// <summary>
        /// If set, the collectible can be crushed into something else using a pulverizer.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public CrushingProperties CrushingProps = null;

        /// <summary>
        /// When this item is held, can the player select liquids?
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public bool LiquidSelectable = false;

        /// <summary>
        /// A list of creative tabs and variant codes for each. 
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "None")]
        public Dictionary<string, string[]> CreativeInventory = new Dictionary<string, string[]>();

        /// <summary>
        /// A list of specific item stacks to place in specific creative tabs.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public CreativeTabAndStackList[] CreativeInventoryStacks;

        /// <summary>
        /// The animation to play in 3rd person mode when hitting with this collectible
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "breakhand")]
        public string HeldTpHitAnimation = "breakhand";

        /// <summary>
        /// The animation to play in 3rd person mode when holding this collectible in the right hand
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public string HeldRightTpIdleAnimation;

        /// <summary>
        /// The animation to play in 3rd person mode when holding this collectible in the left hand
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public string HeldLeftTpIdleAnimation;

        /// <summary>
        /// The animation to play in 3rd person when returning to idle from use in the left hand.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "helditemready")]
        public string HeldLeftReadyAnimation = "helditemready";

        /// <summary>
        /// The animation to play in 3rd person when returning to idle from use in the right hand.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "helditemready")]
        public string HeldRightReadyAnimation = "helditemready";

        /// <summary>
        /// Deprecated. Use <see cref="HeldRightTpIdleAnimation"/> instead. 
        /// </summary>
        [JsonProperty("heldTpIdleAnimation")]
        [DocumentAsJson("Obsolete")]
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        private string HeldOldTpIdleAnimation;

        /// <summary>
        /// The animation to play in 3rd person mod when using this collectible
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "interactstatic")]
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
        public string HeldTpUseAnimation = "interactstatic";

        /// <summary>
        /// Particles that should spawn in regular intervals from this block or item when held in hands
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public AdvancedParticleProperties[] ParticleProperties = null;

        /// <summary>
        /// If set, the breaking particles will be taken from this texture, otherwise it'll just pick the first texture
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public string ParticlesTextureCode = null;

        /// <summary>
        /// List of tags that collectible has. Used for categorizing.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public TagSet Tags;


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
