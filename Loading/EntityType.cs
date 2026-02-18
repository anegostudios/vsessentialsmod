
#nullable disable

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
using Vintagestory.API.Util;

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
        /// Natural habitat of the entity. Decides whether to apply gravity or not.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "Land")]
        public EnumHabitat Habitat = EnumHabitat.Land;

        /// <summary>
        /// Sets both <see cref="CollisionBoxSize"/> and <see cref="SelectionBoxSize"/>.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public Vec2f HitBoxSize
        {
            get { return null; }
            set { CollisionBoxSize = value; SelectionBoxSize = value; }
        }

        /// <summary>
        /// Sets both <see cref="DeadCollisionBoxSize"/> and <see cref="DeadSelectionBoxSize"/>.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public Vec2f DeadHitBoxSize
        {
            get { return null; }
            set { DeadCollisionBoxSize = value; DeadSelectionBoxSize = value; }
        }

        /// <summary>
        /// The size of the entity's hitbox, in meters.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0.5, 0.5")]
        public Vec2f CollisionBoxSize = new Vec2f(0.5f, 0.5f);

        /// <summary>
        /// The size of the hitbox, in meters, while the entity is dead.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0.5, 0.25")]
        public Vec2f DeadCollisionBoxSize = new Vec2f(0.5f, 0.25f);

        /// <summary>
        /// The size of the entity's hitbox. Defaults to <see cref="CollisionBoxSize"/>.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "CollisionBoxSize")]
        public Vec2f SelectionBoxSize = null;

        /// <summary>
        /// The size of the hitbox while the entity is dead. Defaults to <see cref="DeadCollisionBoxSize"/>.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "DeadCollisionBoxSize")]
        public Vec2f DeadSelectionBoxSize = null;

        /// <summary>
        /// How high the camera should be placed if this entity were to be controlled by the player.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0.1")]
        public double EyeHeight = 0.1;

        /// <summary>
        /// The eye height of the entity when swimming. Defaults to be same as <see cref="EyeHeight"/>.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "EyeHeight")]
        public double? SwimmingEyeHeight = null;

        /// <summary>
        /// The mass of this type of entity in kilograms, on average.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "25")]
        public float Weight = 25;

        /// <summary>
        /// If true the entity can climb on walls.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public bool CanClimb = false;


        /// <summary>
        /// If true the entity can climb anywhere.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public bool CanClimbAnywhere = false;

        /// <summary>
        /// If less than one, mitigates fall damage (e.g. could be used for mountainous creatures); if more than one, increases fall damage.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "1")]
        public float FallDamageMultiplier = 1.0f;

        /// <summary>
        /// The minimum distance from a block that a creature has to be to climb it.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0.5")]
        public float ClimbTouchDistance = 0.5f;

        /// <summary>
        /// Should the entity rotate to 'stand' on the direction it's climbing?
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public bool RotateModelOnClimb = false;

        /// <summary>
        /// The resistance to being pushed back by an impact. Value will vary based on mob weight.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0")]
        public float KnockbackResistance = 0f;

        /// <summary>
        /// Specific attributes for the entity. Contents can vary per entity.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        [DocumentAsJson("Optional", "None")]
        public JsonObject Attributes;

        /// <summary>
        /// A list of properties common to each client/server entity behavior.
        /// Key is a behavior code, and value is a set of attributes. Attributes will get merged with any matching client/server entity behaviors.
        /// </summary>
        [JsonProperty(ItemConverterType = typeof(JsonAttributesConverter))]
        [DocumentAsJson("Optional", "None")]
        public Dictionary<string, JsonObject> BehaviorConfigs;

        /// <summary>
        /// The client-side properties of the entity. Usually related to rendering, precise physics calculations, and behaviors.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Required")]
        public ClientEntityConfig Client;

        /// <summary>
        /// The server-side properties of the entity. Usually related to spawning, general physics, AI tasks, and other behaviors..
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Required")]
        public ServerEntityConfig Server;

        /// <summary>
        /// The sounds that this entity can make. Keys to use are:<br/>
        /// - "hurt"<br/>
        /// - "death"<br/>
        /// - "idle"<br/>
        /// - "swim" (player only)<br/>
        /// - "eat" (player only)
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "None. Sound ranges default to 24, and pitches default to non-random except for the idle sound.")]
        public Dictionary<string, SoundAttributes> Sounds;

        /// <summary>
        /// The chance that an idle sound will play for the entity.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0.3")]
        public float IdleSoundChance = 0.05f;

        /// <summary>
        /// The drops for the entity when they are killed.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public BlockDropItemStack[] Drops;

        /// <summary>
        /// List of tags that entity has. Used for categorizing.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public TagSetFast Tags;


        public EntityProperties CreateProperties(ICoreAPI api)
        {
            BlockDropItemStack[] DropsCopy;
            if (Drops == null)
            {
                DropsCopy = null;
            }
            else
            {
                DropsCopy = new BlockDropItemStack[Drops.Length];
                for (int i = 0; i < DropsCopy.Length; i++)
                    DropsCopy[i] = Drops[i].Clone();
            }


            EntityProperties properties = new EntityProperties()
            {
                Code = Code,
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
                Sounds = Sounds == null ? new Dictionary<string, SoundAttributes>() : new Dictionary<string, SoundAttributes>(Sounds),
                IdleSoundChance = IdleSoundChance,
                Drops = DropsCopy,
                EyeHeight = EyeHeight,
                SwimmingEyeHeight = SwimmingEyeHeight ?? EyeHeight,
                Tags = Tags,
            };

            var serverBhHealth = Server?.Behaviors?.FirstOrDefault(jsobjs => jsobjs["code"].AsString() == "health");
            if (serverBhHealth != null)
            {
                if (Client.Behaviors.FirstOrDefault(jsobjc => jsobjc["code"].AsString() == "health") == null) {
                    Client.Behaviors = Client.Behaviors.Append(serverBhHealth);
                    //api.Logger.Warning("Entity type {0} has behavior health server side, but not client side. This is deprecated, please add it client side as well.", Code);
                }
            }        

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
            deserializer.Converters.Add(new SoundAttributeConverter(false, 24));
            EntityType type = CreateResolvedType<EntityType>(api, fullcode, jobject, deserializer, variant);
            try
            {
                // Backwards compatibility with the json setup used by 1.21
                JToken idleSoundRange = jobject["idleSoundRange"];
                if (idleSoundRange != null)
                {
                    SoundAttributes idleSound = type.Sounds["idle"];
                    idleSound.Range = idleSoundRange.ToObject<float>();
                    type.Sounds["idle"] = idleSound;
                }
                // Special case to make pitch default to random for the idle sound only, not other sounds
                JToken jIdleSound = (jobject["sounds"] as JObject)?["idle"];
                if (jIdleSound != null && (jIdleSound as JObject)?["pitch"] == null)
                {
                    SoundAttributes idleSound = type.Sounds["idle"];
                    idleSound.Pitch = SoundAttributes.RandomPitch;
                    type.Sounds["idle"] = idleSound;
                }
            }
            catch (Exception e)
            {
                api.Server.Logger.Error("Exception thrown while trying to resolve entity type {0}, variant {1}. Will ignore most of the attributes. Exception thrown:", this.Code, fullcode);
                api.Server.Logger.Error(e);
            }

            return type;
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
        [DocumentAsJson("Required")]
        public string Renderer;

        /// <summary>
        /// A list of all available textures for the entity. First texture in the list will be the default. 
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "None")]
        public Dictionary<string, CompositeTexture> Textures { get; set; } = new Dictionary<string, CompositeTexture>();

        /// <summary>
        /// Sets a single texture. It is recommended to specify texture keys by using <see cref="Textures"/> instead of this.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        protected CompositeTexture Texture;

        /// <summary>
        /// The glow level for the entity.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0")]
        public int GlowLevel = 0;

        /// <summary>
        /// The shape of the entity. Must be set unless <see cref="Renderer"/> is not set to "Shape".
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Required")]
        public CompositeShape Shape;

        /// <summary>
        /// A list of all client-side behaviors for the entity.
        /// </summary>
        [JsonProperty(ItemConverterType = typeof(JsonAttributesConverter))]
        [DocumentAsJson("Optional", "None")]
        public JsonObject[] Behaviors;

        /// <summary>
        /// The size of the entity.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "1")]
        public float Size = 1f;

        /// <summary>
        /// The rate at which the entity's size grows with age - used for chicks and other small baby animals.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0")]
        public float SizeGrowthFactor = 0f;

        /// <summary>
        /// The animation data for the entity.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public AnimationMetaData[] Animations;

        /// <summary>
        /// Makes entities pitch forward and backwards when stepping.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "True")]
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
        /// A list of all server-side behaviors for the entity.
        /// </summary>
        [JsonProperty(ItemConverterType = typeof(JsonAttributesConverter))]
        [DocumentAsJson("Optional", "None")]
        public JsonObject[] Behaviors;

        /// <summary>
        /// A set of server-side attributes passed to the entity.
        /// </summary>
        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        [DocumentAsJson("Optional", "None")]
        public JsonObject Attributes;

        /// <summary>
        /// The spawn conditions for the entity. Without this, the entity will not spawn anywhere.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "None")]
        public SpawnConditions SpawnConditions;
    }

}
