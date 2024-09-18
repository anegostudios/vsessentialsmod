using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class BEBehaviorAnimatable : BlockEntityBehavior
    {
        public BEBehaviorAnimatable(BlockEntity blockentity) : base(blockentity)
        {
        }

        public BlockEntityAnimationUtil animUtil;

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            animUtil = new BlockEntityAnimationUtil(api, Blockentity);
        }


        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            animUtil?.Dispose();
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            base.OnBlockBroken(byPlayer);
            animUtil?.Dispose();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            animUtil?.Dispose();
        }



        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            return animUtil.activeAnimationsByAnimCode.Count > 0 || (animUtil.animator != null && animUtil.animator.ActiveAnimationCount > 0);
        }


        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            if (Api is ICoreClientAPI capi && capi.Settings.Bool["extendedDebugInfo"] == true)
            {
                dsc.AppendLine(string.Format("Active animations: {0}", string.Join(", ", animUtil.activeAnimationsByAnimCode.Keys)));
            }
        }
    }


    public class BlockEntityAnimationUtil : AnimationUtil
    {
        BlockEntity be;
        public Action<MeshData> OnAfterTesselate;

        public BlockEntityAnimationUtil(ICoreAPI api, BlockEntity be) : base(api, be.Pos.ToVec3d())
        {
            this.be = be;
        }

        public virtual MeshData InitializeAnimator(string cacheDictKey, Shape shape = null, ITexPositionSource texSource = null, Vec3f rotationDeg = null)
        {
            MeshData meshdata = CreateMesh(cacheDictKey, shape, out Shape resultingShape, texSource);
            InitializeAnimator(cacheDictKey, meshdata, resultingShape, rotationDeg);
            return meshdata;
        }

        /// <summary>
        /// The first of two stages to initialise the animator: pair this with a call to FinishInitializeAnimator().
        /// </summary>
        /// <param name="nameForLogging"></param>
        /// <param name="shape"></param>
        /// <param name="resultingShape"></param>
        /// <param name="texSource"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public virtual MeshData CreateMesh(string nameForLogging, Shape shape, out Shape resultingShape, ITexPositionSource texSource)
        {
            if (api.Side != EnumAppSide.Client) throw new NotImplementedException("Server side animation system not implemented yet.");

            ICoreClientAPI capi = api as ICoreClientAPI;
            MeshData meshdata;
            Block block = api.World.BlockAccessor.GetBlock(be.Pos);

            if (texSource == null)
            {
                texSource = capi.Tesselator.GetTextureSource(block);
            }

            if (shape == null)
            {
                AssetLocation shapePath = block.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                shape = Shape.TryGet(api, shapePath);
                if (shape == null)
                {
                    api.World.Logger.Error("Shape for block {0} not found or errored, was supposed to be at {1}. Block animations not loaded!", this.be.Block.Code, shapePath);
                    resultingShape = shape;
                    return new MeshData();
                }
            }

            shape.ResolveReferences(api.World.Logger, nameForLogging);
            shape.CacheInvTransforms();
            shape.ResolveAndFindJoints(api.World.Logger, nameForLogging);

            TesselationMetaData meta = new TesselationMetaData()
            {
                QuantityElements = block.Shape.QuantityElements,
                SelectiveElements = block.Shape.SelectiveElements,
                IgnoreElements = block.Shape.IgnoreElements,
                TexSource = texSource,
                WithJointIds = true,
                WithDamageEffect = true,
                TypeForLogging = nameForLogging,
                //Rotation = rotationDeg - why was this here? It breaks animations
            };


            capi.Tesselator.TesselateShape(meta, shape, out meshdata);
            OnAfterTesselate?.Invoke(meshdata);

            resultingShape = shape;
            return meshdata;
        }


        public override void InitializeAnimatorServer(string cacheDictKey, Shape blockShape)
        {
            base.InitializeAnimatorServer(cacheDictKey, blockShape);

            be.RegisterGameTickListener(AnimationTickServer, 20);
        }

        protected override void OnAnimationsStateChange(bool animsNowActive)
        {
            if (animsNowActive)
            {
                if (renderer != null) api.World.BlockAccessor.MarkBlockDirty(be.Pos, () => renderer.ShouldRender = true);
            } else
            {
                api.World.BlockAccessor.MarkBlockDirty(be.Pos, () => renderer.ShouldRender = false);
            }
        }


    }
}
