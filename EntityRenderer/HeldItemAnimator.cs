using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class HeldItemAnimator
    {
        readonly Dictionary<string, AnimationMetaData> activeAnimations = new Dictionary<string, AnimationMetaData>();

        Shape? shape;
        ClientAnimator? animator;
        IAnimator? sourceAnimator;
        RunningAnimation[]? matchingAnimations;

        public MultiTextureMeshRef? ModelRef;
        public readonly float[] ModelMatrix = Mat4f.Create();
        public bool ShouldRender;
        public bool CullFaces;
        public float AlphaTest;
        public int ExtraGlow;

        public void Reset()
        {
            ShouldRender = false;
        }

        public void Clear()
        {
            shape = null;
            animator = null;
            sourceAnimator = null;
            matchingAnimations = null;
            activeAnimations.Clear();
            ModelRef = null;
            ShouldRender = false;
        }

        public bool MatchesWereCheckedFor(Shape animationShape, IAnimationManager sourceAnimationManager)
        {
            return shape == animationShape && sourceAnimator == sourceAnimationManager.Animator && matchingAnimations != null;
        }

        public void FindAnimationMatches(Shape animationShape, IAnimationManager sourceAnimationManager, string[] animationCodes)
        {
            shape = animationShape;
            animator = null;
            sourceAnimator = sourceAnimationManager.Animator;
            activeAnimations.Clear();
            ModelRef = null;

            List<RunningAnimation> matching = new List<RunningAnimation>(animationCodes.Length);
            for (int i = 0; i < animationCodes.Length; i++)
            {
                RunningAnimation? animation = sourceAnimationManager.GetAnimationState(animationCodes[i]);
                if (animation != null) matching.Add(animation);
            }
            matchingAnimations = matching.ToArray();
        }

        public bool HasRunningAnimation()
        {
            if (matchingAnimations == null) return false;

            for (int i = 0; i < matchingAnimations.Length; i++)
            {
                RunningAnimation animation = matchingAnimations[i];
                if (animation.Running && animation.meta.Attributes?.IsTrue("animateHeldItem") == true) return true;
            }

            return false;
        }

        public bool Sync(Shape animationShape, IAnimationManager sourceAnimationManager)
        {
            if (animator == null || shape != animationShape)
            {
                shape = animationShape;
                animator = new ClientAnimator(null, shape.Animations, shape.Elements, shape.JointsById);
                sourceAnimator = null;
                matchingAnimations = null;
                activeAnimations.Clear();
            }

            ClientAnimator currentAnimator = animator;

            bool anyRunning = false;
            foreach (RunningAnimation itemAnimation in currentAnimator.Animations)
            {
                string code = itemAnimation.Animation.Code;
                RunningAnimation? sourceAnimation = sourceAnimationManager.GetAnimationState(code);
                if (sourceAnimation == null || !sourceAnimation.Running || sourceAnimation.meta.Attributes?.IsTrue("animateHeldItem") != true)
                {
                    activeAnimations.Remove(code);
                    itemAnimation.Stop();
                    continue;
                }

                anyRunning = true;
                if (!activeAnimations.TryGetValue(code, out AnimationMetaData meta))
                {
                    activeAnimations[code] = meta = sourceAnimation.meta.Clone();
                }

                float itemFrame = sourceAnimation.CurrentFrame;
                meta.StartFrameOnce = itemFrame;
                itemAnimation.CurrentFrame = itemFrame;
                itemAnimation.EasingFactor = sourceAnimation.EasingFactor;
                itemAnimation.Iterations = 0;
            }

            currentAnimator.OnFrame(activeAnimations, 0);
            return anyRunning;
        }

        public ElementPose? GetPosebyName(string name)
        {
            return animator?.GetPosebyName(name);
        }

        public void UploadMatrices(IShaderProgram shader)
        {
            ClientAnimator currentAnimator = animator!;
            shader.UBOs["Animation"].Update(currentAnimator.Matrices, 0, currentAnimator.MaxJointId * 16 * 4);
        }
    }
}
