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

namespace Vintagestory.ServerMods.NoObf
{
    /// <summary>
    /// Describes a entity type
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class EntityType : RegistryObjectType
    {
        [JsonProperty]
        public EnumHabitat Habitat = EnumHabitat.Land;
        [JsonProperty]
        public Vec2f HitBoxSize = new Vec2f(0.5f, 0.5f);
        [JsonProperty]
        public Vec2f DeadHitBoxSize = new Vec2f(0.5f, 0.25f);
        [JsonProperty]
        public double EyeHeight = 0.1;
        [JsonProperty]
        public bool CanClimb = false;
        [JsonProperty]
        public bool CanClimbAnywhere = false;
        [JsonProperty]
        public bool FallDamage = true;
        [JsonProperty]
        public float ClimbTouchDistance = 0.5f;
        [JsonProperty]
        public bool RotateModelOnClimb = false;
        [JsonProperty]
        public float KnockbackResistance = 0f;

        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        public JsonObject Attributes;

        [JsonProperty]
        public ClientEntityConfig Client;

        [JsonProperty]
        public ServerEntityConfig Server;

        [JsonProperty]
        public Dictionary<string, AssetLocation> Sounds;

        [JsonProperty]
        public float IdleSoundChance = 0.3f;

        [JsonProperty]
        public float IdleSoundRange = 24;

        [JsonProperty]
        public BlockDropItemStack[] Drops;


        public EntityProperties CreateProperties()
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
                Variant = new Dictionary<string, string>(Variant),
                Class = Class,
                Habitat = Habitat,
                HitBoxSize = HitBoxSize,
                DeadHitBoxSize = DeadHitBoxSize,
                CanClimb = CanClimb,
                CanClimbAnywhere = CanClimbAnywhere,
                FallDamage = FallDamage,
                ClimbTouchDistance = ClimbTouchDistance,
                RotateModelOnClimb = RotateModelOnClimb,
                KnockbackResistance = KnockbackResistance,
                Attributes = Attributes,
                Sounds = Sounds == null ? new Dictionary<string, AssetLocation>() : new Dictionary<string, AssetLocation>(Sounds),
                IdleSoundChance = IdleSoundChance,
                IdleSoundRange = IdleSoundRange,
                Drops = DropsCopy
            };

            if (Client != null)
            {
                properties.Client = new EntityClientProperties(Client.Behaviors)
                {
                    RendererName = Client.Renderer,
                    Textures = new Dictionary<string, CompositeTexture>(Client.Textures),
                    GlowLevel = Client.GlowLevel,
                    Shape = Client.Shape,
                    Size = Client.Size,
                    Animations = Client.Animations,
                    AnimationsByMetaCode = Client.AnimationsByMetaCode,
                };
            }

            if (Server != null)
            {
                properties.Server = new EntityServerProperties(Server.Behaviors)
                {
                    Attributes = Server.Attributes?.ToAttribute() as TreeAttribute,
                    SpawnConditions = Server.SpawnConditions
                };
            }

            properties.SetEyeHeight(EyeHeight);

            return properties;
        }
    }


    public class ClientEntityConfig
    {
        [JsonProperty]
        public string Renderer;
        [JsonProperty]
        public Dictionary<string, CompositeTexture> Textures { get; set; } = new Dictionary<string, CompositeTexture>();
        [JsonProperty]
        protected CompositeTexture Texture;
        [JsonProperty]
        public int GlowLevel = 0;
        [JsonProperty]
        public CompositeShape Shape;
        [JsonProperty(ItemConverterType = typeof(JsonAttributesConverter))]
        public JsonObject[] Behaviors;
        [JsonProperty]
        public float Size = 1f;
        [JsonProperty]
        public AnimationMetaData[] Animations;

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

    public class ServerEntityConfig
    {
        [JsonProperty(ItemConverterType = typeof(JsonAttributesConverter))]
        public JsonObject[] Behaviors;

        [JsonProperty, JsonConverter(typeof(JsonAttributesConverter))]
        public JsonObject Attributes;

        [JsonProperty]
        public SpawnConditions SpawnConditions;
    }

}
