using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class MessageTexture
    {
        public LoadedTexture tex;
        public string message;
        public long receivedTime;
    }

    
    public class EntityShapeRenderer : EntityRenderer, ITexPositionSource
    {
        protected LoadedTexture nameTagTexture = null;
        protected int renderRange = 999;
        protected bool showNameTagOnlyWhenTargeted = false;

        protected LoadedTexture debugTagTexture = null;

        protected MeshRef meshRefOpaque;
        protected MeshRef meshRefOit;

        protected Vec4f color = new Vec4f(1, 1, 1, 1);
        protected long lastDebugInfoChangeMs = 0;
        protected bool isSpectator;
        protected IClientPlayer player;
        protected float bodyYawLerped = 0;

        public Vec3f OriginPos = new Vec3f();
        public float[] ModelMat = Mat4f.Create();
        protected float[] tmpMvMat = Mat4f.Create();
        protected Matrixf ItemModelMat = new Matrixf();

        public bool DoRenderHeldItem;
        public bool DisplayChatMessages;

        public int AddRenderFlags;
        public double WindWaveIntensity = 1f;

        protected List<MessageTexture> messageTextures = null;
        protected NameTagRendererDelegate nameTagRenderHandler;

        protected bool hasArmorExtras;
        protected EntityAgent eagent;

        public CompositeShape OverrideCompositeShape;
        public Shape OverrideEntityShape;

        public bool glitchAffected;
        protected IInventory gearInv;

        ITexPositionSource defaultTexSource;


        protected Dictionary<string, CompositeTexture> extraTexturesByTextureName
        {
            get
            {
                return ObjectCacheUtil.GetOrCreate(capi, "entityShapeExtraTexturesByName", () => new Dictionary<string, CompositeTexture>());
            }
        }
        protected Dictionary<AssetLocation, BakedCompositeTexture> extraTextureByLocation
        {
            get
            {
                return ObjectCacheUtil.GetOrCreate(capi, "entityShapeExtraTexturesByLoc", () => new Dictionary<AssetLocation, BakedCompositeTexture>());
            }
        }


        public Size2i AtlasSize { get { return capi.EntityTextureAtlas.Size; } }
        protected TextureAtlasPosition skinTexPos;
        public virtual TextureAtlasPosition this[string textureCode]
        {
            get
            {
                CompositeTexture cpt = null;
                if (extraTexturesByTextureName?.TryGetValue(textureCode, out cpt) == true)
                {
                    return capi.EntityTextureAtlas.Positions[cpt.Baked.TextureSubId];
                }

                return defaultTexSource[textureCode];
            }
        }


        public EntityShapeRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            eagent = entity as EntityAgent;

            DoRenderHeldItem = true;
            DisplayChatMessages = entity is EntityPlayer;
            
            TesselateShape();

            // For players the player data is not there yet, so we load the thing later
            if (!(entity is EntityPlayer))
            {
                nameTagRenderHandler = api.ModLoader.GetModSystem<EntityNameTagRendererRegistry>().GetNameTagRenderer(entity);
            }

            glitchAffected = true;// entity.Properties.Attributes?["glitchAffected"].AsBool(false) ?? false;

            entity.WatchedAttributes.OnModified.Add(new TreeModifiedListener() { path = "nametag", listener = OnNameChanged });
            OnNameChanged();
            api.Event.RegisterGameTickListener(UpdateDebugInfo, 250);
            OnDebugInfoChanged();

            if (DisplayChatMessages)
            {
                messageTextures = new List<MessageTexture>();
                api.Event.ChatMessage += OnChatMessage;
            }
            
            api.Event.ReloadShapes += TesselateShape;
        }


        protected void OnChatMessage(int groupId, string message, EnumChatType chattype, string data)
        {
            if (data != null && data.Contains("from:") && entity.Pos.SquareDistanceTo(capi.World.Player.Entity.Pos.XYZ) < 20*20 && message.Length > 0)
            {
                int entityid = 0;
                string[] parts = data.Split(new char[] { ',' }, 2);
                if (parts.Length < 2) return;

                string[] partone = parts[0].Split(new char[] { ':' }, 2);
                string[] parttwo = parts[1].Split(new char[] { ':' }, 2);
                if (partone[0] != "from") return;

                int.TryParse(partone[1], out entityid);
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
            CompositeShape compositeShape = OverrideCompositeShape != null ? OverrideCompositeShape : entity.Properties.Client.Shape;
            Shape entityShape = OverrideEntityShape != null ? OverrideEntityShape : entity.Properties.Client.LoadedShape;

            if (entityShape == null)
            {
                return;
            }

            if (eagent != null)
            {
                entityShape = addGearToShape(entityShape, compositeShape.Base.ToString());
            }

            defaultTexSource = GetTextureSource();
            MeshData meshdata;

            if (entity.Properties.Client.Shape.VoxelizeTexture)
            {
                int altTexNumber = entity.WatchedAttributes.GetInt("textureIndex", 0);

                TextureAtlasPosition pos = defaultTexSource["all"];
                CompositeTexture[] Alternates = entity.Properties.Client.FirstTexture.Alternates;

                CompositeTexture tex = altTexNumber == 0 ? entity.Properties.Client.FirstTexture : Alternates[altTexNumber % Alternates.Length];
                meshdata = capi.Tesselator.VoxelizeTexture(tex, capi.EntityTextureAtlas.Size, pos);
                for (int i = 0; i < meshdata.xyz.Length; i+=3)
                {
                    meshdata.xyz[i] -= 0.125f;
                    meshdata.xyz[i + 1] -= 0.5f;
                    meshdata.xyz[i + 2] += 0.125f / 2;
                }
            } else
            {
                try
                {
                    capi.Tesselator.TesselateShapeWithJointIds("entity", entityShape, out meshdata, this, new Vec3f(), compositeShape.QuantityElements, compositeShape.SelectiveElements);

                    meshdata.Translate(compositeShape.offsetX, compositeShape.offsetY, compositeShape.offsetZ);

                } catch (Exception e)
                {
                    capi.World.Logger.Fatal("Failed tesselating entity {0} with id {1}. Entity will probably be invisible!. The teselator threw {2}", entity.Code, entity.EntityId, e);
                    return;
                }
            }

            meshdata.Rgba2 = null;

            if (meshRefOpaque != null)
            {
                capi.Render.DeleteMesh(meshRefOpaque);
                meshRefOpaque = null;
            }
            if (meshRefOit != null)
            {
                capi.Render.DeleteMesh(meshRefOit);
                meshRefOit = null;
            }

            MeshData opaqueMesh = meshdata.Clone().Clear();
            MeshData oitMesh = meshdata.Clone().Clear();

            opaqueMesh.AddMeshData(meshdata, EnumChunkRenderPass.Opaque);
            oitMesh.AddMeshData(meshdata, EnumChunkRenderPass.Transparent);

            if (opaqueMesh.VerticesCount > 0)
            {
                meshRefOpaque = capi.Render.UploadMesh(opaqueMesh);
            }

            if (oitMesh.VerticesCount > 0)
            {
                meshRefOit = capi.Render.UploadMesh(oitMesh);
            }
            
        }

        protected Shape addGearToShape(Shape entityShape, string shapePathForLogging)
        {
            IInventory inv = eagent.GearInventory;
            hasArmorExtras = false;

            if (inv == null) return entityShape;

            foreach (var slot in inv)
            {
                entityShape = addGearToShape(slot, entityShape, shapePathForLogging);
            }

            if (entity is EntityPlayer eplr) {
                IInventory backPackInv = eplr.Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);

                Dictionary<string, ItemSlot> uniqueGear = new Dictionary<string, ItemSlot>();
                for(int i = 0; backPackInv != null && i < 4; i++)
                {
                    ItemSlot slot = backPackInv[i];
                    if (slot.Empty) continue;
                    uniqueGear["" + slot.Itemstack.Class + slot.Itemstack.Collectible.Id] = slot;
                }

                foreach (var val in uniqueGear)
                {
                    entityShape = addGearToShape(val.Value, entityShape, shapePathForLogging);
                }
            }

            return entityShape;
        }

        private Shape addGearToShape(ItemSlot slot, Shape entityShape, string shapePathForLogging)
        {
            if (slot.Empty) return entityShape;
            ItemStack stack = slot.Itemstack;
            JsonObject attrObj = stack.Collectible.Attributes;
            if (attrObj?["wearableAttachment"].Exists != true) return entityShape;

            if (!hasArmorExtras)
            {
                // So we don't affect other players models
                Shape newShape = new Shape()
                {
                    Elements = entityShape.CloneElements(),
                    Animations = entityShape.Animations,
                    AnimationsByCrc32 = entityShape.AnimationsByCrc32,
                    AttachmentPointsByCode = entityShape.AttachmentPointsByCode,
                    JointsById = entityShape.JointsById,
                    TextureWidth = entityShape.TextureWidth,
                    TextureHeight = entityShape.TextureHeight,
                    TextureSizes = entityShape.TextureSizes,
                    Textures = entityShape.Textures,
                };

                newShape.ResolveAndLoadJoints("head");

                entityShape = newShape;
            }

            hasArmorExtras = true;

            CompositeShape compArmorShape = !attrObj["attachShape"].Exists ? (stack.Class == EnumItemClass.Item ? stack.Item.Shape : stack.Block.Shape) : attrObj["attachShape"].AsObject<CompositeShape>(null, stack.Collectible.Code.Domain);

            AssetLocation shapePath = shapePath = compArmorShape.Base.CopyWithPath("shapes/" + compArmorShape.Base.Path + ".json");

            string[] disableElements = attrObj["disableElements"].AsArray<string>(null);
            if (disableElements != null)
            {
                foreach (var val in disableElements)
                {
                    entityShape.RemoveElementByName(val);
                }
            }

            IAsset asset = capi.Assets.TryGet(shapePath);

            if (asset == null)
            {
                capi.World.Logger.Warning("Entity armor shape {0} defined in {1} {2} not found, was supposed to be at {3}. Armor piece will be invisible.", compArmorShape.Base, stack.Class, stack.Collectible.Code, shapePath);
                return null;
            }

            Shape armorShape;

            try
            {
                armorShape = asset.ToObject<Shape>();
            }
            catch (Exception e)
            {
                capi.World.Logger.Warning("Exception thrown when trying to load entity armor shape {0} defined in {1} {2}. Armor piece will be invisible. Exception: {3}", compArmorShape.Base, slot.Itemstack.Class, slot.Itemstack.Collectible.Code, e);
                return null;
            }

            bool added = false;
            foreach (var val in armorShape.Elements)
            {
                ShapeElement elem;

                if (val.StepParentName != null)
                {
                    elem = entityShape.GetElementByName(val.StepParentName, StringComparison.InvariantCultureIgnoreCase);
                    if (elem == null)
                    {
                        capi.World.Logger.Warning("Entity armor shape {0} defined in {1} {2} requires step parent element with name {3}, but no such element was found in shape {3}. Will not be visible.", compArmorShape.Base, slot.Itemstack.Class, slot.Itemstack.Collectible.Code, val.StepParentName, shapePathForLogging);
                        continue;
                    }
                }
                else
                {
                    capi.World.Logger.Warning("Entity armor shape element {0} in shape {1} defined in {2} {3} did not define a step parent element. Will not be visible.", val.Name, compArmorShape.Base, slot.Itemstack.Class, slot.Itemstack.Collectible.Code);
                    continue;
                }

                if (elem.Children == null)
                {
                    elem.Children = new ShapeElement[] { val };
                }
                else
                {
                    elem.Children = elem.Children.Append(val);
                }

                val.SetJointIdRecursive(elem.JointId);
                val.WalkRecursive((el) =>
                {
                    foreach (var face in el.Faces)
                    {
                        face.Value.Texture = "#" + stack.Collectible.Code + "-" + face.Value.Texture.TrimStart('#');
                    }
                });

                added = true;
            }

            if (added && armorShape.Textures != null)
            {
                Dictionary<string, AssetLocation> newdict = new Dictionary<string, AssetLocation>();
                foreach (var val in armorShape.Textures)
                {
                    newdict[stack.Collectible.Code + "-" + val.Key] = val.Value;
                }

                // Item overrides
                var collDict = stack.Class == EnumItemClass.Block ? stack.Block.Textures : stack.Item.Textures;
                foreach (var val in collDict)
                {
                    newdict[stack.Collectible.Code + "-" + val.Key] = val.Value.Base;
                }

                armorShape.Textures = newdict;

                foreach (var val in armorShape.Textures)
                {
                    CompositeTexture ctex = new CompositeTexture() { Base = val.Value };
                    //ctex.Bake(capi.Assets);
                    /*if (ctex.Baked.BakedVariants != null)
                    {
                        for (int i = 0; i < ctex.Baked.BakedVariants.Length; i++)
                        {
                            AddTextureLocation(new AssetLocationAndSource(ctex.Baked.BakedVariants[i].BakedName, "Shape file " + clientConf.Shape.Base));
                        }
                        continue;
                    }*/

                    entityShape.TextureSizes[val.Key] = new int[] { armorShape.TextureWidth, armorShape.TextureHeight };

                    AssetLocation armorTexLoc = val.Value;
                    var texturesByLoc = extraTextureByLocation;
                    var texturesByName = extraTexturesByTextureName;
                    BakedCompositeTexture bakedCtex;

                    if (!texturesByLoc.TryGetValue(armorTexLoc, out bakedCtex))
                    {
                        int textureSubId = 0;
                        TextureAtlasPosition texpos;

                        IAsset texAsset = capi.Assets.TryGet(val.Value.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                        if (texAsset != null)
                        {
                            BitmapRef bmp = texAsset.ToBitmap(capi);
                            capi.EntityTextureAtlas.InsertTexture(bmp, out textureSubId, out texpos);
                        }
                        else
                        {
                            capi.World.Logger.Warning("Entity armor shape {0} defined texture {1}, not no such texture found.", shapePath, val.Value);
                        }

                        ctex.Baked = new BakedCompositeTexture() { BakedName = val.Value, TextureSubId = textureSubId };

                        texturesByName[val.Key] = ctex;
                        texturesByLoc[armorTexLoc] = ctex.Baked;
                    }
                    else
                    {
                        ctex.Baked = bakedCtex;
                        texturesByName[val.Key] = ctex;
                    }
                }

                foreach (var val in armorShape.TextureSizes)
                {
                    entityShape.TextureSizes[val.Key] = val.Value;
                }
            }

            return entityShape;
        }

        protected virtual ITexPositionSource GetTextureSource()
        {
            int altTexNumber = entity.WatchedAttributes.GetInt("textureIndex", 0);
            return capi.Tesselator.GetTextureSource(entity, extraTexturesByTextureName, altTexNumber);
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
                text.AppendLine(val.Key +": " + val.Value.ToString());
            }

            debugTagTexture = capi.Gui.TextTexture.GenUnscaledTextTexture(
                text.ToString(), 
                new CairoFont(20, GuiStyle.StandardFontName, ColorUtil.WhiteArgbDouble), 
                new TextBackground() { FillColor = GuiStyle.DialogDefaultBgColor, Padding = 3, Radius = GuiStyle.ElementBGRadius }
            );

            lastDebugInfoChangeMs = entity.World.ElapsedMilliseconds;
        }

        protected void OnNameChanged()
        {
            if (nameTagRenderHandler == null) return;
            if (nameTagTexture != null)
            {
                nameTagTexture.Dispose();
                nameTagTexture = null;
            }

            int? range = entity.GetBehavior<EntityBehaviorNameTag>()?.RenderRange;
            renderRange = range == null ? 999 : (int)range;
            showNameTagOnlyWhenTargeted = entity.GetBehavior<EntityBehaviorNameTag>()?.ShowOnlyWhenTargeted == true;
            nameTagTexture = nameTagRenderHandler.Invoke(capi, entity);
        }

        public string GetNameTagName()
        {
            EntityBehaviorNameTag behavior = entity.GetBehavior<EntityBehaviorNameTag>();
            return behavior?.DisplayName;
        }


        public override void BeforeRender(float dt)
        {
            if (meshRefOpaque == null && meshRefOit == null) return;

            if (gearInv == null && eagent?.GearInventory != null)
            {
                registerSlotModified();
            }


            if (capi.IsGamePaused) return;

            if (player == null && entity is EntityPlayer)
            {
                player = capi.World.PlayerByUid((entity as EntityPlayer).PlayerUID) as IClientPlayer;

                nameTagRenderHandler = capi.ModLoader.GetModSystem<EntityNameTagRendererRegistry>().GetNameTagRenderer(entity);
                OnNameChanged();
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

                    /*if (messageTextures.Count > 0)
                    {
                        tex = messageTextures[messageTextures.Count - 1];
                        long msvisible = tex.receivedTime + 3500 + 100 * (tex.message.Length - 10) - capi.World.ElapsedMilliseconds;
                        tex.receivedTime += Math.Max(0, 1000 - msvisible);
                    }*/
                }
            }
        }


        

        public override void DoRender3DOpaque(float dt, bool isShadowPass)
        {
            if (isSpectator) return;

            if (player != null)
            {
                bool isSelf = capi.World.Player.Entity.EntityId == entity.EntityId;
                loadModelMatrixForPlayer(entity, isSelf, dt);
                if (isSelf) OriginPos.Set(0, 0, 0);
            }
            else
            {
                loadModelMatrix(entity, dt, isShadowPass);
                Vec3d camPos = capi.World.Player.Entity.CameraPos;
                OriginPos.Set((float)(entity.Pos.X - camPos.X), (float)(entity.Pos.Y - camPos.Y), (float)(entity.Pos.Z - camPos.Z));
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
        }


        public override void DoRender3DAfterOIT(float dt, bool isShadowPass)
        {
            
        }


        float accum = 0;
        
        protected void RenderHeldItem(float dt, bool isShadowPass, bool right)
        {
            IRenderAPI rapi = capi.Render;
            ItemSlot slot = right ? eagent?.RightHandItemSlot : eagent?.LeftHandItemSlot;
            ItemStack stack = slot?.Itemstack;

            AttachmentPointAndPose apap = entity.AnimManager.Animator.GetAttachmentPointPose(right ? "RightHand" : "LeftHand");
            if (apap == null || stack == null) return;
            
            AttachmentPoint ap = apap.AttachPoint;
            ItemRenderInfo renderInfo = rapi.GetItemStackRenderInfo(slot, EnumItemRenderTarget.HandTp);
            IStandardShaderProgram prog = null;

            if (renderInfo?.Transform == null) return; // Happens with unknown items/blocks
            
            ItemModelMat
                .Set(ModelMat)
                .Mul(apap.AnimModelMatrix)
                .Translate(renderInfo.Transform.Origin.X, renderInfo.Transform.Origin.Y, renderInfo.Transform.Origin.Z)
                .Scale(renderInfo.Transform.ScaleXYZ.X, renderInfo.Transform.ScaleXYZ.Y, renderInfo.Transform.ScaleXYZ.Z)
                .Translate(ap.PosX / 16f + renderInfo.Transform.Translation.X, ap.PosY / 16f + renderInfo.Transform.Translation.Y, ap.PosZ / 16f + renderInfo.Transform.Translation.Z)
                .RotateX((float)(ap.RotationX + renderInfo.Transform.Rotation.X) * GameMath.DEG2RAD)
                .RotateY((float)(ap.RotationY + renderInfo.Transform.Rotation.Y) * GameMath.DEG2RAD)
                .RotateZ((float)(ap.RotationZ + renderInfo.Transform.Rotation.Z) * GameMath.DEG2RAD)
                .Translate(-(renderInfo.Transform.Origin.X), -(renderInfo.Transform.Origin.Y), -(renderInfo.Transform.Origin.Z))
            ;


            if (isShadowPass)
            {
                rapi.CurrentActiveShader.BindTexture2D("tex2d", renderInfo.TextureId, 0);
                float[] mvpMat = Mat4f.Mul(ItemModelMat.Values, capi.Render.CurrentModelviewMatrix, ItemModelMat.Values);
                Mat4f.Mul(mvpMat, capi.Render.CurrentProjectionMatrix, mvpMat);

                capi.Render.CurrentActiveShader.UniformMatrix("mvpMatrix", mvpMat);
                capi.Render.CurrentActiveShader.Uniform("origin", new Vec3f());
            }
            else
            {
                prog = rapi.StandardShader;
                prog.Use();
                prog.DontWarpVertices = 0;
                prog.AddRenderFlags = 0;
                prog.NormalShaded = 1;
                prog.Tex2D = renderInfo.TextureId;
                prog.RgbaTint = ColorUtil.WhiteArgbVec;

                prog.OverlayOpacity = renderInfo.OverlayOpacity;
                if (renderInfo.OverlayTexture != null && renderInfo.OverlayOpacity > 0)
                {
                    prog.Tex2dOverlay2D = renderInfo.OverlayTexture.TextureId;
                    prog.OverlayTextureSize = new Vec2f(renderInfo.OverlayTexture.Width, renderInfo.OverlayTexture.Height);
                    prog.BaseTextureSize = new Vec2f(renderInfo.TextureSize.Width, renderInfo.TextureSize.Height);
                    TextureAtlasPosition texPos = rapi.GetTextureAtlasPosition(stack);
                    prog.BaseUvOrigin = new Vec2f(texPos.x1, texPos.y1);
                }

                
                Vec4f lightrgbs = capi.World.BlockAccessor.GetLightRGBs((int)(entity.Pos.X + entity.CollisionBox.X1 - entity.OriginCollisionBox.X1), (int)entity.Pos.Y, (int)(entity.Pos.Z + entity.CollisionBox.Z1 - entity.OriginCollisionBox.Z1));
                int temp = (int)stack.Collectible.GetTemperature(capi.World, stack);
                float[] glowColor = ColorUtil.GetIncandescenceColorAsColor4f(temp);
                lightrgbs[0] += glowColor[0];
                lightrgbs[1] += glowColor[1];
                lightrgbs[2] += glowColor[2];

                prog.ExtraGlow = GameMath.Clamp((temp - 500) / 3, 0, 255);
                prog.RgbaAmbientIn = rapi.AmbientColor;
                prog.RgbaLightIn = lightrgbs;
                prog.RgbaBlockIn = ColorUtil.WhiteArgbVec;
                prog.RgbaFogIn = rapi.FogColor;
                prog.FogMinIn = rapi.FogMin;
                prog.FogDensityIn = rapi.FogDensity;
                prog.NormalShaded = renderInfo.NormalShaded ? 1 : 0;

                prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
                prog.ViewMatrix = rapi.CameraMatrixOriginf;
                prog.ModelMatrix = ItemModelMat.Values;
            }

            
            if (!renderInfo.CullFaces)
            {
                rapi.GlDisableCullFace();
            }

            rapi.RenderMesh(renderInfo.ModelRef);

            if (!renderInfo.CullFaces)
            {
                rapi.GlEnableCullFace();
            }


            if (!isShadowPass)
            {
                prog.Stop();

                AdvancedParticleProperties[] ParticleProperties = stack.Block?.ParticleProperties;

                if (stack.Block != null && !capi.IsGamePaused)
                {
                    
                    Vec4f pos = ItemModelMat.TransformVector(new Vec4f(stack.Block.TopMiddlePos.X, stack.Block.TopMiddlePos.Y, stack.Block.TopMiddlePos.Z, 1));
                    EntityPlayer entityPlayer = capi.World.Player.Entity;
                    accum += dt;
                    if (ParticleProperties != null && ParticleProperties.Length > 0 && accum > 0.025f)
                    {
                        accum = accum % 0.025f;

                        for (int i = 0; i < ParticleProperties.Length; i++)
                        {
                            AdvancedParticleProperties bps = ParticleProperties[i];
                            bps.basePos.X = pos.X + entity.Pos.X + -(entity.Pos.X - entityPlayer.CameraPos.X);
                            bps.basePos.Y = pos.Y + entity.Pos.Y + -(entity.Pos.Y - entityPlayer.CameraPos.Y);
                            bps.basePos.Z = pos.Z + entity.Pos.Z + -(entity.Pos.Z - entityPlayer.CameraPos.Z);

                            eagent.World.SpawnParticles(bps);
                        }
                    }
                }

            }

        }

        public override void PrepareForGuiRender(float dt, double posX, double posY, double posZ, float yawDelta, float size, out MeshRef meshRef, out float[] modelviewMatrix)
        {
            if (gearInv == null && eagent?.GearInventory != null)
            {
                registerSlotModified();
            }

            loadModelMatrixForGui(entity, posX, posY, posZ, yawDelta, size);
            modelviewMatrix = ModelMat;
            meshRef = this.meshRefOpaque;
        }

        void registerSlotModified()
        {
            eagent.GearInventory.SlotModified += gearSlotModified;
            gearInv = eagent.GearInventory;

            if (entity is EntityPlayer eplr)
            {
                IInventory inv = eplr.Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
                if (inv != null) inv.SlotModified += backPackSlotModified;
            }

            TesselateShape();
        }

        protected void backPackSlotModified(int slotId)
        {
            TesselateShape();
        }

        protected void gearSlotModified(int slotid)
        {
            if (slotid >= 12)
            {
                TesselateShape();
            }
            else
            {
                reloadSkin();
            }
        }

        public virtual void reloadSkin()
        {

        }


        public override void DoRender3DOpaqueBatched(float dt, bool isShadowPass)
        {
            if (isSpectator || (meshRefOpaque == null && meshRefOit == null)) return;

            if (isShadowPass)
            {
                Mat4f.Mul(tmpMvMat, capi.Render.CurrentModelviewMatrix, ModelMat);
                capi.Render.CurrentActiveShader.UniformMatrix("modelViewMatrix", tmpMvMat);
            }
            else
            {
                Vec4f lightrgbs = capi.World.BlockAccessor.GetLightRGBs((int)(entity.Pos.X + entity.CollisionBox.X1 - entity.OriginCollisionBox.X1), (int)entity.Pos.Y, (int)(entity.Pos.Z + entity.CollisionBox.Z1 - entity.OriginCollisionBox.Z1));

                capi.Render.CurrentActiveShader.Uniform("rgbaLightIn", lightrgbs);
                capi.Render.CurrentActiveShader.Uniform("extraGlow", entity.Properties.Client.GlowLevel);
                capi.Render.CurrentActiveShader.UniformMatrix("modelMatrix", ModelMat);
                capi.Render.CurrentActiveShader.UniformMatrix("viewMatrix", capi.Render.CurrentModelviewMatrix);
                capi.Render.CurrentActiveShader.Uniform("addRenderFlags", AddRenderFlags);
                capi.Render.CurrentActiveShader.Uniform("windWaveIntensity", (float)WindWaveIntensity);

                color[0] = (entity.RenderColor >> 16 & 0xff) / 255f;
                color[1] = ((entity.RenderColor >> 8) & 0xff) / 255f;
                color[2] = ((entity.RenderColor >> 0) & 0xff) / 255f;
                color[3] = ((entity.RenderColor >> 24) & 0xff) / 255f;

                capi.Render.CurrentActiveShader.Uniform("renderColor", color);

                double stab = entity.WatchedAttributes.GetDouble("temporalStability", 1);
                double plrStab = capi.World.Player.Entity.WatchedAttributes.GetDouble("temporalStability", 1);
                double stabMin = Math.Min(stab, plrStab);

                float strength = (float)(glitchAffected ? Math.Max(0, 1 - 1 / 0.4f * stabMin) : 0);
                capi.Render.CurrentActiveShader.Uniform("glitchEffectStrength", strength);
            }


            capi.Render.CurrentActiveShader.UniformMatrices(
                "elementTransforms", 
                GlobalConstants.MaxAnimatedElements, 
                entity.AnimManager.Animator.Matrices
            );

            if (meshRefOpaque != null)
            {
                capi.Render.RenderMesh(meshRefOpaque);
            }

            if (meshRefOit != null)
            {
                capi.Render.RenderMesh(meshRefOit);
            }
        }


        public override void DoRender3DOITBatched(float dt)
        {
            /*if (isSpectator || meshRefOit == null) return;

            Vec4f lightrgbs = capi.World.BlockAccessor.GetLightRGBs((int)entity.Pos.X, (int)entity.Pos.Y, (int)entity.Pos.Z);
            capi.Render.CurrentActiveShader.Uniform("rgbaLightIn", lightrgbs);
            //capi.Render.CurrentActiveShader.Uniform("extraGlow", entity.Properties.Client.GlowLevel);
            capi.Render.CurrentActiveShader.UniformMatrix("modelMatrix", ModelMat);
            capi.Render.CurrentActiveShader.UniformMatrix("viewMatrix", capi.Render.CurrentModelviewMatrix);
            capi.Render.CurrentActiveShader.Uniform("addRenderFlags", AddRenderFlags);
            capi.Render.CurrentActiveShader.Uniform("windWaveIntensity", (float)WindWaveIntensity);

            color[0] = (entity.RenderColor >> 16 & 0xff) / 255f;
            color[1] = ((entity.RenderColor >> 8) & 0xff) / 255f;
            color[2] = ((entity.RenderColor >> 0) & 0xff) / 255f;
            color[3] = ((entity.RenderColor >> 24) & 0xff) / 255f;

            capi.Render.CurrentActiveShader.Uniform("renderColor", color);
            capi.Render.CurrentActiveShader.UniformMatrices(
                "elementTransforms",
                GlobalConstants.MaxAnimatedElements,
                entity.AnimManager.Animator.Matrices
            );

            capi.Render.RenderMesh(meshRefOit);*/
        }


        public override void DoRender2D(float dt)
        {
            if (isSpectator || (nameTagTexture == null && debugTagTexture == null)) return;
            if ((entity as EntityPlayer)?.ServerControls.Sneak == true) return;

            IRenderAPI rapi = capi.Render;
            EntityPlayer entityPlayer = capi.World.Player.Entity;
            Vec3d aboveHeadPos;

            if (capi.World.Player.Entity.EntityId == entity.EntityId) {
                if (rapi.CameraType == EnumCameraMode.FirstPerson) return;
                aboveHeadPos = new Vec3d(entityPlayer.CameraPos.X, entityPlayer.CameraPos.Y + entity.CollisionBox.Y2 + 0.2, entityPlayer.CameraPos.Z);
            } else
            {
                aboveHeadPos = new Vec3d(entity.Pos.X, entity.Pos.Y + entity.CollisionBox.Y2 + 0.2, entity.Pos.Z);
            }

            double offX = entity.CollisionBox.X2 - entity.OriginCollisionBox.X2;
            double offZ = entity.CollisionBox.Z2 - entity.OriginCollisionBox.Z2;
            aboveHeadPos.Add(offX, 0, offZ);


            Vec3d pos = MatrixToolsd.Project(aboveHeadPos, rapi.PerspectiveProjectionMat, rapi.PerspectiveViewMat, rapi.FrameWidth, rapi.FrameHeight);

            // Z negative seems to indicate that the name tag is behind us \o/
            if (pos.Z < 0) return;

            float scale = 4f / Math.Max(1, (float)pos.Z);

            float cappedScale = Math.Min(1f, scale);
            if (cappedScale > 0.75f) cappedScale = 0.75f + (cappedScale - 0.75f) / 2;

            float offY = 0;

            double dist = entityPlayer.Pos.SquareDistanceTo(entity.Pos);
            if (nameTagTexture != null && (!showNameTagOnlyWhenTargeted || capi.World.Player.CurrentEntitySelection?.Entity == entity) && renderRange * renderRange > dist)
            {
                float posx = (float)pos.X - cappedScale * nameTagTexture.Width / 2;
                float posy = rapi.FrameHeight - (float)pos.Y - (nameTagTexture.Height * Math.Max(0, cappedScale));

                rapi.Render2DTexture(
                    nameTagTexture.TextureId, posx, posy, cappedScale * nameTagTexture.Width, cappedScale * nameTagTexture.Height, 20
                );

                offY += nameTagTexture.Height;
            }

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
                    // - cappedScale * mt.tex.height

                    
                }
            }
        }

        public override void Dispose()
        {
            if (meshRefOpaque != null)
            {
                meshRefOpaque.Dispose();
                meshRefOpaque = null;
            }

            if (meshRefOit != null)
            {
                meshRefOit.Dispose();
                meshRefOit = null;
            }

            if (nameTagTexture != null)
            {
                nameTagTexture.Dispose();
                nameTagTexture = null;
            }

            if (debugTagTexture != null)
            {
                debugTagTexture.Dispose();
                debugTagTexture = null;
            }

            capi.Event.ReloadShapes -= TesselateShape;

            if (DisplayChatMessages)
            {
                capi.Event.ChatMessage -= OnChatMessage;
            }


            if (eagent?.GearInventory != null)
            {
                eagent.GearInventory.SlotModified -= gearSlotModified;
            }

            if (entity is EntityPlayer eplr)
            {
                IInventory inv = eplr.Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
                if (inv != null) inv.SlotModified -= backPackSlotModified;
            }

            }


        double stepPitch;

        public void loadModelMatrix(Entity entity, float dt, bool isShadowPass)
        {
            EntityPlayer entityPlayer = capi.World.Player.Entity;

            Mat4f.Identity(ModelMat);            
            Mat4f.Translate(ModelMat, ModelMat, (float)(entity.Pos.X - entityPlayer.CameraPos.X), (float)(entity.Pos.Y - entityPlayer.CameraPos.Y), (float)(entity.Pos.Z - entityPlayer.CameraPos.Z));

            float rotX = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateX : 0;
            float rotY = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateY : 0;
            float rotZ = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateZ : 0;

            Mat4f.Translate(ModelMat, ModelMat, 0, entity.CollisionBox.Y2 / 2, 0);
            

            // Some weird quick random hack to make creatures rotate their bodies up/down when stepping up stuff or falling down
            if (eagent != null && !entity.Properties.CanClimbAnywhere && !isShadowPass)
            {
                if (entity.Properties.Habitat != EnumHabitat.Air && entity.Alive)
                {
                    if (entity.ServerPos.Y > entity.Pos.Y + 0.04 && !eagent.Controls.IsClimbing && !entity.FeetInLiquid && !entity.Swimming)
                    {
                        stepPitch = Math.Max(-0.5, stepPitch - 3 * dt);
                    }
                    else
                    {
                        if (stepPitch < 0)
                        {
                            stepPitch = Math.Min(0, stepPitch + 3 * dt);
                        }
                        else
                        {
                            if (!entity.OnGround && !entity.FeetInLiquid && !entity.Swimming)
                            {
                                stepPitch = Math.Min(0.5, stepPitch + 4.5f * dt);
                            }
                            else
                            {
                                stepPitch = Math.Max(0, stepPitch - 3 * dt);
                            }
                        }
                    }
                } else
                {
                    stepPitch = GameMath.Clamp(entity.Pos.Y - entity.ServerPos.Y + 0.1, 0, 0.3) - GameMath.Clamp(entity.ServerPos.Y - entity.Pos.Y - 0.1, 0, 0.3);
                }
            }
            

            double[] quat = Quaterniond.Create();

            float bodyPitch = entity is EntityPlayer ? 0 : entity.Pos.Pitch;

            float yaw = entity.Pos.Yaw + (rotY + 90) * GameMath.DEG2RAD;
            
            BlockFacing climbonfacing = entity.ClimbingOnFace;

            // To fix climbing locust rotation weirdnes on east and west faces. Brute forced fix. There's probably a correct solution to this.
            bool fuglyHack = (entity as EntityAgent)?.Controls.IsClimbing == true && entity.ClimbingOnFace?.Axis == EnumAxis.X;

            Quaterniond.RotateX(quat, quat, bodyPitch + rotX * GameMath.DEG2RAD + (fuglyHack ? yaw : 0));
            Quaterniond.RotateY(quat, quat, fuglyHack ? 0 : yaw);
            Quaterniond.RotateZ(quat, quat, entity.Pos.Roll + stepPitch + rotZ * GameMath.DEG2RAD + (fuglyHack ? GameMath.PIHALF * (climbonfacing == BlockFacing.WEST ? -1 : 1) : 0));
            
            float[] qf = new float[quat.Length];
            for (int i = 0; i < quat.Length; i++) qf[i] = (float)quat[i];
            Mat4f.Mul(ModelMat, ModelMat, Mat4f.FromQuat(Mat4f.Create(), qf));
            
            float scale = entity.Properties.Client.Size;
            Mat4f.Scale(ModelMat, ModelMat, new float[] { scale, scale, scale });
            Mat4f.Translate(ModelMat, ModelMat, -0.5f, -entity.CollisionBox.Y2 / 2, -0.5f);
        }

        

        public void loadModelMatrixForPlayer(Entity entity, bool isSelf, float dt)
        {
            EntityPlayer entityPlayer = capi.World.Player.Entity;
            EntityPlayer eplr = entity as EntityPlayer;

            Mat4f.Identity(ModelMat);

            if (!isSelf)
            {
                Mat4f.Translate(ModelMat, ModelMat, (float)(entity.Pos.X - entityPlayer.CameraPos.X), (float)(entity.Pos.Y - entityPlayer.CameraPos.Y), (float)(entity.Pos.Z - entityPlayer.CameraPos.Z));
            }
            float rotX = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateX : 0;
            float rotY = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateY : 0;
            float rotZ = entity.Properties.Client.Shape != null ? entity.Properties.Client.Shape.rotateZ : 0;
            float bodyYaw = 0;

            if (eagent != null)
            {
                float yawDist = GameMath.AngleRadDistance(bodyYawLerped, eagent.BodyYaw);
                bodyYawLerped += GameMath.Clamp(yawDist, -dt * 8, dt * 8);
                bodyYaw = bodyYawLerped;
            }


            float bodyPitch = eplr == null ? 0 : eplr.WalkPitch;


            Mat4f.RotateX(ModelMat, ModelMat, entity.Pos.Roll + rotX * GameMath.DEG2RAD);
            Mat4f.RotateY(ModelMat, ModelMat, bodyYaw + (180 + rotY) * GameMath.DEG2RAD);
            Mat4f.RotateZ(ModelMat, ModelMat, bodyPitch + rotZ * GameMath.DEG2RAD);

            //float str = (float)entity.Pos.Motion.Length();
            //Mat4f.RotateX(ModelMat, ModelMat, eplr.Controls.Left ? str : -str);

            float scale = entity.Properties.Client.Size;
            Mat4f.Scale(ModelMat, ModelMat, new float[] { scale, scale, scale });
            Mat4f.Translate(ModelMat, ModelMat, -0.5f, 0, -0.5f);
        }



        protected void loadModelMatrixForGui(Entity entity, double posX, double posY, double posZ, double yawDelta, float size)
        {
            EntityPlayer entityPlayer = capi.World.Player.Entity;

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

    }
}
