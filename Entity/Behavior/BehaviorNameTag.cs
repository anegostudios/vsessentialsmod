using System;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Displays name above entity.
    /// <br/>Uses the "nametag" code
    /// </summary>
    /// <example><code lang="json">
    ///"behaviors": [
    /// {
    ///     "code": "nametag",
    ///     "showtagonlywhentargeted": true,
    ///     "selectFromRandomName": ["Yern", "Gild", "Hans", "Hector", "McCrae", "Yoskolo", "Grit", "Bounder"]
    /// },
    ///],
    /// </code></example>
    [DocumentAsJson]
    [AddDocumentationProperty("selectFromRandomName", "Randomly choose and set the entity's display name to one of the specified names", "System.Single[]", "Recommended", "", false)]
    public class EntityBehaviorNameTag : EntityBehavior, IRenderer
    {
        protected LoadedTexture? nameTagTexture = null;
        [Obsolete("Use the property instead")]
        protected bool showNameTagOnlyWhenTargeted = false;
        protected NameTagRendererDelegate? nameTagRenderHandler;
        bool nameTagTextureDirty = true;
        ICoreClientAPI? capi;
        protected int renderRange = 999;
        IPlayer? player;

        public string? DisplayName
        {
            get {
                if (capi != null && TriggeredNameReveal && !IsNameRevealedFor(capi.World.Player.PlayerUID)) return UnrevealedDisplayName;
                return GetName(entity); 
            }
        }

        public static string? GetName(Entity entity)
        {
            return entity.WatchedAttributes.GetTreeAttribute("nametag")?.GetString("name");
        }

        /// <summary>
        /// The name that is shown when the real name is not yet revealed. Only used if <see cref="TriggeredNameReveal"/> is true.
        /// </summary>
        [DocumentAsJson("Optional", "nametag-default-unrevealedname")]
        public string? UnrevealedDisplayName { get; set; }

        /// <summary>
        /// Set to false to continue showing the default name (e.g. Bighorn ewe) elsewhere
        /// </summary>
        [DocumentAsJson("Optional", "true")]
        public bool ReplaceDefaultName { get; set; } = true;

        /// <summary>
        /// <!--<jsonalias>ShowTagOnlyWhenTargeted</jsonalias>-->
        /// Whether or not to show the nametag constantly or only when being looked at.
        /// </summary>
        [DocumentAsJson("Optional", "false")]
        public bool ShowOnlyWhenTargeted
        {
            get { return entity.WatchedAttributes.GetTreeAttribute("nametag")?.GetBool("showtagonlywhentargeted") == true; }
            set { entity.WatchedAttributes.GetTreeAttribute("nametag")?.SetBool("showtagonlywhentargeted", value); }
        }

        /// <summary>
        /// Whether the real name on entity is only revealed after certain conditions are met
        /// </summary>
        [DocumentAsJson("Optional", "false")]
        public bool TriggeredNameReveal { get; set; }

        public bool IsNameRevealedFor(string playeruid)
        {
            return entity.WatchedAttributes.GetTreeAttribute("nametag")?.GetTreeAttribute("nameRevealedFor")?.HasAttribute(playeruid) == true;
        }
        public void SetNameRevealedFor(string playeruid)
        {
            var ntree = entity.WatchedAttributes.GetOrAddTreeAttribute("nametag");
            var tree = ntree.GetOrAddTreeAttribute("nameRevealedFor");
            tree.SetBool(playeruid, true);
            OnNameChanged();
        }

        /// <summary>
        /// The maximum range the nametag is visible from
        /// </summary>
        [DocumentAsJson("Optional", "999")]
        public int RenderRange
        {
            get { return entity.WatchedAttributes.GetTreeAttribute("nametag")?.GetInt("renderRange") ?? 999; }
            set { entity.WatchedAttributes.GetTreeAttribute("nametag")?.SetInt("renderRange", value); }
        }

        public double RenderOrder => 1;

        public EntityBehaviorNameTag(Entity entity) : base(entity)
        {
            ITreeAttribute nametagTree = entity.WatchedAttributes.GetTreeAttribute("nametag");
            if (nametagTree == null)
            {
                entity.WatchedAttributes.SetAttribute("nametag", nametagTree = new TreeAttribute());
                nametagTree.SetString("name", "");
                nametagTree.SetInt("showtagonlywhentargeted", 0);
                nametagTree.SetInt("renderRange", 999);
                entity.WatchedAttributes.MarkPathDirty("nametag");
            }
        }

        public override void Initialize(EntityProperties entityType, JsonObject attributes)
        {
            base.Initialize(entityType, attributes);

            if ((DisplayName == null || DisplayName.Length == 0))
            {
                string?[]? randomName = attributes["selectFromRandomName"].AsArray<string>();
                if (randomName != null && randomName.Length > 0)
                {
                    SetName(randomName[entity.World.Rand.Next(randomName.Length)]);
                }
            }
                
            TriggeredNameReveal = attributes["triggeredNameReveal"].AsBool(false);
            RenderRange = attributes["renderRange"].AsInt(999);
            ShowOnlyWhenTargeted = attributes["showtagonlywhentargeted"].AsBool(false);
            ReplaceDefaultName = attributes["replaceDefaultName"].AsBool(true);
            UnrevealedDisplayName = Lang.Get(attributes["unrevealedDisplayName"].AsString("nametag-default-unrevealedname"));

            entity.WatchedAttributes.OnModified.Add(new TreeModifiedListener() { path = "nametag", listener = OnNameChanged });
            OnNameChanged();

            capi = entity.World.Api as ICoreClientAPI;
            if (capi != null)
            {
                capi.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "nametag");
            }
        }


        protected bool IsSelf => capi == null ? false : entity.EntityId == capi.World.Player.Entity.EntityId;
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            ICoreClientAPI capi = this.capi!;
            if (IsSelf)
            {
                if (capi.Render.CameraType == EnumCameraMode.FirstPerson) return;
            }

            if (ShowOnlyWhenTargeted && capi.World.Player.CurrentEntitySelection?.Entity != entity) return;

            if (nameTagRenderHandler == null || (entity is EntityPlayer && player == null))
            {
                player = (entity as EntityPlayer)?.Player;
                if (player != null || !(entity is EntityPlayer))
                {
                    nameTagRenderHandler = capi.ModLoader.GetModSystem<EntityNameTagRendererRegistry>()?.GetNameTagRenderer(entity)
                        ?? throw new Exception("Can't render name tag without a renderer");
                    OnNameChanged();
                }
            }

            bool isSpectator = player != null && player.WorldData.CurrentGameMode == EnumGameMode.Spectator;

            if (isSpectator) return;
            if (player?.Entity?.ServerControls.Sneak == true) return;

            IRenderAPI rapi = capi.Render;
            EntityPlayer entityPlayer = capi.World.Player.Entity;
            double dist = entityPlayer.Pos.SquareDistanceTo(entity.Pos);
            if (renderRange * renderRange <= dist) return;

            var esr = (entity.Properties.Client.Renderer as EntityShapeRenderer);
            if (esr == null) return;

            Vec3d aboveHeadPos = esr.getAboveHeadPosition(entityPlayer);

            Vec3d pos = MatrixToolsd.Project(aboveHeadPos, rapi.PerspectiveProjectionMat, rapi.PerspectiveViewMat, rapi.FrameWidth, rapi.FrameHeight);

            // Z negative seems to indicate that the name tag is behind us \o/
            if (pos.Z < 0) return;

            EnsureNameTagTextureExists();
            if (nameTagTexture == null) return;

            float scale = 4f / Math.Max(1, (float)pos.Z);

            float cappedScale = Math.Min(1f, scale);
            if (cappedScale > 0.75f) cappedScale = 0.75f + (cappedScale - 0.75f) / 2;

            float posx = (float)pos.X - cappedScale * nameTagTexture.Width / 2;
            float posy = rapi.FrameHeight - (float)pos.Y - (nameTagTexture.Height * Math.Max(0, cappedScale));

            rapi.Render2DTexture(
                nameTagTexture.TextureId, posx, posy, cappedScale * nameTagTexture.Width, cappedScale * nameTagTexture.Height, 20
            );
        }

        public void Dispose()
        {
            if (nameTagTexture != null)
            {
                nameTagTexture.Dispose();
                nameTagTexture = null;
            }

            if (capi != null)
            {
                capi.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
            }
        }

        public override string? GetName(ref EnumHandling handling)
        {
            string? name = DisplayName;
            if (ReplaceDefaultName && name != null && name != "") handling = EnumHandling.PreventDefault;
            return name;
        }

        public void SetName(string? newName)
        {
            SetName(entity, newName);
        }

        public static void SetName(Entity entity, string? newName)
        {
            if (newName == "") newName = null;

            ITreeAttribute nametagTree = entity.WatchedAttributes.GetOrAddTreeAttribute("nametag");
            nametagTree.SetString("name", newName);
            entity.WatchedAttributes.MarkPathDirty("nametag");
        }

        protected void OnNameChanged()
        {
            if (nameTagTexture != null)
            {
                nameTagTexture.Dispose();
                nameTagTexture = null;
            }

            nameTagTextureDirty = true;
            if (!ShowOnlyWhenTargeted) EnsureNameTagTextureExists();
        }

        private void EnsureNameTagTextureExists()
        {
            if (!nameTagTextureDirty || nameTagRenderHandler == null) return;

            nameTagTexture = nameTagRenderHandler.Invoke(capi, entity);
            nameTagTextureDirty = false;
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            Dispose();
        }
        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            Dispose();
        }

        public override string PropertyName()
        {
            return "displayname";
        }
    }
}
