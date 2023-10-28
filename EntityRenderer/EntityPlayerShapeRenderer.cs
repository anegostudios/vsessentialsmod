using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityPlayerShapeRenderer : EntitySkinnableShapeRenderer
    {
        MultiTextureMeshRef firstPersonMeshRef;
        MultiTextureMeshRef thirdPersonMeshRef;
        bool watcherRegistered;

        static float HandRenderFov = 90 * GameMath.DEG2RAD;

        public EntityPlayerShapeRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
        }

        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();
        }

        public override void TesselateShape()
        {
            if (eagent is EntityPlayer && eagent.GearInventory == null) return; // Player is not fully initialized yet

            Tesselate();
            

            if (eagent.GearInventory != null)
            {
                reloadSkin();
            }

            if (!watcherRegistered)
            {
                if (IsSelf)
                {
                    capi.Settings.Bool.AddWatcher("immersiveFpMode", (on) => { 
                        shapeFresh = false; 
                    });
                }

                watcherRegistered = true;
            }
        }

        public void Tesselate()
        {
            if (!IsSelf)
            {
                base.TesselateShape();
                return;
            }

            if (!loaded) return;
            shapeFresh = true;
            TesselateShape((meshData) => {
                disposeMeshes();

                if (capi.IsShuttingDown)
                {
                    return;
                }

                if (meshData.VerticesCount > 0)
                {
                    MeshData firstPersonMesh = meshData.EmptyClone();

                    thirdPersonMeshRef = capi.Render.UploadMultiTextureMesh(meshData);

                    if (capi.Settings.Bool["immersiveFpMode"])
                    {
                        HashSet<int> skipJointIds = new HashSet<int>();
                        loadJointIdsRecursive(entity.AnimManager.Animator.GetPosebyName("Neck"), skipJointIds);
                        firstPersonMesh.AddMeshData(meshData, (i) => !skipJointIds.Contains(meshData.CustomInts.Values[i * 4]));
                    }
                    else
                    {
                        HashSet<int> includeJointIds = new HashSet<int>();
                        loadJointIdsRecursive(entity.AnimManager.Animator.GetPosebyName("UpperArmL"), includeJointIds);
                        loadJointIdsRecursive(entity.AnimManager.Animator.GetPosebyName("UpperArmR"), includeJointIds);

                        firstPersonMesh.AddMeshData(meshData, (i) => includeJointIds.Contains(meshData.CustomInts.Values[i * 4]));
                    }

                    firstPersonMeshRef = capi.Render.UploadMultiTextureMesh(firstPersonMesh);
                }
            });
        }

        private void loadJointIdsRecursive(ElementPose elementPose, HashSet<int> outList)
        {
            outList.Add(elementPose.ForElement.JointId);
            foreach (var childpose in elementPose.ChildElementPoses) loadJointIdsRecursive(childpose, outList);
        }

        private void disposeMeshes()
        {
            if (firstPersonMeshRef != null)
            {
                firstPersonMeshRef.Dispose();
                firstPersonMeshRef = null;
            }
            if (thirdPersonMeshRef != null)
            {
                thirdPersonMeshRef.Dispose();
                thirdPersonMeshRef = null;
            }

            meshRefOpaque = null;
        }

        public override void RenderToGui(float dt, double posX, double posY, double posZ, float yawDelta, float size)
        {
            if (IsSelf)
            {
                meshRefOpaque = thirdPersonMeshRef;
            }

            base.RenderToGui(dt, posX, posY, posZ, yawDelta, size);
        }


        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            bool isHandRender = HandRenderMode && !isShadowPass;

            if (isHandRender)
            {
                capi.Render.Set3DProjection(capi.Render.ShaderUniforms.ZFar, HandRenderFov);
            }

            base.DoRender3DOpaque(dt, isShadowPass);

            if (isHandRender)
            {
                capi.Render.Reset3DProjection();
            }
        }

        public override void DoRender3DOpaqueBatched(float dt, bool isShadowPass)
        {
            bool isHandRender = HandRenderMode && !isShadowPass;

            if (IsSelf && firstPersonMeshRef != null && thirdPersonMeshRef != null && !firstPersonMeshRef.Disposed && !thirdPersonMeshRef.Disposed) // No idea why I have to check this thoroughly. Crashes otherwise when spamming the ifp mode settings
            {
                if (player != null && player.CameraMode == EnumCameraMode.FirstPerson && IsSelf && !isShadowPass) meshRefOpaque = firstPersonMeshRef;
                else meshRefOpaque = thirdPersonMeshRef;
            }

            if (isHandRender)
            {
                capi.Render.Set3DProjection(capi.Render.ShaderUniforms.ZFar, HandRenderFov);
                capi.Render.CurrentActiveShader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            }

            base.DoRender3DOpaqueBatched(dt, isShadowPass);

            if (isHandRender)
            {
                capi.Render.Reset3DProjection();
                capi.Render.CurrentActiveShader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            }
        }

        bool HandRenderMode => IsSelf && player?.ImmersiveFpMode==false && player?.CameraMode == EnumCameraMode.FirstPerson;

    }
}
