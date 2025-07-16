using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{
    /// <summary>
    /// An entity type.
    /// Any json files inside of assets/entities will be loaded in as this type.
    /// </summary>
    [DocumentAsJson]
    [JsonObject(MemberSerialization.OptIn)]
    public class EntityType : RegistryObjectType
    {
        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>Land</jsondefault>-->
        /// Natural habitat of the entity. Decides whether to apply gravity or not.
        /// </summary>
        [JsonProperty]
        public EnumHabitat Habitat = EnumHabitat.Land;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional>-->
        /// Sets both <see cref="CollisionBoxSize"/> and <see cref="SelectionBoxSize"/>.
        /// </summary>
        [JsonProperty]
        public Vec2f HitBoxSize
        {
            get { return null; }
            set { CollisionBoxSize = value; SelectionBoxSize = value; }
        }

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional>-->
        /// Sets both <see cref="DeadCollisionBoxSize"/> and <see cref="DeadSelectionBoxSize"/>.
        /// </summary>
        [JsonProperty]
        public Vec2f DeadHitBoxSize
        {
            get { return null; }
            set { DeadCollisionBoxSize = value; DeadSelectionBoxSize = value; }
        }

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.5, 0.5</jsondefault>-->
        /// The size of the entity's hitbox, in meters.
        /// </summary>
        [JsonProperty]
        public Vec2f CollisionBoxSize = new Vec2f(0.5f, 0.5f);

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.5, 0.25</jsondefault>-->
        /// The size of the hitbox, in meters, while the entity is dead.
        /// </summary>
        [JsonProperty]
        public Vec2f DeadCollisionBoxSize = new Vec2f(0.5f, 0.25f);

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>CollisionBoxSize</jsondefault>-->
        /// The size of the entity's hitbox. Defaults to <see cref="CollisionBoxSize"/>.
        /// </summary>
        [JsonProperty]
        public Vec2f SelectionBoxSize = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>DeadCollisionBoxSize</jsondefault>-->
        /// The size of the hitbox while the entity is dead. Defaults to <see cref="DeadCollisionBoxSize"/>.
        /// </summary>
        [JsonProperty]
        public Vec2f DeadSelectionBoxSize = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.1</jsondefault>-->
        /// How high the camera should be placed if this entity were to be controlled by the player.
        /// </summary>
        [JsonProperty]
        public double EyeHeight = 0.1;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>EyeHeight</jsondefault>-->
        /// The eye height of the entity when swimming. Defaults to be same as <see cref="EyeHeight"/>.
        /// </summary>
        [JsonProperty]
        public double? SwimmingEyeHeight = null;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>25</jsondefault>-->
        /// The mass of this type of entity in kilograms, on average.
        /// </summary>
        [JsonProperty]
        public float Weight = 25;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>false</jsondefault>-->
        /// If true the entity can climb on walls.
        /// </summary>
        [JsonProperty]
        public bool CanClimb = false;


        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>false</jsondefault>-->
        /// If true the entity can climb anywhere.
        /// </summary>
        [JsonProperty]
        public bool CanClimbAnywhere = false;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>1</jsondefault>-->
        /// If less than one, mitigates fall damage (e.g. could be used for mountainous creatures); if more than one, increases fall damage.
        /// </summary>
        [JsonProperty]
        public float FallDamageMultiplier = 1.0f;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.5</jsondefault>-->
        /// The minimum distance from a block that a creature has to be to climb it.
        /// </summary>
        [JsonProperty]
        public float ClimbTouchDistance = 0.5f;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>false</jsondefault>-->
        /// Should the entity rotate to 'stand' on the direction it's climbing?
        /// </summary>
        [JsonProperty]
        public bool RotateModelOnClimb = false;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0</jsondefault>-->
        /// The resistance to being pushed back by an impact. Value will vary based on mob weight.
        /// </summary>
        [JsonProperty]
        public float KnockbackResistance = 0f;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Specific attributes for the entity. Contents can vary per entity.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        public JsonObject Attributes;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A list of properties common to each client/server entity behavior.
        /// Key is a behavior code, and value is a set of attributes. Attributes will get merged with any matching client/server entity behaviors.
        /// </summary>
        [JsonProperty(ItemConverterType = typeof(JsonAttributesConverter))]
        public Dictionary<string, JsonObject> BehaviorConfigs;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The client-side properties of the entity. Usually related to rendering, precise physics calculations, and behaviors.
        /// </summary>
        [JsonProperty]
        public ClientEntityConfig Client;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The server-side properties of the entity. Usually related to spawning, general physics, AI tasks, and other behaviors..
        /// </summary>
        [JsonProperty]
        public ServerEntityConfig Server;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// The sounds that this entity can make. Keys to use are:<br/>
        /// - "hurt"<br/>
        /// - "death"<br/>
        /// - "idle"<br/>
        /// - "swim" (player only)<br/>
        /// - "eat" (player only)
        /// </summary>
        [JsonProperty]
        public Dictionary<string, AssetLocation> Sounds;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0.3</jsondefault>-->
        /// The chance that an idle sound will play for the entity.
        /// </summary>
        [JsonProperty]
        public float IdleSoundChance = 0.05f;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>24</jsondefault>-->
        /// The sound range for the idle sound in blocks.
        /// </summary>
        [JsonProperty]
        public float IdleSoundRange = 24;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// The drops for the entity when they are killed.
        /// </summary>
        [JsonProperty]
        public BlockDropItemStack[] Drops;


        public EntityProperties CreateProperties(ICoreAPI api)
        {
            BlockDropItemStack[] DropsCopy;
            if (Drops == null)
                DropsCopy = null;
            else
            {
                DropsCopy = new BlockDropItemStack[Drops.Length];
                for (int i = 0; i < DropsCopy.Length; i++)
                    DropsCopy[i] = Drops[i].Clone();
            }


            EntityProperties properties = new EntityProperties()
            {
                Code = Code,
                Tags = api.TagRegistry.EntityTagsToTagArray(Tags),
                Variant = new (Variant),
                Class = Class,
                Habitat = Habitat,
                CollisionBoxSize = CollisionBoxSize,
                DeadCollisionBoxSize = DeadCollisionBoxSize,
                SelectionBoxSize = SelectionBoxSize,
                DeadSelectionBoxSize = DeadSelectionBoxSize,
                Weight = Weight,
                CanClimb = CanClimb,
                CanClimbAnywhere = CanClimbAnywhere,
                FallDamage = FallDamageMultiplier > 0,
                FallDamageMultiplier = FallDamageMultiplier,
                ClimbTouchDistance = ClimbTouchDistance,
                RotateModelOnClimb = RotateModelOnClimb,
                KnockbackResistance = KnockbackResistance,
                Attributes = Attributes,
                Sounds = Sounds == null ? new Dictionary<string, AssetLocation>() : new Dictionary<string, AssetLocation>(Sounds),
                IdleSoundChance = IdleSoundChance,
                IdleSoundRange = IdleSoundRange,
                Drops = DropsCopy,
                EyeHeight = EyeHeight,
                SwimmingEyeHeight = SwimmingEyeHeight ?? EyeHeight
            };

            if (Client != null)
            {
                properties.Client = new EntityClientProperties(Client.Behaviors, BehaviorConfigs)
                {
                    RendererName = Client.Renderer,
                    Textures = new FastSmallDictionary<string, CompositeTexture>(Client.Textures),
                    GlowLevel = Client.GlowLevel,
                    PitchStep = Client.PitchStep,
                    Shape = Client.Shape,
                    Size = Client.Size,
                    SizeGrowthFactor = Client.SizeGrowthFactor,
                    Animations = Client.Animations,
                    AnimationsByMetaCode = Client.AnimationsByMetaCode,
                };
            }

            if (Server != null)
            {
                properties.Server = new EntityServerProperties(Server.Behaviors, BehaviorConfigs)
                {
                    Attributes = Server.Attributes?.ToAttribute() as TreeAttribute,
                    SpawnConditions = Server.SpawnConditions
                };
            }

            return properties;
        }


        internal override RegistryObjectType CreateAndPopulate(ICoreServerAPI api, AssetLocation fullcode, JObject jobject, JsonSerializer deserializer, API.Datastructures.OrderedDictionary<string, string> variant)
        {
            return CreateResolvedType<EntityType>(api, fullcode, jobject, deserializer, variant);
        }
    }

    /// <summary>
    /// Specific configuration settings for entities on the client-side.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///"client": {
	///	"renderer": "Shape",
	///	"textures": {
	///		"material": { "base": "block/stone/rock/{rock}1" }
	///	},
	///	"shape": { "base": "item/stone" },
	///	"size": 1,
	///	"behaviors": [
	///		{ "code": "passivephysics" },
	///		{ "code": "interpolateposition" }
	///	]
	///},
    /// </code>
    /// </example>
    [DocumentAsJson]
    public class ClientEntityConfig
    {
        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// Name of the renderer system that draws this entity.
        /// Vanilla Entity Renderer Systems are:<br/>
        /// - Item<br/>
        /// - Dummy<br/>
        /// - BlockFalling<br/>
        /// - Shape<br/>
        /// - PlayerShape<br/>
        /// - EchoChamber<br/>
        /// You will likely want to use Shape.
        /// </summary>
        [JsonProperty]
        public string Renderer;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// A list of all available textures for the entity. First texture in the list will be the default. 
        /// </summary>
        [JsonProperty]
        public Dictionary<string, CompositeTexture> Textures { get; set; } = new Dictionary<string, CompositeTexture>();

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// Sets a single texture. It is recommended to specify texture keys by using <see cref="Textures"/> instead of this.
        /// </summary>
        [JsonProperty]
        protected CompositeTexture Texture;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0</jsondefault>-->
        /// The glow level for the entity.
        /// </summary>
        [JsonProperty]
        public int GlowLevel = 0;

        /// <summary>
        /// <!--<jsonoptional>Required</jsonoptional>-->
        /// The shape of the entity. Must be set unless <see cref="Renderer"/> is not set to "Shape".
        /// </summary>
        [JsonProperty]
        public CompositeShape Shape;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A list of all client-side behaviors for the entity.
        /// </summary>
        [JsonProperty(ItemConverterType = typeof(JsonAttributesConverter))]
        public JsonObject[] Behaviors;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>1</jsondefault>-->
        /// The size of the entity.
        /// </summary>
        [JsonProperty]
        public float Size = 1f;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>0</jsondefault>-->
        /// The rate at which the entity's size grows with age - used for chicks and other small baby animals.
        /// </summary>
        [JsonProperty]
        public float SizeGrowthFactor = 0f;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// The animation data for the entity.
        /// </summary>
        [JsonProperty]
        public AnimationMetaData[] Animations;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>true</jsondefault>-->
        /// Makes entities pitch forward and backwards when stepping.
        /// </summary>
        [JsonProperty]
        public bool PitchStep = true;

        public Dictionary<string, AnimationMetaData> AnimationsByMetaCode = new Dictionary<string, AnimationMetaData>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the first texture in Textures dict
        /// </summary>
        public CompositeTexture FirstTexture { get { return (Textures == null || Textures.Count == 0) ? null : Textures.First().Value; } }

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            if (Texture != null)
            {
                Textures["all"] = Texture;
            }
            Init();
        }
        
        public void Init()
        {
            if (Animations != null)
            {
                for (int i = 0; i < Animations.Length; i++)
                {
                    AnimationMetaData animMeta = Animations[i];
                    if (animMeta.Animation != null) AnimationsByMetaCode[animMeta.Code] = animMeta;
                }
            }
        }
        
    }

    /// <summary>
    /// Specific configuration settings for entities on the server-side.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///"server": {
	///	"behaviors": [
	///		{
	///			"code": "passivephysics",
	///			"groundDragFactor": 1,
	///			"airDragFactor": 0.25,
	///			"gravityFactor": 0.75
	///		},
	///		{
	///			"code": "despawn",
	///			"minSeconds": 600
	///		}
	///	]
	///},
    /// </code>
    /// </example>
    [DocumentAsJson]
    public class ServerEntityConfig
    {
        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A list of all server-side behaviors for the entity.
        /// </summary>
        [JsonProperty(ItemConverterType = typeof(JsonAttributesConverter))]
        public JsonObject[] Behaviors;

        /// <summary>
        /// <!--<jsonoptional>Optional</jsonoptional><jsondefault>None</jsondefault>-->
        /// A set of server-side attributes passed to the entity.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        public JsonObject Attributes;

        /// <summary>
        /// <!--<jsonoptional>Recommended</jsonoptional><jsondefault>None</jsondefault>-->
        /// The spawn conditions for the entity. Without this, the entity will not spawn anywhere.
        /// </summary>
        [JsonProperty]
        public SpawnConditions SpawnConditions;
    }

}
