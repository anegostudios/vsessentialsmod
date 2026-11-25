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
        private readonly BlockEntity be;
        private readonly float settleDurationSeconds;
        private StaticAnimationState staticAnimationState;
        private MeshData sourceMesh;
        private volatile MeshData settledMesh;
        private volatile bool staticMeshInitialized;
        private bool disposed;
        public Action<MeshData> OnAfterTesselate;

        public bool UsesStaticMesh => settleDurationSeconds > 0 && staticMeshInitialized;

        public bool Initialized => animator != null;
        public bool Disposed => disposed;

        public BlockEntityAnimationUtil(ICoreAPI api, BlockEntity be) : this(api, be, 0)
        {
        }

        /// <summary>
        /// Variant of the anim util with static meshing after a animation has settled
        /// </summary>
        /// <param name="api"></param>
        /// <param name="be"></param>
        /// <param name="settleDurationSeconds"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public BlockEntityAnimationUtil(ICoreAPI api, BlockEntity be, float settleDurationSeconds) : base(api, be.Pos.ToVec3d(), be.Pos.dimension)
        {
            if (settleDurationSeconds < 0 || settleDurationSeconds > 1000) throw new InvalidOperationException("settleDurationSeconds must be within 0...1000");
            
            this.be = be;
            this.settleDurationSeconds = api.Side == EnumAppSide.Client ? settleDurationSeconds : 0;
        }

        /// <summary>
        /// Door animations are single frame animations, the motion is merely the ease-in of the animation. This is a heuristic to get the settle duration.
        /// </summary>
        /// <param name="easingSpeed"></param>
        /// <returns></returns>
        public static float GetSettleDurationFromEasing(float easingSpeed)
        {
            if (easingSpeed <= 0) return 0;
            return MathF.Ceiling(8f / easingSpeed);
        }

        public override void InitializeAnimator(string cacheDictKey, MeshData meshdata, Shape shape, Vec3f rotation, EnumRenderStage renderStage = EnumRenderStage.Opaque)
        {
            InitializeAnimator(cacheDictKey, meshdata, shape, rotation, false, renderStage);
        }

        /// <summary>
        /// Initializes the animator, optionally retaining an immutable source mesh without copying it.
        /// </summary>
        /// <remarks>When shareSourceMesh is true, the caller must not modify meshdata while this animator uses it.</remarks>
        public virtual void InitializeAnimator(string cacheDictKey, MeshData meshdata, Shape shape, Vec3f rotation, bool shareSourceMesh, EnumRenderStage renderStage = EnumRenderStage.Opaque)
        {
            InitializeAnimator(cacheDictKey, meshdata, shape, rotation, shareSourceMesh, null, renderStage);
        }

        internal void InitializeAnimator(string cacheDictKey, MeshData meshdata, Shape shape, Vec3f rotation, bool shareSourceMesh, Action<AnimatableRenderer> applyPlacement, EnumRenderStage renderStage = EnumRenderStage.Opaque)
        {
            staticMeshInitialized = false;
            base.InitializeAnimator(cacheDictKey, meshdata, shape, rotation, renderStage);

            if (settleDurationSeconds > 0)
            {
                sourceMesh = shareSourceMesh ? meshdata : meshdata.Clone();
                if (!shareSourceMesh) sourceMesh.NormalsCount = meshdata.NormalsCount;
                staticAnimationState = new StaticAnimationState(settleDurationSeconds);
            }
            else if (applyPlacement == null) return;

            StaticAnimationState initializedState = staticAnimationState;
            AnimatableRenderer initializedRenderer = renderer;

            void FinishStaticInitialization()
            {
                if (disposed || staticAnimationState != initializedState || renderer != initializedRenderer) return;

                applyPlacement?.Invoke(initializedRenderer);
                if (initializedState == null) return;

                initializedRenderer.ShouldRender = false;
                bool hasActiveAnimation = activeAnimationsByAnimCode.Count > 0;
                EnumStaticAnimState handoff = hasActiveAnimation ? initializedState.OnAnimationChanged() : EnumStaticAnimState.None;
                settledMesh = hasActiveAnimation ? null : BakePose();
                staticMeshInitialized = true;
                UpdateStaticAnimState(handoff);
            }

            if (Environment.CurrentManagedThreadId == RuntimeEnv.MainThreadId) FinishStaticInitialization();
            else capi.Event.EnqueueMainThreadTask(FinishStaticInitialization, "staticAnimationInit");
        }

        /// <summary>
        /// Refreshes the baked pose after changing the renderer's placement transform.
        /// Call on the main thread before marking the block dirty.
        /// </summary>
        public void RefreshStaticMesh()
        {
            if (!UsesStaticMesh || sourceMesh == null || disposed) return;

            if (staticAnimationState.IncludeTerrainMesh) settledMesh = BakePose();
            if (staticAnimationState.RefreshPendingTerrainMesh())
            {
                UpdateStaticAnimState(EnumStaticAnimState.AddTerrainMesh);
            }
        }

        /// <summary>
        /// Adds the resting mesh, or suppresses it during animation. Returns true when the caller
        /// should skip its usual mesh for this animated part; unrelated static parts can still be added.
        /// </summary>
        public bool OnTesselation(ITerrainMeshPool mesher)
        {
            if (!UsesStaticMesh) return false;

            MeshData mesh = settledMesh;
            if (mesh != null) mesher.AddMeshData(mesh);
            return true;
        }

        /// <summary>
        /// Wakes a baked animation and restarts its settling delay after externally changing
        /// animation parameters. StartAnimation and StopAnimation call this automatically.
        /// </summary>
        public void NotifyAnimationChanged()
        {
            if (UsesStaticMesh && !disposed) UpdateStaticAnimState(staticAnimationState.OnAnimationChanged());
        }

        public override bool StartAnimation(AnimationMetaData meta)
        {
            bool changed = base.StartAnimation(meta);
            if (changed) NotifyAnimationChanged();
            return changed;
        }

        public override void StopAnimation(string code)
        {
            bool changed = activeAnimationsByAnimCode.ContainsKey(code);
            base.StopAnimation(code);
            if (changed) NotifyAnimationChanged();
        }

        public override void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (settleDurationSeconds <= 0)
            {
                base.OnRenderFrame(deltaTime, stage);
                return;
            }
            if (disposed || !staticMeshInitialized || !staticAnimationState.AdvanceAnimation || capi.IsGamePaused) return;

            base.OnRenderFrame(deltaTime, stage);

            UpdateStaticAnimState(staticAnimationState.Advance(deltaTime));
        }

        private MeshData BakePose()
        {
            float[] transform = Mat4f.Create();
            if (renderer.CustomTransform != null)
            {
                Array.Copy(renderer.CustomTransform, transform, 16);
            }
            else
            {
                Mat4f.Translate(transform, transform, 0.5f, 0, 0.5f);
                Mat4f.Scale(transform, transform, renderer.ScaleX, renderer.ScaleY, renderer.ScaleZ);
                Mat4f.RotateY(transform, transform, renderer.rotationDeg.Y * GameMath.DEG2RAD);
                Mat4f.Translate(transform, transform, -0.5f, 0, -0.5f);
            }

            return BEBehaviorAnimatable.BakePoseMesh(sourceMesh, animator.Matrices, transform);
        }

        private void UpdateStaticAnimState(EnumStaticAnimState handoff)
        {
            if (handoff == EnumStaticAnimState.None) return;

            settledMesh = handoff == EnumStaticAnimState.AddTerrainMesh ? BakePose() : null;
            StaticAnimationState requestedState = staticAnimationState;
            int generation = staticAnimationState.Generation;

            api.World.BlockAccessor.MarkBlockDirty(be.Pos, () =>
            {
                if (disposed || staticAnimationState != requestedState || staticAnimationState.Generation != generation) return;

                EnumStaticAnimState next = staticAnimationState.CompleteHandoff(generation);
                renderer.ShouldRender = staticAnimationState.RenderAnimation;
                UpdateStaticAnimState(next);
            });
        }

        public override void Dispose()
        {
            disposed = true;
            settledMesh = null;
            sourceMesh = null;
            base.Dispose();
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
            if (settleDurationSeconds > 0) return;

            if (animsNowActive)
            {
                if (renderer != null) api.World.BlockAccessor.MarkBlockDirty(be.Pos, () => renderer.ShouldRender = true);
            } else
            {
                api.World.BlockAccessor.MarkBlockDirty(be.Pos, () => renderer.ShouldRender = false);
            }
        }


    }

    internal enum EnumStaticAnimState
    {
        None,
        RemoveTerrainMesh,
        AddTerrainMesh
    }

    /// <summary>Serializes terrain/animation handoffs while retaining the latest animation request.</summary>
    internal sealed class StaticAnimationState(float settleDurationSeconds)
    {
        private enum EnumPhase { Static, RemovingTerrainMesh, Animated, AddingTerrainMesh }

        private EnumPhase phase;
        private float secondsRemaining;
        private bool animationRequested;

        public int Generation { get; private set; }
        public bool AdvanceAnimation => phase == EnumPhase.Animated;
        public bool RenderAnimation => phase == EnumPhase.Animated || phase == EnumPhase.AddingTerrainMesh;
        public bool IncludeTerrainMesh => phase == EnumPhase.Static || phase == EnumPhase.AddingTerrainMesh;

        public bool RefreshPendingTerrainMesh()
        {
            if (phase != EnumPhase.AddingTerrainMesh) return false;
            Generation++;
            return true;
        }

        public EnumStaticAnimState OnAnimationChanged()
        {
            switch (phase)
            {
                case EnumPhase.Static:
                    phase = EnumPhase.RemovingTerrainMesh;
                    Generation++;
                    return EnumStaticAnimState.RemoveTerrainMesh;
                case EnumPhase.Animated:
                    secondsRemaining = settleDurationSeconds;
                    break;
                case EnumPhase.AddingTerrainMesh:
                    animationRequested = true;
                    break;
            }
            return EnumStaticAnimState.None;
        }

        public EnumStaticAnimState Advance(float deltaTime)
        {
            if (phase != EnumPhase.Animated) return EnumStaticAnimState.None;

            secondsRemaining = Math.Max(0, secondsRemaining - deltaTime);
            if (secondsRemaining > 0) return EnumStaticAnimState.None;

            phase = EnumPhase.AddingTerrainMesh;
            Generation++;
            return EnumStaticAnimState.AddTerrainMesh;
        }

        public EnumStaticAnimState CompleteHandoff(int generation)
        {
            if (generation != Generation) return EnumStaticAnimState.None;

            if (phase == EnumPhase.RemovingTerrainMesh)
            {
                phase = EnumPhase.Animated;
                secondsRemaining = settleDurationSeconds;
            }
            else if (phase == EnumPhase.AddingTerrainMesh)
            {
                phase = EnumPhase.Static;
                if (animationRequested)
                {
                    animationRequested = false;
                    return OnAnimationChanged();
                }
            }
            return EnumStaticAnimState.None;
        }
    }
}
