using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using Vintagestory.API;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Util;

namespace Vintagestory.ServerMods.NoObf
{
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
            FpHandTransform = ModelTransform.BlockDefaultFp();
            TpHandTransform = ModelTransform.BlockDefaultTp();
            TpOffHandTransform = null;
            GroundTransform = ModelTransform.BlockDefaultGround();
            MaxStackSize = 64;
        }

        [JsonProperty]
        public string EntityClass;
        [JsonProperty]
        public BlockEntityBehaviorType[] EntityBehaviors = new BlockEntityBehaviorType[0];
        [JsonProperty]
        public EnumDrawType DrawType = EnumDrawType.JSON;
        [JsonProperty]
        public EnumRandomizeAxes RandomizeAxes = EnumRandomizeAxes.XYZ;
        [JsonProperty]
        public bool RandomDrawOffset;
        [JsonProperty]
        public bool RandomizeRotations;
        [JsonProperty]
        public float RandomSizeAdjust;
        [JsonProperty]
        public EnumChunkRenderPass RenderPass = EnumChunkRenderPass.Opaque;
        [JsonProperty]
        public EnumFaceCullMode FaceCullMode = EnumFaceCullMode.Default;
        [JsonProperty]
        public CompositeShape ShapeInventory = null;
        [JsonProperty]
        public CompositeShape Lod0Shape = null;
        [JsonProperty]
        public CompositeShape Lod2Shape = null;
        [JsonProperty]
        public bool DoNotRenderAtLod2 = false;
        [JsonProperty]
        public bool Ambientocclusion = true;
        [JsonProperty]
        public BlockSounds Sounds;
        [JsonProperty]
        public Dictionary<string, CompositeTexture> TexturesInventory;

        [JsonProperty]
        public Dictionary<string, bool> SideOpaque;
        [JsonProperty]
        public Dictionary<string, bool> SideAo;
        [JsonProperty]
        public Dictionary<string, bool> EmitSideAo;
        [JsonProperty]
        public Dictionary<string, bool> SideSolid;
        [JsonProperty]
        public Dictionary<string, bool> SideSolidOpaqueAo;

        [JsonProperty]
        public string ClimateColorMap = null;
        [JsonProperty]
        public string SeasonColorMap = null;
        [JsonProperty]
        public int Replaceable;
        [JsonProperty]
        public int Fertility;

        [JsonProperty]
        public VertexFlags VertexFlags;
        [JsonProperty]
        public bool Frostable;

        [JsonProperty]
        public byte[] LightHsv = new byte[] { 0, 0, 0 };
        [JsonProperty]
        public ushort LightAbsorption = 99;
        [JsonProperty]
        public Dictionary<string, bool> LightTraversable;
        [JsonProperty]
        public float Resistance = 6f; // How long it takes to break this block in seconds
        [JsonProperty]
        public EnumBlockMaterial BlockMaterial = EnumBlockMaterial.Stone; // Helps with finding out mining speed for each tool type
        [JsonProperty]
        public int RequiredMiningTier;

        [JsonProperty("CollisionBox")]
        private RotatableCube CollisionBoxR = DefaultCollisionBoxR.Clone();
        [JsonProperty("SelectionBox")]
        private RotatableCube SelectionBoxR = DefaultCollisionBoxR.Clone();
        [JsonProperty("CollisionSelectionBox")]
        private RotatableCube CollisionSelectionBoxR = null;
        [JsonProperty("ParticleCollisionBox")]
        private RotatableCube ParticleCollisionBoxR = null;

        [JsonProperty("CollisionBoxes")]
        private RotatableCube[] CollisionBoxesR = null;
        [JsonProperty("SelectionBoxes")]
        private RotatableCube[] SelectionBoxesR = null;
        [JsonProperty("CollisionSelectionBoxes")]
        private RotatableCube[] CollisionSelectionBoxesR = null;
        [JsonProperty("ParticleCollisionBoxes")]
        private RotatableCube[] ParticleCollisionBoxesR = null;

        public Cuboidf[] CollisionBoxes = null;
        public Cuboidf[] SelectionBoxes = null;
        public Cuboidf[] ParticleCollisionBoxes = null;

        [JsonProperty]
        public bool Climbable = false;
        [JsonProperty]
        public bool RainPermeable = false;
        [JsonProperty]
        public int LiquidLevel;
        [JsonProperty]
        public string LiquidCode;
        [JsonProperty]
        public float WalkspeedMultiplier = 1f;
        [JsonProperty]
        public float DragMultiplier = 1f;
        [JsonProperty]
        public BlockDropItemStack[] Drops;

        [JsonProperty]
        public BlockCropPropertiesType CropProps = null;

        [JsonProperty]
        public string[] AllowSpawnCreatureGroups = new string[] { "*" };


        Cuboidf[] ToCuboidf(params RotatableCube[] cubes)
        {
            Cuboidf[] outcubes = new Cuboidf[cubes.Length];
            for (int i = 0; i < cubes.Length; i++)
            {
                outcubes[i] = cubes[i].RotatedCopy();
            }
            return outcubes;
        }

        Cuboidf[] ToCuboidf(InerhitableRotatableCube cube, Cuboidf parentCube)
        {
            if (parentCube == null) parentCube = DefaultCollisionBox;
            return new Cuboidf[] { cube.InheritedCopy(parentCube) };
        }


        Cuboidf[] ToCuboidf(InerhitableRotatableCube[] cubes, Cuboidf[] parentCubes)
        {
            Cuboidf[] outcubes = new Cuboidf[cubes.Length];
            for (int i = 0; i < cubes.Length; i++)
            {
                Cuboidf parentCube = null;
                if (i < parentCubes.Length) parentCube = parentCubes[i];
                else parentCube = DefaultCollisionBox;

                outcubes[i] = cubes[i].InheritedCopy(parentCube);
            }

            return outcubes;
        }

        override internal void OnDeserialized()
        {
            base.OnDeserialized();

            // Only one collision/selectionbox 
            if (CollisionBoxR != null) CollisionBoxes = ToCuboidf(CollisionBoxR);
            if (SelectionBoxR != null) SelectionBoxes = ToCuboidf(SelectionBoxR);
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
                ResolveDict(SideSolidOpaqueAo, ref SideAo);
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

        public void InitBlock(IClassRegistryAPI instancer, ILogger logger, Block block, OrderedDictionary<string, string> searchReplace)
        {
            CollectibleBehaviorType[] behaviorTypes = Behaviors;

            if (behaviorTypes != null)
            {
                List<BlockBehavior> blockbehaviors = new List<BlockBehavior>();
                List<CollectibleBehavior> collbehaviors = new List<CollectibleBehavior>();

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
                        logger.Error("Failed calling Initialize() on collectible or block behavior {0} for block {1}, using properties {2}. Will continue anyway. Exception: {3}", behaviorType.name, block.Code, behaviorType.properties.ToString(), e);
                    }

                    collbehaviors.Add(behavior);
                    if (behavior is BlockBehavior bbh)
                    {
                        blockbehaviors.Add(bbh);
                    }
                }

                block.BlockBehaviors = blockbehaviors.ToArray();
                block.CollectibleBehaviors = collbehaviors.ToArray();
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

            foreach (BlockFacing facing in BlockFacing.ALLFACES)
            {
                if (SideAo != null && SideAo.ContainsKey(facing.Code))
                {
                    block.SideAo[facing.Index] = SideAo[facing.Code];
                }

                if (EmitSideAo != null && EmitSideAo.ContainsKey(facing.Code))
                {
                    if (EmitSideAo[facing.Code]) block.EmitSideAo |= (byte) facing.Flag;
                }

                if (SideSolid != null && SideSolid.ContainsKey(facing.Code))
                {
                    block.SideSolid[facing.Index] = SideSolid[facing.Code];
                }

                if (SideOpaque != null && SideOpaque.ContainsKey(facing.Code))
                {
                    block.SideOpaque[facing.Index] = SideOpaque[facing.Code];
                }
            }
        }

        public static string[] GetCreativeTabs(AssetLocation code, Dictionary<string, string[]> CreativeInventory, OrderedDictionary<string, string> searchReplace)
        {
            List<string> tabs = new List<string>();

            foreach (var val in CreativeInventory)
            {
                for (int i = 0; i < val.Value.Length; i++)
                {
                    string blockCode = RegistryObject.FillPlaceHolder(val.Value[i], searchReplace);

                    if (WildcardUtil.Match(blockCode, code.Path))
                    //if (WildCardMatch(blockCode, code.Path))
                    {
                        string tabCode = val.Key;
                        tabs.Add(tabCode);
                    }
                }
            }

            return tabs.ToArray();
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
