using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class BlockEntityAnimationUtil : AnimationUtil
    {
        BlockEntity be;
        public Action<MeshData> OnAfterTesselate;

        public BlockEntityAnimationUtil(ICoreAPI api, BlockEntity be) : base(api, be.Pos.ToVec3d(), be.Pos.dimension)
        {
            this.be = be;
        }

        public virtual MeshData InitializeAnimator(string cacheDictKey, Shape shape = null, ITexPositionSource texSource = null, Vec3f rotationDeg = null)
        {
            MeshData meshdata = CreateMesh(cacheDictKey, shape, out Shape resultingShape, texSource);
            if (renderer != null)
            {
                if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId) throw new InvalidOperationException("If the renderer has already been created, then InitializeAnimator() cannot be safely called outside the main thread");
                // Note, even if we did not throw an exception here, the InitializeAnimator() method would call capi.Event.UnregisterRenderer() which would throw an exception if it is not on the main thread
            }
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
        public virtual MeshData CreateMesh(string nameForLogging, Shape shape, out Shape resultingShape, ITexPositionSource texSource, TesselationMetaData metaOverride = null)
        {
            if (api.Side != EnumAppSide.Client) throw new NotImplementedException("Server side animation system not implemented yet.");

            ICoreClientAPI capi = api as ICoreClientAPI;
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

            var elementsByName = shape.CollectAndResolveReferences(api.World.Logger, nameForLogging);
            shape.CacheInvTransforms();
            shape.ResolveAndFindJoints(api.World.Logger, nameForLogging, elementsByName);

            TesselationMetaData meta = new TesselationMetaData()
            {
                QuantityElements = metaOverride?.QuantityElements ?? block.Shape.QuantityElements,
                SelectiveElements = metaOverride?.SelectiveElements ?? block.Shape.SelectiveElements,
                IgnoreElements = metaOverride?.IgnoreElements ?? block.Shape.IgnoreElements,
                TexSource = texSource,
                WithJointIds = true,
                WithDamageEffect = true,
                TypeForLogging = nameForLogging,
                //Rotation = rotationDeg - why was this here? It breaks animations
            };


            capi.Tesselator.TesselateShape(meta, shape, out MeshData meshdata);
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
