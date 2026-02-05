using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Util;
using Vintagestory.API.Server;
using Vintagestory.API;
using System.Linq;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{
    /// <summary>
    /// Defines an in-game block using a json file. BlockTypes use all properties from <see cref="CollectibleType"/> and <see cref="RegistryObjectType"/>, but also contain some unique properties.
    /// Any json file placed in your "assets/blocktypes" folder will be loaded as a blocktype in the game.
    /// </summary>
    /// <example>
    /// <code language="json">
    ///{
    ///  "code": "leather",
    ///  "class": "Block",
    ///  "shape": { "base": "block/basic/cube" },
    ///  "drawtype": "Cube",
    ///  "attributes": {
    ///    ...
    ///  },
    ///  "blockmaterial": "Cloth",
    ///  "creativeinventory": {
    ///    ...
    ///  },
    ///  "replaceable": 700,
    ///  "resistance": 1.5,
    ///  "lightAbsorption": 99,
    ///  "textures": {
    ///    ...
    ///  },
    ///  "combustibleProps": {
    ///    ...
    ///  },
    ///  "sounds": {
    ///    ...
    ///  },
    ///  "materialDensity": 400
    ///}
    /// </code>
    /// </example>
    [DocumentAsJson]
    [JsonObject(MemberSerialization.OptIn)]
    public class BlockType : CollectibleType
    {
        public static Cuboidf DefaultCollisionBox = new Cuboidf(0, 0, 0, 1, 1, 1);
        public static RotatableCube DefaultCollisionBoxR = new RotatableCube(0, 0, 0, 1, 1, 1);

        public BlockType()
        {
            Class = "Block";
            Shape = new CompositeShape() { Base = new AssetLocation(GlobalConstants.DefaultDomain, "block/basic/cube") };
            GuiTransform = ModelTransform.BlockDefaultGui();
#pragma warning disable CS0618 // Type or member is obsolete
            FpHandTransform = ModelTransform.BlockDefaultFp();
#pragma warning restore CS0618
            TpHandTransform = ModelTransform.BlockDefaultTp();
            GroundTransform = ModelTransform.BlockDefaultGround();
            MaxStackSize = 64;
        }

        internal override RegistryObjectType CreateAndPopulate(ICoreServerAPI api, AssetLocation fullcode, JObject jobject, JsonSerializer deserializer, API.Datastructures.OrderedDictionary<string, string> variant)
        {
            return CreateResolvedType<BlockType>(api, fullcode, jobject, deserializer, variant);
        }

        /// <summary>
        /// A 'block entity' is stored per specific instance of a block in the world.
        /// To attach a block entity to a block, add the block entity code here..
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public string EntityClass;

        /// <summary>
        /// This array adds modifiers that can alter the behavior of a block entity defined in <see cref="EntityClass"/>.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public BlockEntityBehaviorType[] EntityBehaviors = Array.Empty<BlockEntityBehaviorType>();

        /// <summary>
        /// If not set to JSON it will use an efficient hardcoded model
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "JSON")]
        public EnumDrawType DrawType = EnumDrawType.JSON;

        /// <summary>
        /// Whether or not to use the Y axis when picking a random value based on the block's position.
        /// If placing an instance of this block on top of one another, setting this to XZ will ensure that all vertical instances have the same random size, offset, and rotations if used.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "XYZ")]
        public EnumRandomizeAxes RandomizeAxes = EnumRandomizeAxes.XYZ;

        /// <summary>
        /// If true then the block will be randomly offseted by 1/3 of a block when placed
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public bool RandomDrawOffset;

        /// <summary>
        /// If true, the block will have a random rotation apploed to it.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public bool RandomizeRotations;

        /// <summary>
        /// If set, the block will have a random size between 1 and 1+RandomSizeAdjust.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public float RandomSizeAdjust;

        /// <summary>
        /// During which render pass this block should be rendered.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "Opaque")]
        public EnumChunkRenderPass RenderPass = EnumChunkRenderPass.Opaque;

        /// <summary>
        /// Determines which sides of the blocks should be rendered
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "Default")]
        public EnumFaceCullMode FaceCullMode = EnumFaceCullMode.Default;

        /// <summary>
        /// The block shape to be used when displayed in the inventory gui, held in hand or dropped on the ground.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public CompositeShape ShapeInventory = null;

        /// <summary>
        /// A specific shape to use when this block is near the camera. Used to add more detail to closer objects.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public CompositeShape Lod0Shape = null;

        /// <summary>
        /// A specific shape to use when this block is far away from the camera. Used to lower detail from further away objects.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public CompositeShape Lod2Shape = null;

        /// <summary>
        /// If set to true, this block will not be rendered if it is too far away from the camera.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public bool DoNotRenderAtLod2 = false;

        /// <summary>
        /// Currently not used. Maybe you're looking for <see cref="SideAo"/> or <see cref="SideSolidOpaqueAo"/>?
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Unused")]
        public bool Ambientocclusion = true;

        /// <summary>
        /// The sounds played for this block during step, break, build and walk. Use GetSounds() to query if not performance critical.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public BlockSounds Sounds;

        /// <summary>
        /// Textures to be used for this block in the inventory gui, held in hand or dropped on the ground
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public Dictionary<string, CompositeTexture> TexturesInventory;

        /// <summary>
        /// Defines which of the 6 block sides are completely opaque. Used to determine which block faces can be culled during tesselation.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "All true")]
        public Dictionary<string, bool> SideOpaque;

        /// <summary>
        /// Defines which of the 6 block side should be shaded with ambient occlusion
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "All true")]
        public Dictionary<string, bool> SideAo;

        /// <summary>
        /// Defines which of the 6 block neighbours should receive AO if this block is in front of them. If this block's <see cref="LightAbsorption"/> > 0, default is all true. Otherwise, all false..
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "See description")]
        public Dictionary<string, bool> EmitSideAo;

        /// <summary>
        /// Defines which of the 6 block side are solid. Used to determine if attachable blocks can be attached to this block. Also used to determine if snow can rest on top of this block.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "All true")]
        public Dictionary<string, bool> SideSolid;

        /// <summary>
        /// Quick way of defining <see cref="SideSolid"/>, <see cref="SideOpaque"/>, and <see cref="SideAo"/>. Using this property overrides any values to those.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional")]
        public Dictionary<string, bool> SideSolidOpaqueAo;

        /// <summary>
        /// The color map for climate color mapping. Leave null for no coloring by climate
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public string ClimateColorMap = null;

        /// <summary>
        /// The color map for season color mapping. Leave null for no coloring by season
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public string SeasonColorMap = null;

        /// <summary>
        /// A value usually between 0-9999 that indicates which blocks may be replaced with others.
        /// - Any block with replaceable value above 5000 will be washed away by water
        /// - Any block with replaceable value above 6000 will replaced when the player tries to place a block
        /// Examples:
        /// 0 = Bedrock
        /// 6000 = Tallgrass
        /// 9000 = Lava
        /// 9500 = Water
        /// 9999 = Air
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0")]
        public int Replaceable;

        /// <summary>
        /// 0 = nothing can grow, 10 = some tallgrass and small trees can be grow on it, 100 = all grass and trees can grow on it
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0")]
        public int Fertility;

        /// <summary>
        /// Data thats passed on to the graphics card for every vertex of the blocks model
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public VertexFlags VertexFlags;

        /// <summary>
        /// A bit uploaded to the shader to add a frost overlay below freezing temperature
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "false")]
        public bool Frostable;

        /// <summary>
        /// For light blocking blocks. Any value above 32 will completely block all light.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "99")]
        public ushort LightAbsorption = 99;

        /// <summary>
        /// How long it takes to break this block in seconds.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "6")]
        public float Resistance = 6f;

		/// <summary>
        /// A way to categorize blocks. Used for getting the mining speed for each tool type, amongst other things.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "Stone")]
        public EnumBlockMaterial BlockMaterial = EnumBlockMaterial.Stone;

        /// <summary>
        /// The mining tier required to break this block.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Recommended", "0")]
        public int RequiredMiningTier;

        /// <summary>
        /// <jsonalias>CollisionBox</jsonalias>
        /// Defines the area with which the player character collides with. If not specified, the default of (0, 0, 0, 1, 1, 1) will be used
        /// </summary>
        [JsonProperty("CollisionBox")]
        [DocumentAsJson("Optional", "Default Collision Box")]
        private RotatableCube CollisionBoxR = DefaultCollisionBoxR.Clone();

        /// <summary>
        /// <jsonalias>SelectionBox</jsonalias>
        /// Defines the area which the players mouse pointer collides with for selection. If not specified, the default of (0, 0, 0, 1, 1, 1) will be used
        /// </summary>
        [JsonProperty("SelectionBox")]
        [DocumentAsJson("Optional", "Default Collision Box")]
        private RotatableCube SelectionBoxR = DefaultCollisionBoxR.Clone();

        /// <summary>
        /// <jsonalias>CollisionSelectionBox</jsonalias>
        /// Shorthand way of setting <see cref="CollisionBoxR"/> and <see cref="SelectionBoxR"/> at the same time.
        /// </summary>
        [JsonProperty("CollisionSelectionBox")]
        [DocumentAsJson("Optional", "Default Collision Box")]
        private RotatableCube CollisionSelectionBoxR = null;

        /// <summary>
        /// <jsonalias>ParticleCollisionBox</jsonalias>
        /// Defines the area with which particles collide with. If not provided, will use <see cref="CollisionBoxR"/> or <see cref="CollisionBoxesR"/>.
        /// </summary>
        [JsonProperty("ParticleCollisionBox")]
        [DocumentAsJson("Optional", "Default Collision Box")]
        private RotatableCube ParticleCollisionBoxR = null;

        /// <summary>
        /// <jsonalias>CollisionBoxes</jsonalias>
        /// Defines multiple areas with which the player character collides with.
        /// </summary>
        [JsonProperty("CollisionBoxes")]
        [DocumentAsJson("Optional", "Default Collision Box")]
        private RotatableCube[] CollisionBoxesR = null;

        /// <summary>
        /// <jsonalias>SelectionBoxes</jsonalias>
        /// Defines multiple areas which the players mouse pointer collides with for selection.
        /// </summary>
        [JsonProperty("SelectionBoxes")]
        [DocumentAsJson("Optional", "Default Collision Box")]
        private RotatableCube[] SelectionBoxesR = null;

        /// <summary>
        /// <jsonalias>CollisionSelectionBoxes</jsonalias>
        /// Shorthand way of setting <see cref="CollisionBoxesR"/> and <see cref="SelectionBoxesR"/> at the same time.
        /// </summary>
        [JsonProperty("CollisionSelectionBoxes")]
        [DocumentAsJson("Optional", "Default Collision Box")]
        private RotatableCube[] CollisionSelectionBoxesR = null;

        /// <summary>
        /// <jsonalias>ParticleCollisionBoxes</jsonalias>
        /// Defines multiple areas with which particles collide with. If not provided, will use <see cref="CollisionBoxR"/> or <see cref="CollisionBoxesR"/>.
        /// </summary>
        [JsonProperty("ParticleCollisionBoxes")]
        [DocumentAsJson("Optional", "Default Collision Box")]
        private RotatableCube[] ParticleCollisionBoxesR = null;

        public Cuboidf[] CollisionBoxes = null;
        public Cuboidf[] SelectionBoxes = null;
        public Cuboidf[] ParticleCollisionBoxes = null;

        /// <summary>
        /// Used for ladders. If true, walking against this blocks collisionbox will make the player climb.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public bool Climbable = false;

        /// <summary>
        /// Will be used for not rendering rain below this block.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "False")]
        public bool RainPermeable = false;

        /// <summary>
        /// Value between 0 to 7. Determines the height of the liquid, if <see cref="LiquidCode"/> is set.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "0")]
        public int LiquidLevel;

        /// <summary>
        /// If this block is or contains a liquid, this should be the code (or "identifier") of the liquid.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public string LiquidCode;

        /// <summary>
        /// Walk speed when standing or inside this block.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "1")]
        public float WalkspeedMultiplier = 1f;

        /// <summary>
        /// Drag multiplier applied to entities standing on it.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "1")]
        public float DragMultiplier = 1f;

        /// <summary>
        /// The items that should drop from breaking this block.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public BlockDropItemStack[] Drops;

        /// <summary>
        /// Information about the blocks as a crop.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "None")]
        public BlockCropPropertiesType CropProps = null;

        /// <summary>
        /// Defines what creature groups may spawn on this block.
        /// </summary>
        [JsonProperty]
        [DocumentAsJson("Optional", "All ( [\"*\"] )")]
        public string[] AllowSpawnCreatureGroups = Block.DefaultAllowAllSpawns;


        public Block CreateBlock(ICoreServerAPI api)
        {
            Block block;

            if (api.ClassRegistry.GetBlockClass(this.Class) == null)
            {
                api.Server.Logger.Error("Block with code {0} has defined a block class {1}, no such class registered. Will ignore.", this.Code, this.Class);
                block = new Block();
            }
            else
            {
                block = api.ClassRegistry.CreateBlock(this.Class);
            }


            if (this.EntityClass != null)
            {
                if (api.ClassRegistry.GetBlockEntity(this.EntityClass) != null)
                {
                    block.EntityClass = this.EntityClass;
                }
                else
                {
                    api.Server.Logger.Error("Block with code {0} has defined a block entity class {1}, no such class registered. Will ignore.", this.Code, this.EntityClass);
                }
            }


            block.Code = this.Code;
            block.VariantStrict = this.Variant;
            block.Variant = new RelaxedReadOnlyDictionary<string, string>(this.Variant);
            block.Class = this.Class;
            block.LiquidSelectable = this.LiquidSelectable;
            block.LiquidCode = this.LiquidCode;
            block.BlockEntityBehaviors = (BlockEntityBehaviorType[])this.EntityBehaviors?.Clone() ?? Array.Empty<BlockEntityBehaviorType>();

            block.Tags = api.TagsManager.GetTagSetUnsafe<TagSet>(this.Tags);

            if (block.EntityClass == null && block.BlockEntityBehaviors != null && block.BlockEntityBehaviors.Length > 0)
            {
                block.EntityClass = "Generic";
            }

            block.WalkSpeedMultiplier = this.WalkspeedMultiplier;
            block.DragMultiplier = this.DragMultiplier;
            block.Durability = this.Durability;
            block.Dimensions = this.Size ?? CollectibleObject.DefaultSize;
            block.DamagedBy = (EnumItemDamageSource[])this.DamagedBy?.Clone();
            block.Tool = this.Tool;
            block.DrawType = this.DrawType;
            block.Replaceable = this.Replaceable;
            block.Fertility = this.Fertility;
            block.LightAbsorption = this.LightAbsorption;
            block.LightHsv = this.LightHsv;
            block.VertexFlags = this.VertexFlags?.Clone() ?? new VertexFlags(0);
            block.Frostable = this.Frostable;
            block.Resistance = this.Resistance;
            block.BlockMaterial = this.BlockMaterial;
            block.Shape = this.Shape;
            block.Lod0Shape = this.Lod0Shape;
            block.Lod2Shape = this.Lod2Shape;
            block.ShapeInventory = this.ShapeInventory;
            block.DoNotRenderAtLod2 = this.DoNotRenderAtLod2;
            block.TexturesInventory = this.TexturesInventory == null ? null : new FastSmallDictionary<string, CompositeTexture>(this.TexturesInventory);
            block.Textures = this.Textures == null ? null : new FastSmallDictionary<string, CompositeTexture>(this.Textures);
            block.ClimateColorMap = this.ClimateColorMap;
            block.SeasonColorMap = this.SeasonColorMap;
            block.Ambientocclusion = this.Ambientocclusion;
            block.CollisionBoxes = this.CollisionBoxes;
            block.SelectionBoxes = this.SelectionBoxes;
            block.ParticleCollisionBoxes = this.ParticleCollisionBoxes;
            block.MaterialDensity = this.MaterialDensity;
            block.GuiTransform = this.GuiTransform;
#pragma warning disable CS0618 // Type or member is obsolete
            block.FpHandTransform = this.FpHandTransform;
#pragma warning restore CS0618
            block.TpHandTransform = this.TpHandTransform;
            block.TpOffHandTransform = this.TpOffHandTransform;
            block.GroundTransform = this.GroundTransform;
            block.RenderPass = this.RenderPass;
            block.ParticleProperties = this.ParticleProperties;
            block.ParticlesTextureCode = this.ParticlesTextureCode;
            block.Climbable = this.Climbable;
            block.RainPermeable = this.RainPermeable;
            block.FaceCullMode = this.FaceCullMode;
            block.Drops = this.Drops;
            block.MaxStackSize = this.MaxStackSize;
            block.MatterState = this.MatterState;
            if (this.Attributes != null)
            {
                block.Attributes = this.Attributes.Clone();
            }
            block.NutritionProps = this.NutritionProps;
            block.TransitionableProps = this.TransitionableProps;
            block.GrindingProps = this.GrindingProps;
            block.CrushingProps = this.CrushingProps;
            block.LiquidLevel = this.LiquidLevel;
            block.AttackPower = this.AttackPower;
            block.MiningSpeed = this.MiningSpeed;
            block.ToolTier = this.ToolTier;
            block.RequiredMiningTier = this.RequiredMiningTier;
            block.HeldSounds = this.HeldSounds?.Clone();
            block.AttackRange = this.AttackRange;


            if (this.Sounds != null)
            {
                block.Sounds = this.Sounds.Clone();
            }
            block.RandomDrawOffset = this.RandomDrawOffset ? 1 : 0;
            block.RandomizeRotations = this.RandomizeRotations;
            block.RandomizeAxes = this.RandomizeAxes;
            block.RandomSizeAdjust = this.RandomSizeAdjust;
            block.CombustibleProps = this.CombustibleProps;
            block.StorageFlags = (EnumItemStorageFlags)this.StorageFlags;
            block.RenderAlphaTest = this.RenderAlphaTest;
            block.HeldTpHitAnimation = this.HeldTpHitAnimation;
            block.HeldRightTpIdleAnimation = this.HeldRightTpIdleAnimation;
            block.HeldLeftTpIdleAnimation = this.HeldLeftTpIdleAnimation;

            block.HeldLeftReadyAnimation = this.HeldLeftReadyAnimation;
            block.HeldRightReadyAnimation = this.HeldRightReadyAnimation;

            block.HeldTpUseAnimation = this.HeldTpUseAnimation;
            block.CreativeInventoryStacks = this.CreativeInventoryStacks == null ? null : (CreativeTabAndStackList[])this.CreativeInventoryStacks.Clone();
            block.AllowSpawnCreatureGroups = this.AllowSpawnCreatureGroups;
            block.AllCreaturesAllowed = AllowSpawnCreatureGroups.Length == 1 && AllowSpawnCreatureGroups[0] == "*";

            // BlockType net only sends the collisionboxes at an accuracy of 1/10000 so we have to make sure they are the same server and client side
            EnsureClientServerAccuracy(block.CollisionBoxes);
            EnsureClientServerAccuracy(block.SelectionBoxes);
            EnsureClientServerAccuracy(block.ParticleCollisionBoxes);

            this.InitBlock(api.ClassRegistry, api.World.Logger, block, this.Variant);

            return block;
        }

        private void EnsureClientServerAccuracy(Cuboidf[] boxes)
        {
            if (boxes == null) return;
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].RoundToFracsOfOne10thousand();
            }
        }

        Cuboidf[] ToCuboidf(params RotatableCube[] cubes)
        {
            Cuboidf[] outcubes = new Cuboidf[cubes.Length];
            for (int i = 0; i < cubes.Length; i++)
            {
                outcubes[i] = cubes[i].RotatedCopy();
            }
            return outcubes;
        }

        override internal void OnDeserialized()
        {
            base.OnDeserialized();

            // Only one collision/selectionbox 
            if (CollisionBoxR != null) CollisionBoxes = ToCuboidf(CollisionBoxR);
            if (SelectionBoxR != null) SelectionBoxes =  ToCuboidf(SelectionBoxR);
            if (ParticleCollisionBoxR != null) ParticleCollisionBoxes = ToCuboidf(ParticleCollisionBoxR);

            // Multiple collision/selectionboxes
            if (CollisionBoxesR != null) CollisionBoxes = ToCuboidf(CollisionBoxesR);
            if (SelectionBoxesR != null) SelectionBoxes = ToCuboidf(SelectionBoxesR);
            if (ParticleCollisionBoxesR != null) ParticleCollisionBoxes = ToCuboidf(ParticleCollisionBoxesR);

            // Merged collision+selectioboxes
            if (CollisionSelectionBoxR != null)
            {
                CollisionBoxes = ToCuboidf(CollisionSelectionBoxR);
                SelectionBoxes = CloneArray(CollisionBoxes);
            }

            if (CollisionSelectionBoxesR != null)
            {
                CollisionBoxes = ToCuboidf(CollisionSelectionBoxesR);
                SelectionBoxes = CloneArray(CollisionBoxes);
            }

            ResolveStringBoolDictFaces(SideSolidOpaqueAo);
            ResolveStringBoolDictFaces(SideSolid);
            ResolveStringBoolDictFaces(SideOpaque);
            ResolveStringBoolDictFaces(SideAo);
            ResolveStringBoolDictFaces(EmitSideAo);

            if (SideSolidOpaqueAo != null && SideSolidOpaqueAo.Count > 0)
            {
                ResolveDict(SideSolidOpaqueAo, ref SideSolid);
                ResolveDict(SideSolidOpaqueAo, ref SideOpaque);
                ResolveDict(SideSolidOpaqueAo, ref EmitSideAo);
            }

            if (EmitSideAo == null)
            {
                EmitSideAo = new Dictionary<string, bool>() { { "all", LightAbsorption > 0 } };
                ResolveStringBoolDictFaces(EmitSideAo);
            }


            if (LightHsv == null) LightHsv = new byte[3];

            // Boundary check light values, if they go beyond allowed values the lighting system will crash
            LightHsv[0] = (byte)GameMath.Clamp(LightHsv[0], 0, ColorUtil.HueQuantities - 1);
            LightHsv[1] = (byte)GameMath.Clamp(LightHsv[1], 0, ColorUtil.SatQuantities - 1);
            LightHsv[2] = (byte)GameMath.Clamp(LightHsv[2], 0, ColorUtil.BrightQuantities - 1);
        }

        private Cuboidf[] CloneArray(Cuboidf[] array)
        {
            if (array == null) return null;
            int l = array.Length;
            Cuboidf[] result = new Cuboidf[l];
            for (int i = 0; i < l; i++) result[i] = array[i].Clone();
            return result;
        }

        private void ResolveDict(Dictionary<string, bool> sideSolidOpaqueAo, ref Dictionary<string, bool> targetDict)
        {
            bool wasNull = targetDict == null;
            if (wasNull)
            {
                targetDict = new Dictionary<string, bool>() { { "all", true } };
            }

            foreach (var val in sideSolidOpaqueAo)
            {
                if (wasNull || !targetDict.ContainsKey(val.Key))
                {
                    targetDict[val.Key] = val.Value;
                }
            }

            ResolveStringBoolDictFaces(targetDict);
        }

        [ThreadStatic]
        private static List<BlockBehavior> reusableBehaviorList;
        [ThreadStatic]
        private static List<CollectibleBehavior> reusableCollectibleBehaviorList;
        public void InitBlock(IClassRegistryAPI instancer, ILogger logger, Block block, API.Datastructures.OrderedDictionary<string, string> searchReplace)
        {
            CollectibleBehaviorType[] behaviorTypes = Behaviors;

            if (behaviorTypes != null)
            {
                List<BlockBehavior> blockbehaviors = reusableBehaviorList ??= new();
                List<CollectibleBehavior> collbehaviors = reusableCollectibleBehaviorList ??= new();
                blockbehaviors.Clear();
                collbehaviors.Clear();

                for (int i = 0; i < behaviorTypes.Length; i++)
                {
                    CollectibleBehaviorType behaviorType = behaviorTypes[i];
                    CollectibleBehavior behavior = null;

                    if (instancer.GetCollectibleBehaviorClass(behaviorType.name) != null)
                    {
                        behavior = instancer.CreateCollectibleBehavior(block, behaviorType.name);
                    }

                    if (instancer.GetBlockBehaviorClass(behaviorType.name) != null)
                    {
                        behavior = instancer.CreateBlockBehavior(block, behaviorType.name);
                    }

                    if (behavior == null)
                    {
                        logger.Warning(Lang.Get("Block or Collectible behavior {0} for block {1} not found", behaviorType.name, block.Code));
                        continue;
                    }

                    if (behaviorType.properties == null) behaviorType.properties = new JsonObject(new JObject());

                    try
                    {
                        behavior.Initialize(behaviorType.properties);
                    } catch (Exception e)
                    {
                        logger.Error("Failed calling Initialize() on collectible or block behavior {0} for block {1}, using properties {2}. Will continue anyway. Exception", behaviorType.name, block.Code, behaviorType.properties.ToString());
                        logger.Error(e);
                    }

                    collbehaviors.Add(behavior);
                    if (behavior is BlockBehavior bbh)
                    {
                        blockbehaviors.Add(bbh);
                    }
                }

                block.BlockBehaviors = blockbehaviors.ToArray();
                block.CollectibleBehaviors = collbehaviors.ToArray();
                blockbehaviors.Clear();
                collbehaviors.Clear();
            }

            if (CropProps != null)
            {
                block.CropProps = new BlockCropProperties();
                block.CropProps.GrowthStages = CropProps.GrowthStages;
                block.CropProps.HarvestGrowthStageLoss = CropProps.HarvestGrowthStageLoss;
                block.CropProps.MultipleHarvests = CropProps.MultipleHarvests;
                block.CropProps.NutrientConsumption = CropProps.NutrientConsumption;
                block.CropProps.RequiredNutrient = CropProps.RequiredNutrient;
                block.CropProps.TotalGrowthDays = CropProps.TotalGrowthDays;
                block.CropProps.TotalGrowthMonths = CropProps.TotalGrowthMonths;

                block.CropProps.ColdDamageBelow = CropProps.ColdDamageBelow;
                block.CropProps.HeatDamageAbove = CropProps.HeatDamageAbove;
                block.CropProps.DamageGrowthStuntMul = CropProps.DamageGrowthStuntMul;
                block.CropProps.ColdDamageRipeMul = CropProps.ColdDamageRipeMul;


                if (CropProps.Behaviors != null)
                {
                    block.CropProps.Behaviors = new CropBehavior[CropProps.Behaviors.Length];
                    for (int i = 0; i < CropProps.Behaviors.Length; i++)
                    {
                        CropBehaviorType behaviorType = CropProps.Behaviors[i];
                        CropBehavior behavior = instancer.CreateCropBehavior(block, behaviorType.name);
                        if (behaviorType.properties != null)
                        {
                            behavior.Initialize(behaviorType.properties);
                        }
                        block.CropProps.Behaviors[i] = behavior;
                    }
                }
            }

            if (block.Drops == null)
            {
                block.Drops = new BlockDropItemStack[] { new BlockDropItemStack() {
                    Code = block.Code,
                    Type = EnumItemClass.Block,
                    Quantity = NatFloat.One
                } };
            }

            block.CreativeInventoryTabs = GetCreativeTabs(block.Code, CreativeInventory, searchReplace);

            if (SideOpaque != null && SideOpaque.Count > 0) block.SideOpaque = new SmallBoolArray(SmallBoolArray.OnAllSides);
            if (SideAo != null && SideAo.Count > 0) block.SideAo = new SmallBoolArray(SmallBoolArray.OnAllSides);
            foreach (BlockFacing facing in BlockFacing.ALLFACES)
            {
                if (SideAo != null && SideAo.TryGetValue(facing.Code, out bool sideAoValue))
                {
                    block.SideAo[facing.Index] = sideAoValue;
                }

                if (EmitSideAo != null && EmitSideAo.TryGetValue(facing.Code, out bool emitSideAoValue))
                {
                    if (emitSideAoValue) block.EmitSideAo |= (byte) facing.Flag;
                }

                if (SideSolid != null && SideSolid.TryGetValue(facing.Code, out bool sideSolidValue))
                {
                    block.SideSolid[facing.Index] = sideSolidValue;
                }

                if (SideOpaque != null && SideOpaque.TryGetValue(facing.Code, out bool sideOpaqueValue))
                {
                    block.SideOpaque[facing.Index] = sideOpaqueValue;
                }
            }

            if (HeldRightReadyAnimation != null && HeldRightReadyAnimation == HeldRightTpIdleAnimation)
            {
                logger.Error("Block {0} HeldRightReadyAnimation and HeldRightTpIdleAnimation is set to the same animation {1}. This invalid and breaks stuff. Will set HeldRightReadyAnimation to null", Code, HeldRightTpIdleAnimation);
                HeldRightReadyAnimation = null;
            }
        }

        [ThreadStatic]
        private static List<string> reusableStringList;
        public static string[] GetCreativeTabs(AssetLocation code, Dictionary<string, string[]> CreativeInventory, API.Datastructures.OrderedDictionary<string, string> searchReplace)
        {
            List<string> tabs = reusableStringList ??= new();
            tabs.Clear();

            foreach (var val in CreativeInventory)
            {
                for (int i = 0; i < val.Value.Length; i++)
                {
                    string blockCode = RegistryObject.FillPlaceHolder(val.Value[i], searchReplace);

                    if (WildcardUtil.Match(blockCode, code.Path))
                    //if (WildCardMatch(blockCode, code.Path))
                    {
                        string tabCode = val.Key;
                        tabs.Add(String.Intern(tabCode));
                    }
                }
            }

            string[] result = tabs.ToArray();
            tabs.Clear();
            return result;
        }

        void ResolveStringBoolDictFaces(Dictionary<string, bool> stringBoolDict)
        {
            if (stringBoolDict == null) return;

            if (stringBoolDict.ContainsKey("horizontals"))
            {
                foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
                {
                    if (!stringBoolDict.ContainsKey(facing.Code)) stringBoolDict[facing.Code] = stringBoolDict["horizontals"];
                }
            }

            if (stringBoolDict.ContainsKey("verticals"))
            {
                foreach (BlockFacing facing in BlockFacing.VERTICALS)
                {
                    if (!stringBoolDict.ContainsKey(facing.Code)) stringBoolDict[facing.Code] = stringBoolDict["verticals"];
                }
            }

            if (stringBoolDict.ContainsKey("all"))
            {
                foreach (BlockFacing facing in BlockFacing.ALLFACES)
                {
                    if (!stringBoolDict.ContainsKey(facing.Code)) stringBoolDict[facing.Code] = stringBoolDict["all"];
                }
            }
            
        }
    }
}
