using System;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorNameTag : EntityBehavior, IRenderer
    {
        protected LoadedTexture nameTagTexture = null;
        protected bool showNameTagOnlyWhenTargeted = false;
        protected NameTagRendererDelegate nameTagRenderHandler;
        ICoreClientAPI capi;
        protected int renderRange = 999;
        IPlayer player;

        /// <summary>
        /// The display name for the entity.
        /// </summary>
        public string DisplayName
        {
            get {
                if (capi != null && TriggeredNameReveal && !IsNameRevealedFor(capi.World.Player.PlayerUID)) return UnrevealedDisplayName;
                return entity.WatchedAttributes.GetTreeAttribute("nametag")?.GetString("name"); 
            }
        }

        public string UnrevealedDisplayName { get; set; }

        /// <summary>
        /// Whether or not to show the nametag constantly or only when being looked at.
        /// </summary>
        public bool ShowOnlyWhenTargeted
        {
            get { return entity.WatchedAttributes.GetTreeAttribute("nametag")?.GetBool("showtagonlywhentargeted") == true; }
            set { entity.WatchedAttributes.GetTreeAttribute("nametag")?.SetBool("showtagonlywhentargeted", value); }
        }

        public bool TriggeredNameReveal { get; set; }

        public bool IsNameRevealedFor(string playeruid)
        {
            return entity.WatchedAttributes.GetTreeAttribute("nametag")?.GetTreeAttribute("nameRevealedFor")?.HasAttribute(playeruid) == true;
        }
        public void SetNameRevealedFor(string playeruid)
        {
            var ntree = entity.WatchedAttributes.GetTreeAttribute("nametag");
            if (ntree == null) entity.WatchedAttributes["nametag"] = ntree = new TreeAttribute();
            var tree = ntree?.GetTreeAttribute("nameRevealedFor");
            if (tree == null) ntree["nameRevealedFor"] = tree = new TreeAttribute();
            tree.SetBool(playeruid, true);
            OnNameChanged();
        }


        public int RenderRange
        {
            get { return entity.WatchedAttributes.GetTreeAttribute("nametag").GetInt("renderRange"); }
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

            if ((DisplayName == null || DisplayName.Length == 0) && attributes["selectFromRandomName"].Exists)
            {
                string[] randomName = attributes["selectFromRandomName"].AsArray<string>();

                SetName(randomName[entity.World.Rand.Next(randomName.Length)]);
            }

            TriggeredNameReveal = attributes["triggeredNameReveal"].AsBool(false);
            RenderRange = attributes["renderRange"].AsInt(999);
            ShowOnlyWhenTargeted = attributes["showtagonlywhentargeted"].AsBool(false);
            UnrevealedDisplayName = attributes["unrevealedDisplayName"].AsString("Stranger");

            entity.WatchedAttributes.OnModified.Add(new TreeModifiedListener() { path = "nametag", listener = OnNameChanged });
            OnNameChanged();

            capi = entity.World.Api as ICoreClientAPI;
            if (capi != null)
            {
                capi.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "nametag");
            }
        }


        protected bool IsSelf => entity.EntityId == capi.World.Player.Entity.EntityId;
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (IsSelf)
            {
                if (capi.Render.CameraType == EnumCameraMode.FirstPerson) return;
            }

            if (nameTagRenderHandler == null || (entity is EntityPlayer && player == null))
            {
                player = (entity as EntityPlayer)?.Player;
                if (player != null || !(entity is EntityPlayer))
                {
                    nameTagRenderHandler = capi.ModLoader.GetModSystem<EntityNameTagRendererRegistry>().GetNameTagRenderer(entity);
                    OnNameChanged();
                }
            }

            bool isSpectator = player != null && player.WorldData.CurrentGameMode == EnumGameMode.Spectator;

            if (isSpectator || nameTagTexture == null) return;
            if (player?.Entity?.ServerControls.Sneak == true) return;

            IRenderAPI rapi = capi.Render;
            EntityPlayer entityPlayer = capi.World.Player.Entity;
            var esr = (entity.Properties.Client.Renderer as EntityShapeRenderer);
            if (esr == null) return;

            Vec3d aboveHeadPos = esr.getAboveHeadPosition(entityPlayer);

            Vec3d pos = MatrixToolsd.Project(aboveHeadPos, rapi.PerspectiveProjectionMat, rapi.PerspectiveViewMat, rapi.FrameWidth, rapi.FrameHeight);

            // Z negative seems to indicate that the name tag is behind us \o/
            if (pos.Z < 0) return;

            float scale = 4f / Math.Max(1, (float)pos.Z);

            float cappedScale = Math.Min(1f, scale);
            if (cappedScale > 0.75f) cappedScale = 0.75f + (cappedScale - 0.75f) / 2;

            float offY = 0;

            double dist = entityPlayer.Pos.SquareDistanceTo(entity.Pos);
            if (nameTagTexture != null && (!ShowOnlyWhenTargeted || capi.World.Player.CurrentEntitySelection?.Entity == entity) && renderRange * renderRange > dist)
            {
                float posx = (float)pos.X - cappedScale * nameTagTexture.Width / 2;
                float posy = rapi.FrameHeight - (float)pos.Y - (nameTagTexture.Height * Math.Max(0, cappedScale));

                rapi.Render2DTexture(
                    nameTagTexture.TextureId, posx, posy, cappedScale * nameTagTexture.Width, cappedScale * nameTagTexture.Height, 20
                );

                offY += nameTagTexture.Height;
            }
        }

        public void Dispose()
        {
            if (nameTagTexture != null)
            {
                nameTagTexture.Dispose();
                nameTagTexture = null;
            }
        }

        public override string GetName(ref EnumHandling handling)
        {
            handling = EnumHandling.PreventDefault;
            return DisplayName;
        }

        public void SetName(string playername)
        {
            ITreeAttribute nametagTree = entity.WatchedAttributes.GetTreeAttribute("nametag");
            if (nametagTree == null)
            {
                entity.WatchedAttributes.SetAttribute("nametag", nametagTree = new TreeAttribute());
            }

            nametagTree.SetString("name", playername);
            entity.WatchedAttributes.MarkPathDirty("nametag");
        }

        protected void OnNameChanged()
        {
            if (nameTagRenderHandler == null) return;
            if (nameTagTexture != null)
            {
                nameTagTexture.Dispose();
                nameTagTexture = null;
            }

            nameTagTexture = nameTagRenderHandler.Invoke(capi, entity);
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
