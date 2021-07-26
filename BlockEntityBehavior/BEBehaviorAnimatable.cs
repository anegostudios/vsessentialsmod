using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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

        public override void OnBlockBroken()
        {
            base.OnBlockBroken();
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
    }




    public class BlockEntityAnimationUtil : IRenderer
    {
        public AnimatorBase animator;

        public BEAnimatableRenderer renderer;

        public Dictionary<string, AnimationMetaData> activeAnimationsByAnimCode = new Dictionary<string, AnimationMetaData>();

        ICoreAPI api;
        BlockEntity be;

        public double RenderOrder => 1;
        public int RenderRange => 99;

        public BlockEntityAnimationUtil(ICoreAPI api, BlockEntity be)
        {
            this.api = api;
            this.be = be;
            (api as ICoreClientAPI)?.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "beanimutil");
        }

        public void InitializeAnimator(string cacheDictKey, Vec3f rotation = null, Shape shape = null)
        {
            if (api.Side != EnumAppSide.Client) throw new NotImplementedException("Server side animation system not implemented yet.");

            ICoreClientAPI capi = api as ICoreClientAPI;

            Block block = api.World.BlockAccessor.GetBlock(be.Pos);

            ITexPositionSource texSource = capi.Tesselator.GetTexSource(block);
            MeshData meshdata;

            if (shape == null)
            {
                IAsset asset = api.Assets.TryGet(block.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
                shape = asset.ToObject<Shape>();
            }

            shape.ResolveReferences(api.World.Logger, cacheDictKey);
            BlockEntityAnimationUtil.CacheInvTransforms(shape.Elements);
            shape.ResolveAndLoadJoints();

            capi.Tesselator.TesselateShapeWithJointIds("entity", shape, out meshdata, texSource, null, block.Shape.QuantityElements, block.Shape.SelectiveElements);

            //meshdata.Rgba2 = null;

            InitializeAnimator(cacheDictKey, rotation, shape, capi.Render.UploadMesh(meshdata));
        }


        public MeshData InitializeAnimator(string cacheDictKey, Shape shape, ITexPositionSource texSource, Vec3f rotation)
        {
            if (api.Side != EnumAppSide.Client) throw new NotImplementedException("Server side animation system not implemented yet.");

            ICoreClientAPI capi = api as ICoreClientAPI;

            Block block = api.World.BlockAccessor.GetBlock(be.Pos);

            MeshData meshdata;

            if (shape == null)
            {
                IAsset asset = api.Assets.TryGet(block.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
                shape = asset.ToObject<Shape>();
            }

            shape.ResolveReferences(api.World.Logger, cacheDictKey);
            CacheInvTransforms(shape.Elements);
            shape.ResolveAndLoadJoints();

            capi.Tesselator.TesselateShapeWithJointIds("entity", shape, out meshdata, texSource, null, block.Shape.QuantityElements, block.Shape.SelectiveElements);

            if (api.Side != EnumAppSide.Client) throw new NotImplementedException("Server side animation system not implemented yet.");

            InitializeAnimator(cacheDictKey, meshdata, shape, rotation);

            return meshdata;
        }

        public void InitializeAnimator(string cacheDictKey, MeshData meshdata, Shape shape, Vec3f rotation) 
        {
            if (meshdata == null)
            {
                throw new ArgumentException("meshdata cannot be null");
            }

            ICoreClientAPI capi = api as ICoreClientAPI;
            animator = GetAnimator(api, cacheDictKey, shape);
            

            if (RuntimeEnv.MainThreadId == System.Threading.Thread.CurrentThread.ManagedThreadId)
            {
                renderer = new BEAnimatableRenderer(api as ICoreClientAPI, be.Pos, rotation, animator, activeAnimationsByAnimCode, capi.Render.UploadMesh(meshdata));
            } else
            {
                renderer = new BEAnimatableRenderer(api as ICoreClientAPI, be.Pos, rotation, animator, activeAnimationsByAnimCode, null);
                (api as ICoreClientAPI).Event.EnqueueMainThreadTask(() => {
                    renderer.meshref = capi.Render.UploadMesh(meshdata);
                }, "uploadmesh");
            }
        }


        public void InitializeAnimator(string cacheDictKey, Vec3f rotation, Shape blockShape, MeshRef meshref)
        {
            if (api.Side != EnumAppSide.Client) throw new NotImplementedException("Server side animation system not implemented yet.");

            animator = GetAnimator(api, cacheDictKey, blockShape);

            (api as ICoreClientAPI).Event.RegisterRenderer(this, EnumRenderStage.Opaque, "beanimutil");
            renderer = new BEAnimatableRenderer(api as ICoreClientAPI, be.Pos, rotation, animator, activeAnimationsByAnimCode, meshref);
        }


        bool stopRenderTriggered = false;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (animator == null || renderer == null) return; // not initialized yet

            if (activeAnimationsByAnimCode.Count > 0 || animator.ActiveAnimationCount > 0)
            {
                animator.OnFrame(activeAnimationsByAnimCode, deltaTime);
            }

            if (activeAnimationsByAnimCode.Count == 0 && animator.ActiveAnimationCount == 0 && renderer.ShouldRender && !stopRenderTriggered)
            {
                stopRenderTriggered = true;
                api.World.BlockAccessor.MarkBlockDirty(be.Pos, () => renderer.ShouldRender = false);
            }
        }

        public void StartAnimation(AnimationMetaData meta)
        {
            if (!activeAnimationsByAnimCode.ContainsKey(meta.Code) && renderer != null)
            {
                stopRenderTriggered = false;
                activeAnimationsByAnimCode[meta.Code] = meta;
                api.World.BlockAccessor.MarkBlockDirty(be.Pos, () => renderer.ShouldRender = true);
            }
        }



        public void StopAnimation(string code)
        {
            activeAnimationsByAnimCode.Remove(code);
        }


        public static AnimatorBase GetAnimator(ICoreAPI api, string cacheDictKey, Shape blockShape)
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
            renderer?.Dispose();
            (api as ICoreClientAPI)?.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }


        void IRenderer.Dispose()
        {
            
        }
    }
}
