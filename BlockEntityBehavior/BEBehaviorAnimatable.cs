using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
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
            animUtil.Dispose();
        }

        public override void OnBlockBroken()
        {
            base.OnBlockBroken();
            animUtil.Dispose();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            animUtil.Dispose();
        }



        public override void FromTreeAtributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAtributes(tree, worldAccessForResolve);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
        }



        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (animUtil.activeAnimationsByAnimCode.Count > 0)
            {
                return true;
            }

            return false;
        }
    }




    public class BlockEntityAnimationUtil
    {
        public AnimatorBase animator;

        public BEAnimatableRenderer render;

        public Dictionary<string, AnimationMetaData> activeAnimationsByAnimCode = new Dictionary<string, AnimationMetaData>();

        ICoreAPI api;
        BlockEntity be;
        public BlockEntityAnimationUtil(ICoreAPI api, BlockEntity be)
        {
            this.api = api;
            this.be = be;
        }

        public void InitializeAnimator(string cacheDictKey, Vec3f rotation = null)
        {
            if (api.Side != EnumAppSide.Client) throw new NotImplementedException("Server side animation system not implemented yet.");

            ICoreClientAPI capi = api as ICoreClientAPI;

            Block block = api.World.BlockAccessor.GetBlock(be.Pos);

            ITexPositionSource texSource = capi.Tesselator.GetTexSource(block);
            MeshData meshdata;
            IAsset asset = api.Assets.TryGet(block.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
            Shape shape = asset.ToObject<Shape>();

            shape.ResolveReferences(api.World.Logger, cacheDictKey);
            BlockEntityAnimationUtil.CacheInvTransforms(shape.Elements);
            shape.ResolveAndLoadJoints();

            capi.Tesselator.TesselateShapeWithJointIds("entity", shape, out meshdata, texSource, null, block.Shape.QuantityElements, block.Shape.SelectiveElements);

            meshdata.Rgba2 = null;

            InitializeAnimator(cacheDictKey, rotation, shape, capi.Render.UploadMesh(meshdata));
        }


        public void InitializeAnimator(string cacheDictKey, Vec3f rotation, Shape blockShape, MeshRef meshref, params string[] requireJointsForElements)
        {
            if (api.Side != EnumAppSide.Client) throw new NotImplementedException("Server side animation system not implemented yet.");

            animator = GetAnimator(api, cacheDictKey, blockShape, requireJointsForElements);

            render = new BEAnimatableRenderer(api as ICoreClientAPI, be.Pos, rotation, animator, activeAnimationsByAnimCode, meshref);
        }

        public void StartAnimation(AnimationMetaData meta)
        {
            if (!activeAnimationsByAnimCode.ContainsKey(meta.Code))
            {
                activeAnimationsByAnimCode[meta.Code] = meta;
                api.World.BlockAccessor.MarkBlockDirty(be.Pos, () => render.ShouldRender = true);
            }
        }



        public void StopAnimation(string code)
        {
            if (activeAnimationsByAnimCode.Remove(code))
            {

                if (activeAnimationsByAnimCode.Count == 0)
                {
                    api.World.BlockAccessor.MarkBlockDirty(be.Pos, () => render.ShouldRender = false);
                }
            }
        }


        public static AnimatorBase GetAnimator(ICoreAPI api, string cacheDictKey, Shape blockShape, params string[] requireJointsForElements)
        {
            if (blockShape == null)
            {
                return null;
            }

            object animCacheObj;
            Dictionary<string, AnimCacheEntry> animCache = null;
            api.ObjectCache.TryGetValue("beAnimCache", out animCacheObj);
            animCache = animCacheObj as Dictionary<string, AnimCacheEntry>;
            if (animCache == null)
            {
                api.ObjectCache["beAnimCache"] = animCache = new Dictionary<string, AnimCacheEntry>();
            }

            AnimatorBase animator;

            AnimCacheEntry cacheObj = null;
            if (animCache.TryGetValue(cacheDictKey, out cacheObj))
            {
                animator = api.Side == EnumAppSide.Client ?
                    new ClientAnimator(() => 1, cacheObj.RootPoses, cacheObj.Animations, cacheObj.RootElems, blockShape.JointsById) :
                    new ServerAnimator(() => 1, cacheObj.RootPoses, cacheObj.Animations, cacheObj.RootElems, blockShape.JointsById)
                ;
            }
            else
            {

                for (int i = 0; blockShape.Animations != null && i < blockShape.Animations.Length; i++)
                {
                    blockShape.Animations[i].GenerateAllFrames(blockShape.Elements, blockShape.JointsById);
                }

                animator = api.Side == EnumAppSide.Client ?
                    new ClientAnimator(() => 1, blockShape.Animations, blockShape.Elements, blockShape.JointsById) :
                    new ServerAnimator(() => 1, blockShape.Animations, blockShape.Elements, blockShape.JointsById)
                ;

                animCache[cacheDictKey] = new AnimCacheEntry()
                {
                    Animations = blockShape.Animations,
                    RootElems = (animator as ClientAnimator).rootElements,
                    RootPoses = (animator as ClientAnimator).RootPoses
                };
            }


            return animator;
        }


        public static void CacheInvTransforms(ShapeElement[] elements)
        {
            if (elements == null) return;

            for (int i = 0; i < elements.Length; i++)
            {
                elements[i].CacheInverseTransformMatrix();
                CacheInvTransforms(elements[i].Children);
            }
        }

        internal void Dispose()
        {
            render?.Unregister();
        }
    }
}
