using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class ModSystemFpHands : ModSystem
    {
        public IShaderProgram fpModeItemShader;
        public IShaderProgram fpModeHandShader;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
        ICoreClientAPI capi;

        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;
            capi.Event.ReloadShader += LoadShaders;
            LoadShaders();
        }

        public bool LoadShaders()
        {
            fpModeItemShader = createProg();
            capi.Shader.RegisterFileShaderProgram("standard", fpModeItemShader);

            fpModeHandShader = createProg();
            capi.Shader.RegisterFileShaderProgram("entityanimated", fpModeHandShader);

            return fpModeItemShader.Compile() && fpModeHandShader.Compile();
        }

        private IShaderProgram createProg()
        {
            var prog = capi.Shader.NewShaderProgram();
            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.VertexShader.PrefixCode = "#define ALLOWDEPTHOFFSET 1\r\n";

            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            prog.FragmentShader.PrefixCode = "#define ALLOWDEPTHOFFSET 1\r\n";
            return prog;
        }
    }

    public class EntityPlayerShapeRenderer : EntitySkinnableShapeRenderer
    {
        MultiTextureMeshRef firstPersonMeshRef;
        MultiTextureMeshRef thirdPersonMeshRef;
        bool watcherRegistered;
        EntityPlayer entityPlayer;
        ModSystemFpHands modSys;


        public override bool DisplayChatMessages => true;

        public EntityPlayerShapeRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {   
            entityPlayer = entity as EntityPlayer;

            modSys = api.ModLoader.GetModSystem<ModSystemFpHands>();
        }

        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();
        }

        public override void TesselateShape()
        {
            if (entityPlayer.GearInventory == null) return; // Player is not fully initialized yet

            // Need to call this before tesselate or we will reference the wrong texture
            defaultTexSource = GetTextureSource();

            if (entityPlayer.GearInventory != null)
            {
                reloadSkin();
            }

            Tesselate();

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

        protected override void onMeshReady(MeshData meshData)
        {
            base.onMeshReady(meshData);
            if (!IsSelf) thirdPersonMeshRef = meshRefOpaque;
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

                    if (IsSelf && capi.Settings.Bool["immersiveFpMode"])
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


        public virtual float HandRenderFov
        {
            get { return capi.Settings.Int["fpHandsFoV"] * GameMath.DEG2RAD; }
        }

        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            if (IsSelf) entityPlayer.selfNowShadowPass = isShadowPass;

            bool isHandRender = HandRenderMode && !isShadowPass;

            loadModelMatrixForPlayer(entity, IsSelf, dt, isShadowPass);

            if (IsSelf && ((capi.Settings.Bool["immersiveFpMode"] && capi.Render.CameraType == EnumCameraMode.FirstPerson) || isShadowPass))
            {
                OriginPos.Set(0, 0, 0);
            }

            if (isHandRender && capi.HideGuis) return;

            if (isHandRender)
            {
                pMatrixNormalFov = (float[])capi.Render.CurrentProjectionMatrix.Clone();
                capi.Render.Set3DProjection(capi.Render.ShaderUniforms.ZFar, HandRenderFov);

                // Needed to reproject particles to the correct position
                pMatrixHandFov = (float[])capi.Render.CurrentProjectionMatrix.Clone();
            } else
            {
                pMatrixHandFov = null;
                pMatrixNormalFov = null;
            }

            if (isShadowPass)
            {
                DoRender3DAfterOIT(dt, true);
            }

            // This was rendered in DoRender3DAfterOIT() - WHY? It makes torches render in front of water
            if (DoRenderHeldItem && !entity.AnimManager.ActiveAnimationsByAnimCode.ContainsKey("lie") && !isSpectator)
            {
                RenderHeldItem(dt, isShadowPass, false);
                RenderHeldItem(dt, isShadowPass, true);
            }

            if (isHandRender)
            {
                if (!capi.Settings.Bool["hideFpHands"] && !entityPlayer.GetBehavior<EntityBehaviorTiredness>().IsSleeping)
                {
                    var prog = modSys.fpModeHandShader;

                    meshRefOpaque = firstPersonMeshRef;

                    prog.Use();
                    prog.Uniform("rgbaAmbientIn", capi.Render.AmbientColor);
                    prog.Uniform("rgbaFogIn", capi.Render.FogColor);
                    prog.Uniform("fogMinIn", capi.Render.FogMin);
                    prog.Uniform("fogDensityIn", capi.Render.FogDensity);
                    prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
                    prog.Uniform("alphaTest", 0.05f);
                    prog.Uniform("lightPosition", capi.Render.ShaderUniforms.LightPosition3D);
                    prog.Uniform("depthOffset", -0.3f - GameMath.Max(0, capi.Settings.Int["fieldOfView"] / 90f - 1) / 2f);

                    capi.Render.GlPushMatrix();
                    capi.Render.GlLoadMatrix(capi.Render.CameraMatrixOrigin);

                    base.DoRender3DOpaqueBatched(dt, false);

                    capi.Render.GlPopMatrix();

                    prog.Stop();
                }

                capi.Render.Reset3DProjection();
            }
        }

        protected override IShaderProgram getReadyShader()
        {
            if (!entityPlayer.selfNowShadowPass && HandRenderMode)
            {
                var prog = modSys.fpModeItemShader;
                prog.Use();
                prog.Uniform("depthOffset", -0.3f - GameMath.Max(0, capi.Settings.Int["fieldOfView"] / 90f - 1) / 2f);
                prog.Uniform("ssaoAttn", 1f);
                return prog;
            }

            return base.getReadyShader();
        }

        protected override void RenderHeldItem(float dt, bool isShadowPass, bool right)
        {
            if (IsSelf) entityPlayer.selfNowShadowPass = isShadowPass;

            bool isHandRender = HandRenderMode && !isShadowPass;
            if (!isHandRender || !capi.Settings.Bool["hideFpHands"])
            {
                base.RenderHeldItem(dt, isShadowPass, right);
            }
        }

        public override void DoRender3DOpaqueBatched(float dt, bool isShadowPass)
        {
            if (HandRenderMode && !isShadowPass)
            {
                return;
            }

            if (isShadowPass)
            {
                meshRefOpaque = thirdPersonMeshRef;
            }
            else
            {
                meshRefOpaque = IsSelf && player?.ImmersiveFpMode == true && player?.CameraMode == EnumCameraMode.FirstPerson ? firstPersonMeshRef : thirdPersonMeshRef;
            }

            base.DoRender3DOpaqueBatched(dt, isShadowPass);
        }

        bool HandRenderMode => IsSelf && player?.ImmersiveFpMode==false && player?.CameraMode == EnumCameraMode.FirstPerson;
        public float? HeldItemPitchFollowOverride { get; set; } = null;

        public void loadModelMatrixForPlayer(Entity entity, bool isSelf, float dt, bool isShadowPass)
        {
            EntityPlayer selfEplr = capi.World.Player.Entity;
            Mat4f.Identity(ModelMat);

            if (!isSelf)
            {
                // We use special positioning code for mounted entities that are on the same mount as we are.
                // While this should not be necesssary, because the client side physics does set the entity position accordingly, it does same to create 1-frame jitter if we dont specially handle this
                var selfMountedOn = selfEplr.MountedOn?.MountSupplier;
                var heMountedOn = (entity as EntityAgent).MountedOn?.MountSupplier;
                if (selfMountedOn != null && selfMountedOn == heMountedOn)
                {
                    var selfmountoffset = selfMountedOn.GetMountOffset(selfEplr);
                    var hemountoffset = heMountedOn.GetMountOffset(entity);
                    Mat4f.Translate(ModelMat, ModelMat, -selfmountoffset.X + hemountoffset.X, -selfmountoffset.Y + hemountoffset.Y, -selfmountoffset.Z + hemountoffset.Z);
                }
                else
                {
                    Mat4f.Translate(ModelMat, ModelMat, (float)(entity.Pos.X - selfEplr.CameraPos.X), (float)(entity.Pos.Y - selfEplr.CameraPos.Y), (float)(entity.Pos.Z - selfEplr.CameraPos.Z));
                }
            }

            float rotX = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateX : 0;
            float rotY = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateY : 0;
            float rotZ = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateZ : 0;
            float bodyYaw;

            if (!isSelf || capi.World.Player.CameraMode != EnumCameraMode.FirstPerson)
            {
                float yawDist = GameMath.AngleRadDistance(bodyYawLerped, eagent.BodyYaw);
                bodyYawLerped += GameMath.Clamp(yawDist, -dt * 8, dt * 8);
                bodyYaw = bodyYawLerped;
            }
            else
            {
                bodyYaw = capi.World.Player.ImmersiveFpMode ? eagent.BodyYaw : eagent.Pos.Yaw;
            }


            float bodyPitch = entityPlayer == null ? 0 : entityPlayer.WalkPitch;
            Mat4f.RotateX(ModelMat, ModelMat, entity.Pos.Roll + rotX * GameMath.DEG2RAD);
            Mat4f.RotateY(ModelMat, ModelMat, bodyYaw + (180 + rotY) * GameMath.DEG2RAD);
            var selfSwimming = isSelf && eagent.Swimming && !capi.World.Player.ImmersiveFpMode && capi.World.Player.CameraMode == EnumCameraMode.FirstPerson;

            if (!selfSwimming && (selfEplr?.Controls.Gliding != true || capi.World.Player.ImmersiveFpMode || capi.World.Player.CameraMode != EnumCameraMode.FirstPerson))
            {
                Mat4f.RotateZ(ModelMat, ModelMat, bodyPitch + rotZ * GameMath.DEG2RAD);
            }

            Mat4f.RotateX(ModelMat, ModelMat, sidewaysSwivelAngle);

            // Rotate player hands with pitch
            if (isSelf && selfEplr != null && !capi.World.Player.ImmersiveFpMode && capi.World.Player.CameraMode == EnumCameraMode.FirstPerson && !isShadowPass)
            {
                float itemSpecificPitchFollow = eagent.RightHandItemSlot?.Itemstack?.ItemAttributes?["heldItemPitchFollow"].AsFloat(0.75f) ?? 0.75f;

                float f = selfEplr?.Controls.IsFlying != true ? HeldItemPitchFollowOverride ?? (itemSpecificPitchFollow) : 1f;
                Mat4f.Translate(ModelMat, ModelMat, 0f, (float)entity.LocalEyePos.Y, 0f);
                Mat4f.RotateZ(ModelMat, ModelMat, (float)(entity.Pos.Pitch - GameMath.PI) * f);
                Mat4f.Translate(ModelMat, ModelMat, 0, -(float)entity.LocalEyePos.Y, 0f);
            }

            if (isSelf && !capi.World.Player.ImmersiveFpMode && capi.World.Player.CameraMode == EnumCameraMode.FirstPerson && !isShadowPass)
            {
                Mat4f.Translate(ModelMat, ModelMat, 0, capi.Settings.Float["fpHandsYOffset"], 0);
            }

            if (selfEplr != null)
            {
                float targetIntensity = entity.WatchedAttributes.GetFloat("intoxication");
                intoxIntensity += (targetIntensity - intoxIntensity) * dt / 3;
                capi.Render.PerceptionEffects.ApplyToTpPlayer(selfEplr, ModelMat, intoxIntensity);
            }

            float scale = entity.Properties.Client.Size;
            Mat4f.Scale(ModelMat, ModelMat, new float[] { scale, scale, scale });
            Mat4f.Translate(ModelMat, ModelMat, -0.5f, 0, -0.5f);
        }


        protected override void determineSidewaysSwivel(float dt)
        {
            double nowAngle = Math.Atan2(entity.Pos.Motion.Z, entity.Pos.Motion.X);
            double walkspeedsq = entity.Pos.Motion.LengthSq();

            if (walkspeedsq > 0.001 && entity.OnGround)
            {
                float angledist = GameMath.AngleRadDistance((float)prevAngleSwing, (float)nowAngle);
                sidewaysSwivelAngle -= GameMath.Clamp(angledist, -0.05f, 0.05f) * dt * 40 * (float)Math.Min(0.025f, walkspeedsq) * 80 * (eagent?.Controls.Backward == true ? -1 : 1);
                sidewaysSwivelAngle = GameMath.Clamp(sidewaysSwivelAngle, -0.3f, 0.3f);
            }

            sidewaysSwivelAngle *= Math.Min(0.99f, 1 - 0.1f * dt * 60f);
            prevAngleSwing = nowAngle;

            entityPlayer.sidewaysSwivelAngle = sidewaysSwivelAngle;
        }


        public override void Dispose()
        {
            base.Dispose();

            firstPersonMeshRef?.Dispose();
            thirdPersonMeshRef?.Dispose();

            IInventory inv = entityPlayer.Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
            if (inv != null) inv.SlotModified -= backPackSlotModified;
        }
    }






    
}
