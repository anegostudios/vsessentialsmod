using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    public class MessageTexture
    {
        public LoadedTexture tex;
        public string message;
        public long receivedTime;
    }

    public delegate void OnEntityShapeTesselationDelegate(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned);
    public delegate float OnGetFrostAlpha();


    public class EntityShapeRenderer : EntityRenderer, ITexPositionSource
    {
        protected long listenerId;

        protected LoadedTexture debugTagTexture = null;

        protected MultiTextureMeshRef meshRefOpaque;

        protected Vec4f color = new Vec4f(1, 1, 1, 1);
        protected long lastDebugInfoChangeMs = 0;
        protected bool isSpectator;
        protected IClientPlayer player;
        public float bodyYawLerped = 0;

        public Vec3f OriginPos = new Vec3f();
        public float[] ModelMat = Mat4f.Create();
        protected float[] tmpMvMat = Mat4f.Create();
        protected Matrixf ItemModelMat = new Matrixf();

        public bool DoRenderHeldItem;
        public virtual bool DisplayChatMessages { get; set; }

        public int AddRenderFlags;
        public double WindWaveIntensity = 1f;
        public bool glitchFlicker;

        public bool frostable;
        public float frostAlpha;
        public float targetFrostAlpha;
        public OnGetFrostAlpha getFrostAlpha;
        public float frostAlphaAccum;

        protected List<MessageTexture> messageTextures = null;


        protected EntityAgent eagent;

        public CompositeShape OverrideCompositeShape;
        public Shape OverrideEntityShape;
        public string[] OverrideSelectiveElements = null;

        public bool glitchAffected;
        protected IInventory gearInv;

        protected ITexPositionSource defaultTexSource;

        protected Vec4f lightrgbs;
        protected float intoxIntensity;



        public Size2i AtlasSize { get { return capi.EntityTextureAtlas.Size; } }
        public TextureAtlasPosition skinTexPos;
        public virtual TextureAtlasPosition this[string textureCode]
        {
            get
            {
                return defaultTexSource[textureCode] ?? skinTexPos;
            }
        }


        public EntityShapeRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            eagent = entity as EntityAgent;

            DoRenderHeldItem = true;
            glitchAffected = true;
            glitchFlicker = entity.Properties.Attributes?["glitchFlicker"].AsBool(false) ?? false;
            frostable = entity.Properties.Attributes?["frostable"].AsBool(true) ?? true;
            shouldSwivelFromMotion = entity.Properties.Attributes?["shouldSwivelFromMotion"].AsBool(true) ?? true;
            maxSwivelAngle = entity.Properties.Attributes?["maxSwivelAngle"].AsFloat(GameMath.RAD2DEG * 0.4f) ?? GameMath.RAD2DEG * 0.4f;
            frostAlphaAccum = (float)api.World.Rand.NextDouble();


            listenerId = api.Event.RegisterGameTickListener(UpdateDebugInfo, 250);
            OnDebugInfoChanged();

            if (DisplayChatMessages)
            {
                messageTextures = new List<MessageTexture>();
                api.Event.ChatMessage += OnChatMessage;
            }

            api.Event.ReloadShapes += entity.MarkShapeModified;


            // Called every 5 seconds
            getFrostAlpha = () =>
            {
                var pos = entity.Pos.AsBlockPos;
                var conds = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);
                if (conds == null) return targetFrostAlpha;

                float dist = 1 - GameMath.Clamp((api.World.BlockAccessor.GetDistanceToRainFall(pos, 5) - 2) / 3f, 0, 1f);
                float a = GameMath.Clamp((Math.Max(0, -conds.Temperature) - 2) / 5f, 0, 1) * dist;

                if (a > 0)
                {
                    var oldconds = api.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.ForSuppliedDateValues, api.World.Calendar.TotalDays - 4 / api.World.Calendar.HoursPerDay);
                    float rain = Math.Max(oldconds.Rainfall, conds.Rainfall);
                    a *= rain;
                }

                return Math.Max(0, a);
            };
        }



        protected bool loaded = false;
        public override void OnEntityLoaded()
        {
            loaded = true;
            prevY = entity.Pos.Y;
            prevPosXSwing = entity.Pos.X;
            prevPosZSwing = entity.Pos.Z;

        }

        protected void OnChatMessage(int groupId, string message, EnumChatType chattype, string data)
        {
            if (data != null && data.Contains("from:") && entity.Pos.SquareDistanceTo(capi.World.Player.Entity.Pos.XYZ) < 20 * 20 && message.Length > 0)
            {
                string[] parts = data.Split(new char[] { ',' }, 2);
                if (parts.Length < 2) return;

                string[] partone = parts[0].Split(new char[] { ':' }, 2);
                string[] parttwo = parts[1].Split(new char[] { ':' }, 2);
                if (partone[0] != "from") return;

                int.TryParse(partone[1], out int entityid);
                if (entity.EntityId == entityid)
                {
                    message = parttwo[1];

                    // Crappy fix
                    message = message.Replace("&lt;", "<").Replace("&gt;", ">");

                    LoadedTexture tex = capi.Gui.TextTexture.GenTextTexture(
                        message,
                        new CairoFont(25, GuiStyle.StandardFontName, ColorUtil.WhiteArgbDouble),
                        350,
                        new TextBackground() { FillColor = GuiStyle.DialogLightBgColor, Padding = 3, Radius = GuiStyle.ElementBGRadius },
                        EnumTextOrientation.Center
                    );

                    messageTextures.Insert(0, new MessageTexture()
                    {
                        tex = tex,
                        message = message,
                        receivedTime = capi.World.ElapsedMilliseconds
                    });

                }
            }
        }





        public virtual void TesselateShape()
        {
            if (!loaded) return;

            ims = entity.GetInterface<IMountable>();

            TesselateShape(onMeshReady);
        }

        protected virtual void onMeshReady(MeshData meshData)
        {
            if (meshRefOpaque != null)
            {
                meshRefOpaque.Dispose();
                meshRefOpaque = null;
            }

            if (capi.IsShuttingDown)
            {
                return;
            }
            if (meshData.VerticesCount > 0)
            {
                meshRefOpaque = capi.Render.UploadMultiTextureMesh(meshData);
            }
        }

        public virtual void TesselateShape(Action<MeshData> onMeshDataReady, string[] overrideSelectiveElements = null)
        {
            if (!loaded)
            {
                return;
            }

            CompositeShape compositeShape = OverrideCompositeShape != null ? OverrideCompositeShape : entity.Properties.Client.Shape;
            Shape entityShape = OverrideEntityShape != null ? OverrideEntityShape : entity.Properties.Client.LoadedShapeForEntity;

            if (entityShape == null)
            {
                return;
            }

            entity.OnTesselation(ref entityShape, compositeShape.Base.ToString());
            defaultTexSource = GetTextureSource();
            var ovse = overrideSelectiveElements ?? this.OverrideSelectiveElements;

            TyronThreadPool.QueueTask(() =>
            {
                MeshData meshdata;

                if (entity.Properties.Client.Shape.VoxelizeTexture)
                {
                    int altTexNumber = entity.WatchedAttributes.GetInt("textureIndex", 0);

                    TextureAtlasPosition pos = defaultTexSource["all"];
                    var FirstTexture = entity.Properties.Client.FirstTexture;
                    CompositeTexture[] Alternates = FirstTexture.Alternates;

                    CompositeTexture tex = altTexNumber == 0 ? FirstTexture : Alternates[altTexNumber % Alternates.Length];
                    meshdata = capi.Tesselator.VoxelizeTexture(tex, capi.EntityTextureAtlas.Size, pos);
                    for (int i = 0; i < meshdata.xyz.Length; i += 3)
                    {
                        meshdata.xyz[i] -= 0.125f;
                        meshdata.xyz[i + 1] -= 0.5f;
                        meshdata.xyz[i + 2] += 0.125f / 2;
                    }
                }
                else
                {
                    try
                    {
                        TesselationMetaData meta = new TesselationMetaData()
                        {
                            QuantityElements = compositeShape.QuantityElements,
                            SelectiveElements = ovse ?? compositeShape.SelectiveElements,
                            IgnoreElements = compositeShape.IgnoreElements,
                            TexSource = this,
                            WithJointIds = true,
                            WithDamageEffect = true,
                            TypeForLogging = "entity",
                            Rotation = new Vec3f(compositeShape.rotateX, compositeShape.rotateY, compositeShape.rotateZ)
                        };

                        capi.Tesselator.TesselateShape(meta, entityShape, out meshdata);

                        meshdata.Translate(compositeShape.offsetX, compositeShape.offsetY, compositeShape.offsetZ);
                    }
                    catch (Exception e)
                    {
                        capi.World.Logger.Fatal("Failed tesselating entity {0} with id {1}. Entity will probably be invisible!.", entity.Code, entity.EntityId);
                        capi.World.Logger.Fatal(e);
                        return;
                    }
                }

                capi.Event.EnqueueMainThreadTask(() =>
                {
                    onMeshDataReady(meshdata);
                    entity.OnTesselated();
                }, "uploadentitymesh");

                capi.TesselatorManager.ThreadDispose();
            });
        }


        protected virtual ITexPositionSource GetTextureSource()
        {
            return entity.GetTextureSource();
        }

        protected void UpdateDebugInfo(float dt)
        {
            OnDebugInfoChanged();

            entity.DebugAttributes.MarkClean();
        }



        protected void OnDebugInfoChanged()
        {
            bool showDebuginfo = capi.Settings.Bool["showEntityDebugInfo"];

            if (showDebuginfo && !entity.DebugAttributes.AllDirty && !entity.DebugAttributes.PartialDirty && debugTagTexture != null) return;

            if (debugTagTexture != null)
            {
                // Don't refresh if player is more than 10 blocks away, so its less laggy
                if (showDebuginfo && capi.World.Player.Entity.Pos.SquareDistanceTo(entity.Pos) > 15 * 15 && debugTagTexture.Width > 10)
                {
                    return;
                }

                debugTagTexture.Dispose();
                debugTagTexture = null;
            }

            if (!showDebuginfo) return;


            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<string, IAttribute> val in entity.DebugAttributes)
            {
                text.AppendLine(val.Key + ": " + val.Value.ToString());
            }

            debugTagTexture = capi.Gui.TextTexture.GenUnscaledTextTexture(
                text.ToString(),
                new CairoFont(20, GuiStyle.StandardFontName, ColorUtil.WhiteArgbDouble),
                new TextBackground() { FillColor = GuiStyle.DialogDefaultBgColor, Padding = 3, Radius = GuiStyle.ElementBGRadius }
            );

            lastDebugInfoChangeMs = entity.World.ElapsedMilliseconds;
        }


        public override void BeforeRender(float dt)
        {
            if (!entity.ShapeFresh)
            {
                TesselateShape();
                capi.World.FrameProfiler.Mark("esr-tesseleateshape");
            }

            lightrgbs = capi.World.BlockAccessor.GetLightRGBs((int)(entity.Pos.X + entity.SelectionBox.X1 - entity.OriginSelectionBox.X1), (int)entity.Pos.InternalY, (int)(entity.Pos.Z + entity.SelectionBox.Z1 - entity.OriginSelectionBox.Z1));

            if (entity.SelectionBox.Y2 > 1)
            {
                Vec4f lightrgbs2 = capi.World.BlockAccessor.GetLightRGBs((int)(entity.Pos.X + entity.SelectionBox.X1 - entity.OriginSelectionBox.X1), (int)entity.Pos.InternalY + 1, (int)(entity.Pos.Z + entity.SelectionBox.Z1 - entity.OriginSelectionBox.Z1));
                if (lightrgbs2.W > lightrgbs.W) lightrgbs = lightrgbs2;
            }

            if (meshRefOpaque == null) return;

            if (player == null && entity is EntityPlayer)
            {
                player = capi.World.PlayerByUid((entity as EntityPlayer).PlayerUID) as IClientPlayer;
            }


            if (capi.IsGamePaused) return;


            frostAlphaAccum += dt;
            if (frostAlphaAccum > 5)
            {
                frostAlphaAccum = 0;
                targetFrostAlpha = getFrostAlpha();
            }


            isSpectator = player != null && player.WorldData.CurrentGameMode == EnumGameMode.Spectator;
            if (isSpectator) return;


            if (DisplayChatMessages && messageTextures.Count > 0)
            {
                MessageTexture tex = messageTextures.Last();
                if (capi.World.ElapsedMilliseconds > tex.receivedTime + 3500 + 100 * (tex.message.Length - 10))
                {
                    messageTextures.RemoveAt(messageTextures.Count - 1);
                    tex.tex.Dispose();
                }
            }

            determineSidewaysSwivel(dt);
        }




        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            if (isSpectator) return;

            loadModelMatrix(entity, dt, isShadowPass);
            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            OriginPos.Set((float)(entity.Pos.X - camPos.X), (float)(entity.Pos.InternalY - camPos.Y), (float)(entity.Pos.Z - camPos.Z));

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
        }




        float accum = 0;
        // Needed to reproject particles to the correct position
        protected float[] pMatrixHandFov = null;
        protected float[] pMatrixNormalFov = null;

        protected virtual IShaderProgram getReadyShader()
        {
            var prog = capi.Render.StandardShader;
            prog.Use();
            return prog;
        }

        protected virtual void RenderHeldItem(float dt, bool isShadowPass, bool right)
        {
            ItemSlot slot = right ? eagent?.RightHandItemSlot : eagent?.LeftHandItemSlot;
            ItemStack stack = slot?.Itemstack;
            if (stack == null || slot is ItemSlotSkill) return;

            AttachmentPointAndPose apap = entity.AnimManager?.Animator?.GetAttachmentPointPose(right ? "RightHand" : "LeftHand");
            if (apap == null) return;

            ItemRenderInfo renderInfo = capi.Render.GetItemStackRenderInfo(slot, right ? EnumItemRenderTarget.HandTp : EnumItemRenderTarget.HandTpOff, dt);

            RenderItem(dt, isShadowPass, stack, apap, renderInfo);
        }

        protected virtual void RenderItem(float dt, bool isShadowPass, ItemStack stack, AttachmentPointAndPose apap, ItemRenderInfo renderInfo)
        {
            IRenderAPI rapi = capi.Render;
            AttachmentPoint ap = apap.AttachPoint;
            IShaderProgram prog = null;

            if (renderInfo?.Transform == null || renderInfo.ModelRef == null) return; // Happens with unknown items/blocks

            var itemTransform = renderInfo.Transform.EnsureDefaultValues();
            var itemOrigin = itemTransform.Origin;
            var itemTranslation = itemTransform.Translation;
            var itemRotation = itemTransform.Rotation;
            var itemScaleXYZ = itemTransform.ScaleXYZ;
            ItemModelMat
                .Set(ModelMat)
                .Mul(apap.AnimModelMatrix)
                .Translate(itemOrigin.X, itemOrigin.Y, itemOrigin.Z)
                .Scale(itemScaleXYZ.X, itemScaleXYZ.Y, itemScaleXYZ.Z)
                .Translate(ap.PosX / 16f + itemTranslation.X, ap.PosY / 16f + itemTranslation.Y, ap.PosZ / 16f + itemTranslation.Z)
                .RotateX((float)(ap.RotationX + itemRotation.X) * GameMath.DEG2RAD)
                .RotateY((float)(ap.RotationY + itemRotation.Y) * GameMath.DEG2RAD)
                .RotateZ((float)(ap.RotationZ + itemRotation.Z) * GameMath.DEG2RAD)
                .Translate(-itemOrigin.X, -itemOrigin.Y, -itemOrigin.Z)
            ;

            string samplername = "tex";
            if (isShadowPass)
            {
                samplername = "tex2d";
                rapi.CurrentActiveShader.BindTexture2D("tex2d", renderInfo.TextureId, 0);
                float[] mvpMat = Mat4f.Mul(ItemModelMat.Values, capi.Render.CurrentModelviewMatrix, ItemModelMat.Values);
                Mat4f.Mul(mvpMat, capi.Render.CurrentProjectionMatrix, mvpMat);

                capi.Render.CurrentActiveShader.UniformMatrix("mvpMatrix", mvpMat);
                capi.Render.CurrentActiveShader.Uniform("origin", new Vec3f());
            }
            else
            {
                prog = getReadyShader();

                prog.Uniform("dontWarpVertices", (int)0);
                prog.Uniform("addRenderFlags", (int)0);
                prog.Uniform("normalShaded", (int)1);
                prog.Uniform("tempGlowMode", stack.ItemAttributes?["tempGlowMode"].AsInt() ?? 0);
                prog.Uniform("rgbaTint", ColorUtil.WhiteArgbVec);
                prog.Uniform("alphaTest", renderInfo.AlphaTest);
                prog.Uniform("damageEffect", renderInfo.DamageEffect);

                prog.Uniform("overlayOpacity", renderInfo.OverlayOpacity);
                if (renderInfo.OverlayTexture != null && renderInfo.OverlayOpacity > 0)
                {
                    prog.BindTexture2D("tex2dOverlay", renderInfo.OverlayTexture.TextureId, 1);
                    prog.Uniform("overlayTextureSize", new Vec2f(renderInfo.OverlayTexture.Width, renderInfo.OverlayTexture.Height));
                    prog.Uniform("baseTextureSize", new Vec2f(renderInfo.TextureSize.Width, renderInfo.TextureSize.Height));
                    TextureAtlasPosition texPos = rapi.GetTextureAtlasPosition(stack);
                    prog.Uniform("baseUvOrigin", new Vec2f(texPos.x1, texPos.y1));
                }


                int temp = (int)stack.Collectible.GetTemperature(capi.World, stack);
                float[] glowColor = ColorUtil.GetIncandescenceColorAsColor4f(temp);
                var gi = GameMath.Clamp((temp - 500) / 3, 0, 255);

                var baked = (stack.Item?.FirstTexture ?? stack.Block?.FirstTextureInventory)?.Baked;
                Vec4f vec = baked == null ? new Vec4f(1, 1, 1, 1) : ColorUtil.ToRGBAVec4f(capi.BlockTextureAtlas.GetAverageColor(baked.TextureSubId));
                prog.Uniform("averageColor", vec);
                prog.Uniform("extraGlow", (int)gi);
                prog.Uniform("rgbaAmbientIn", rapi.AmbientColor);
                prog.Uniform("rgbaLightIn", lightrgbs);
                prog.Uniform("rgbaGlowIn", new Vec4f(glowColor[0], glowColor[1], glowColor[2], gi / 255f));
                prog.Uniform("rgbaFogIn", rapi.FogColor);
                prog.Uniform("fogMinIn", rapi.FogMin);
                prog.Uniform("fogDensityIn", rapi.FogDensity);
                prog.Uniform("normalShaded", renderInfo.NormalShaded ? 1 : 0);

                prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
                prog.UniformMatrix("viewMatrix", rapi.CameraMatrixOriginf);
                prog.UniformMatrix("modelMatrix", ItemModelMat.Values);
            }


            if (!renderInfo.CullFaces)
            {
                rapi.GlDisableCullFace();
            }

            rapi.RenderMultiTextureMesh(renderInfo.ModelRef, samplername);

            if (!isShadowPass) prog.Uniform("tempGlowMode", 0);
            if (!renderInfo.CullFaces)
            {
                rapi.GlEnableCullFace();
            }

            if (!isShadowPass)
            {
                prog.Uniform("damageEffect", 0f);
                prog.Stop();

                float windAffectednessAtPos = Math.Max(0, 1 - capi.World.BlockAccessor.GetDistanceToRainFall(entity.Pos.AsBlockPos) / 5f);

                AdvancedParticleProperties[] ParticleProperties = stack.Collectible?.ParticleProperties;

                if (stack.Collectible != null && !capi.IsGamePaused)
                {
                    Vec4f pos = ItemModelMat.TransformVector(new Vec4f(stack.Collectible.TopMiddlePos.X, stack.Collectible.TopMiddlePos.Y, stack.Collectible.TopMiddlePos.Z, 1));

                    if (pMatrixHandFov != null)
                    {
                        var screenSpaceCoordNormalFov = new Matrixf().Set(pMatrixHandFov).Mul(rapi.CameraMatrixOriginf).TransformVector(pos);
                        var unprojectMatrix = new Matrixf(rapi.CameraMatrixOriginf).Invert().Mul(new Matrixf(pMatrixNormalFov).Invert());
                        pos = unprojectMatrix.TransformVector(screenSpaceCoordNormalFov);
                    }

                    EntityPlayer entityPlayer = capi.World.Player.Entity;
                    accum += dt;
                    if (ParticleProperties != null && ParticleProperties.Length > 0 && accum > 0.05f)
                    {
                        accum = accum % 0.025f;

                        for (int i = 0; i < ParticleProperties.Length; i++)
                        {
                            AdvancedParticleProperties bps = ParticleProperties[i];

                            bps.WindAffectednesAtPos = windAffectednessAtPos;
                            bps.WindAffectednes = windAffectednessAtPos;

                            bps.basePos.X = pos.X + entityPlayer.CameraPos.X;
                            bps.basePos.Y = pos.Y + entityPlayer.CameraPos.Y;
                            bps.basePos.Z = pos.Z + entityPlayer.CameraPos.Z;

                            eagent.World.SpawnParticles(bps);
                        }
                    }
                }

            }
        }


        public override void RenderToGui(float dt, double posX, double posY, double posZ, float yawDelta, float size)
        {
            loadModelMatrixForGui(entity, posX, posY, posZ, yawDelta, size);

            if (meshRefOpaque != null)
            {
                capi.Render.CurrentActiveShader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
                capi.Render.CurrentActiveShader.UniformMatrix("modelViewMatrix", Mat4f.Mul(ModelMat, capi.Render.CurrentModelviewMatrix, ModelMat));
                capi.Render.RenderMultiTextureMesh(meshRefOpaque, "tex2d");
            }

            if (!entity.ShapeFresh)
            {
                TesselateShape();
            }
        }



        public override void DoRender3DOpaqueBatched(float dt, bool isShadowPass)
        {
            if (isSpectator || meshRefOpaque == null) return;

            var prog = capi.Render.CurrentActiveShader;

            if (isShadowPass)
            {
                Mat4f.Mul(tmpMvMat, capi.Render.CurrentModelviewMatrix, ModelMat);
                prog.UniformMatrix("modelViewMatrix", tmpMvMat);
            }
            else
            {
                frostAlpha += (targetFrostAlpha - frostAlpha) * dt / 6f;
                float fa = (float)Math.Round(GameMath.Clamp(frostAlpha, 0, 1), 4);

                prog.Uniform("rgbaLightIn", lightrgbs);
                prog.Uniform("extraGlow", entity.Properties.Client.GlowLevel);
                prog.UniformMatrix("modelMatrix", ModelMat);
                prog.UniformMatrix("viewMatrix", capi.Render.CurrentModelviewMatrix);
                prog.Uniform("addRenderFlags", AddRenderFlags);
                prog.Uniform("windWaveIntensity", (float)WindWaveIntensity);
                prog.Uniform("entityId", (int)entity.EntityId);
                prog.Uniform("glitchFlicker", glitchFlicker ? 1 : 0);
                prog.Uniform("frostAlpha", fa);
                prog.Uniform("waterWaveCounter", capi.Render.ShaderUniforms.WaterWaveCounter);

                color.R = (entity.RenderColor >> 16 & 0xff) / 255f;
                color.G = ((entity.RenderColor >> 8) & 0xff) / 255f;
                color.B = ((entity.RenderColor >> 0) & 0xff) / 255f;
                color.A = ((entity.RenderColor >> 24) & 0xff) / 255f;

                prog.Uniform("renderColor", color);

                double stab = entity.WatchedAttributes.GetDouble("temporalStability", 1);
                double plrStab = capi.World.Player.Entity.WatchedAttributes.GetDouble("temporalStability", 1);
                double stabMin = Math.Min(stab, plrStab);

                float strength = (float)(glitchAffected ? Math.Max(0, 1 - 1 / 0.4f * stabMin) : 0);
                prog.Uniform("glitchEffectStrength", strength);
            }

            prog.UBOs["Animation"].Update(entity.AnimManager.Animator.Matrices, 0, entity.AnimManager.Animator.MaxJointId * 16 * 4);

            if (meshRefOpaque != null)
            {
                capi.Render.RenderMultiTextureMesh(meshRefOpaque, "entityTex");
            }
        }

        public override void DoRender2D(float dt)
        {
            if (isSpectator || (debugTagTexture == null && messageTextures == null)) return;
            if ((entity as EntityPlayer)?.ServerControls.Sneak == true && debugTagTexture == null) return;

            IRenderAPI rapi = capi.Render;
            EntityPlayer entityPlayer = capi.World.Player.Entity;
            Vec3d aboveHeadPos = getAboveHeadPosition(entityPlayer);

            Vec3d pos = MatrixToolsd.Project(aboveHeadPos, rapi.PerspectiveProjectionMat, rapi.PerspectiveViewMat, rapi.FrameWidth, rapi.FrameHeight);

            // Z negative seems to indicate that the name tag is behind us \o/
            if (pos.Z < 0) return;

            float scale = 4f / Math.Max(1, (float)pos.Z);

            float cappedScale = Math.Min(1f, scale);
            if (cappedScale > 0.75f) cappedScale = 0.75f + (cappedScale - 0.75f) / 2;

            float offY = 0;

            double dist = entityPlayer.Pos.SquareDistanceTo(entity.Pos);

            if (debugTagTexture != null)
            {
                float posx = (float)pos.X - cappedScale * debugTagTexture.Width / 2;
                float posy = rapi.FrameHeight - (float)pos.Y - (offY + debugTagTexture.Height) * Math.Max(0, cappedScale);

                rapi.Render2DTexture(
                    debugTagTexture.TextureId, posx, posy - offY, cappedScale * debugTagTexture.Width, cappedScale * debugTagTexture.Height, 20
                );
            }

            if (messageTextures != null)
            {
                offY += 0;

                foreach (MessageTexture mt in messageTextures)
                {
                    offY += (mt.tex.Height * cappedScale) + 4;

                    float posx = (float)pos.X - cappedScale * mt.tex.Width / 2;
                    float posy = (float)pos.Y + offY;


                    rapi.Render2DTexture(
                        mt.tex.TextureId, posx, rapi.FrameHeight - posy, cappedScale * mt.tex.Width, cappedScale * mt.tex.Height, 20
                    );
                }
            }
        }

        public virtual Vec3d getAboveHeadPosition(EntityPlayer entityPlayer)
        {
            Vec3d aboveHeadPos;

            var thisMount = (entity as EntityAgent)?.MountedOn;
            var selfMount = entityPlayer.MountedOn;

            // If this entity is mounted on the same mount as the client player, render as offset from mount. Prevents jitter
            if (thisMount?.MountSupplier != null && thisMount.MountSupplier == selfMount?.MountSupplier)
            {
                var mpos = thisMount.SeatPosition.XYZ - selfMount.SeatPosition.XYZ;
                aboveHeadPos = new Vec3d(
                    entityPlayer.CameraPos.X + entityPlayer.LocalEyePos.X,
                    entityPlayer.CameraPos.Y + 0.4 + entityPlayer.LocalEyePos.Y,
                    entityPlayer.CameraPos.Z + entityPlayer.LocalEyePos.Z
                );
                aboveHeadPos.Add(mpos);
            }
            else
            {
                aboveHeadPos = new Vec3d(entity.Pos.X, entity.Pos.InternalY + entity.SelectionBox.Y2 + 0.2, entity.Pos.Z);
            }

            double offX = entity.SelectionBox.X2 - entity.OriginSelectionBox.X2;
            double offZ = entity.SelectionBox.Z2 - entity.OriginSelectionBox.Z2;
            aboveHeadPos.Add(offX, 0, offZ);
            return aboveHeadPos;
        }



        public double stepPitch;
        double prevY;
        double prevYAccum;
        public float xangle = 0, yangle = 0, zangle = 0;
        IMountable ims;

        public void loadModelMatrix(Entity entity, float dt, bool isShadowPass)
        {
            EntityPlayer entityPlayer = capi.World.Player.Entity;

            Mat4f.Identity(ModelMat);

            // If the player is sitting on this entity, simply render it at the offset of the seat to the entity, this prevents jitter.
            IMountableSeat seat;
            if (ims != null && (seat = ims.GetSeatOfMountedEntity(entityPlayer)) != null)
            {
                var offset = seat.SeatPosition.XYZ - seat.MountSupplier.Position.XYZ;
                ModelMat = Mat4f.Translate(ModelMat, ModelMat, -(float)offset.X, -(float)offset.Y, -(float)offset.Z);
            }
            else
            {
                seat = eagent?.MountedOn;
                // If this entity itself is riding something, render it as offset of that entity
                if (/*ims != null && - why is this condition here? It prevents jitter avoidance! */ seat != null)
                {
                    // If both the player and the entity is sitting on the same mount
                    if (entityPlayer.MountedOn != null && entityPlayer.MountedOn.Entity == eagent.MountedOn.Entity)
                    {
                        var selfMountPos = entityPlayer.MountedOn.SeatPosition;
                        Mat4f.Translate(ModelMat, ModelMat, (float)(seat.SeatPosition.X - selfMountPos.X), (float)(seat.SeatPosition.InternalY - selfMountPos.Y), (float)(seat.SeatPosition.Z - selfMountPos.Z));
                    }
                    else
                    {
                        Mat4f.Translate(ModelMat, ModelMat, (float)(seat.SeatPosition.X - entityPlayer.CameraPos.X), (float)(seat.SeatPosition.InternalY - entityPlayer.CameraPos.Y), (float)(seat.SeatPosition.Z - entityPlayer.CameraPos.Z));
                    }
                }
                else
                {
                    Mat4f.Translate(ModelMat, ModelMat, (float)(entity.Pos.X - entityPlayer.CameraPos.X), (float)(entity.Pos.InternalY - entityPlayer.CameraPos.Y), (float)(entity.Pos.Z - entityPlayer.CameraPos.Z));
                }
            }

            float rotX = entity.Properties.Client.Shape?.rotateX ?? 0;
            float rotY = entity.Properties.Client.Shape?.rotateY ?? 0;
            float rotZ = entity.Properties.Client.Shape?.rotateZ ?? 0;

            Mat4f.Translate(ModelMat, ModelMat, 0, entity.SelectionBox.Y2 / 2, 0);

            if (!isShadowPass)
            {
                updateStepPitch(dt);
            }

            double[] quat = Quaterniond.Create();
            float bodyPitch = entity is EntityPlayer ? 0 : entity.Pos.Pitch;
            float yaw = entity.Pos.Yaw + (rotY + 90) * GameMath.DEG2RAD;


            BlockFacing climbonfacing = entity.ClimbingOnFace;

            // To fix climbing locust rotation weirdnes on east and west faces. Brute forced fix. There's probably a correct solution to this.
            bool fuglyHack = entity.Properties.RotateModelOnClimb && entity.ClimbingOnFace?.Axis == EnumAxis.X;
            float sign = -1;


            Quaterniond.RotateX(quat, quat, bodyPitch + rotX * GameMath.DEG2RAD + (fuglyHack ? yaw * sign : 0));
            Quaterniond.RotateY(quat, quat, (fuglyHack ? 0 : yaw));
            Quaterniond.RotateZ(quat, quat, entity.Pos.Roll + stepPitch + rotZ * GameMath.DEG2RAD + (fuglyHack ? GameMath.PIHALF * (climbonfacing == BlockFacing.WEST ? -1 : 1) : 0));

            Quaterniond.RotateX(quat, quat, xangle);
            Quaterniond.RotateY(quat, quat, yangle);
            Quaterniond.RotateZ(quat, quat, zangle);


            float[] qf = new float[quat.Length];
            for (int i = 0; i < quat.Length; i++) qf[i] = (float)quat[i];
            Mat4f.Mul(ModelMat, ModelMat, Mat4f.FromQuat(Mat4f.Create(), qf));
            if (shouldSwivelFromMotion)
            {
                Mat4f.RotateX(ModelMat, ModelMat, nowSwivelRad);
            }

            float scale = entity.Properties.Client.Size;
            Mat4f.Translate(ModelMat, ModelMat, 0, -entity.SelectionBox.Y2 / 2, 0f);
            Mat4f.Scale(ModelMat, ModelMat, new float[] { scale, scale, scale });
            Mat4f.Translate(ModelMat, ModelMat, -0.5f, 0, -0.5f);
        }





        protected void loadModelMatrixForGui(Entity entity, double posX, double posY, double posZ, double yawDelta, float size)
        {
            Mat4f.Identity(ModelMat);
            Mat4f.Translate(ModelMat, ModelMat, (float)posX, (float)posY, (float)posZ);

            Mat4f.Translate(ModelMat, ModelMat, size, 2 * size, 0);

            float rotX = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateX : 0;
            float rotY = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateY : 0;
            float rotZ = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateZ : 0;

            Mat4f.RotateX(ModelMat, ModelMat, GameMath.PI + rotX * GameMath.DEG2RAD);
            Mat4f.RotateY(ModelMat, ModelMat, (float)yawDelta + rotY * GameMath.DEG2RAD);
            Mat4f.RotateZ(ModelMat, ModelMat, rotZ * GameMath.DEG2RAD);

            float scale = entity.Properties.Client.Size * size;
            Mat4f.Scale(ModelMat, ModelMat, new float[] { scale, scale, scale });
            Mat4f.Translate(ModelMat, ModelMat, -0.5f, 0f, -0.5f);
        }



        #region Step pitch and Sideways swivel when changing the movement direction

        float stepingAccum = 0f;
        float fallingAccum = 0f;

        private void updateStepPitch(float dt)
        {
            if (!entity.CanStepPitch)
            {
                stepPitch = 0;
                return;
            }

            double targetPitch = 0;

            if (LastJumpMs > 0)
            {
                targetPitch = 0;
                if (capi.InWorldEllapsedMilliseconds - LastJumpMs > 500 && entity.OnGround) LastJumpMs = -1;
            }
            else
            {
                prevYAccum += dt;
                if (prevYAccum > 1f / 5f)
                {
                    prevYAccum = 0;
                    prevY = entity.Pos.Y;
                }

                if (eagent?.Alive == false)
                {
                    stepPitch = Math.Max(0, stepPitch - 2 * dt);
                }

                if (eagent == null || entity.Properties.CanClimbAnywhere || !eagent.Alive || entity.Attributes.GetInt("dmgkb", 0) != 0 || !entity.Properties.Client.PitchStep) return;


                if (entity.Properties.Habitat == EnumHabitat.Air || eagent.Controls.IsClimbing)
                {
                    stepPitch = GameMath.Clamp(entity.Pos.Y - prevY + 0.1, 0, 0.3) - GameMath.Clamp(prevY - entity.Pos.Y - 0.1, 0, 0.3);
                    return;
                }

                double deltaY = entity.Pos.Y - prevY;

                bool steppingUp = deltaY > 0.02 && !entity.FeetInLiquid && !entity.Swimming && !entity.OnGround;
                bool falling = deltaY < 0 && !entity.OnGround && !entity.FeetInLiquid && !entity.Swimming;


                stepingAccum = Math.Max(0f, stepingAccum - dt);
                fallingAccum = Math.Max(0f, fallingAccum - dt);

                if (steppingUp) stepingAccum = 0.2f;
                if (falling) fallingAccum = 0.2f;

                if (stepingAccum > 0) targetPitch = -0.5;
                else if (fallingAccum > 0) targetPitch = 0.5;
            }

            stepPitch += (targetPitch - stepPitch) * dt * 5;
        }


        public float targetSwivelRad = 0;
        public float nowSwivelRad = 0;
        protected double prevAngleSwing;
        protected double prevPosXSwing;
        protected double prevPosZSwing;

        float swivelaccum = 0f;
        public long LastJumpMs;

        public bool shouldSwivelFromMotion = true;
        public float maxSwivelAngle = GameMath.RAD2DEG * 0.4f;

        protected virtual void determineSidewaysSwivel(float dt)
        {
            if (!shouldSwivelFromMotion)
            {
                if (eagent != null) eagent.sidewaysSwivelAngle = 0;
                return;
            }
            if (!entity.CanSwivel)
            {
                nowSwivelRad = 0;
                targetSwivelRad = 0;
                if (eagent != null) eagent.sidewaysSwivelAngle = 0;
                return;
            }

            swivelaccum += dt;

            if (swivelaccum > 0.1 && entity.CanSwivelNow)
            {
                double dx = entity.Pos.X - prevPosXSwing;
                double dz = entity.Pos.Z - prevPosZSwing;

                double nowAngle = Math.Atan2(dz, dx);
                double speed = Math.Sqrt(dx * dx + dz * dz);
                swivelaccum = 0;

                float anglechange = GameMath.AngleRadDistance((float)nowAngle, (float)prevAngleSwing);

                if (Math.Abs(anglechange) < GameMath.PIHALF)
                {
                    var maxSwivelRad = GameMath.DEG2RAD * maxSwivelAngle;
                    targetSwivelRad = GameMath.Clamp((float)speed * anglechange * 3f, -maxSwivelRad, maxSwivelRad);
                }
                else
                {
                    targetSwivelRad = 0;
                }

                prevAngleSwing = nowAngle;
                prevPosXSwing = entity.Pos.X;
                prevPosZSwing = entity.Pos.Z;
            }


            float diff = GameMath.AngleRadDistance(nowSwivelRad, targetSwivelRad);

            nowSwivelRad += GameMath.Clamp(diff * dt * 2, -0.15f, 0.15f);

            if (eagent != null)
            {
                eagent.sidewaysSwivelAngle = nowSwivelRad;
            }
        }

        #endregion



        public override void Dispose()
        {
            capi.World.UnregisterGameTickListener(listenerId);
            listenerId = 0;

            if (meshRefOpaque != null)
            {
                meshRefOpaque.Dispose();
                meshRefOpaque = null;
            }


            if (debugTagTexture != null)
            {
                debugTagTexture.Dispose();
                debugTagTexture = null;
            }

            capi.Event.ReloadShapes -= entity.MarkShapeModified;

            if (DisplayChatMessages)
            {
                capi.Event.ChatMessage -= OnChatMessage;
            }
        }
    }
}
