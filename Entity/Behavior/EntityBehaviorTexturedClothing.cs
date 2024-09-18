using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public abstract class EntityBehaviorTexturedClothing : EntityBehaviorContainer
    {
        public event Action<LoadedTexture, TextureAtlasPosition, int> OnReloadSkin;
        protected int skinTextureSubId;
        

        ICoreClientAPI capi;
        protected TextureAtlasPosition skinTexPos
        {
            get {
                return (entity.Properties.Client.Renderer as EntityShapeRenderer).skinTexPos; 
            }
            set { 
                (entity.Properties.Client.Renderer as EntityShapeRenderer).skinTexPos = value; 
            }
        }

        public Size2i AtlasSize { get { return capi.EntityTextureAtlas.Size; } }

        public EntityBehaviorTexturedClothing(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            capi = Api as ICoreClientAPI;
        }

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);

            reloadSkin();
        }

        bool textureSpaceAllocated = false;

        public override ITexPositionSource GetTextureSource(ref EnumHandling handling)
        {
            handling = EnumHandling.PassThrough;

            if (!textureSpaceAllocated)
            {
                TextureAtlasPosition origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.FirstTexture.Baked.TextureSubId];
                string skinBaseTextureKey = entity.Properties.Attributes?["skinBaseTextureKey"].AsString();
                if (skinBaseTextureKey != null) origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.Textures[skinBaseTextureKey].Baked.TextureSubId];

                int width = (int)((origTexPos.x2 - origTexPos.x1) * AtlasSize.Width);
                int height = (int)((origTexPos.y2 - origTexPos.y1) * AtlasSize.Height);

                capi.EntityTextureAtlas.AllocateTextureSpace(width, height, out skinTextureSubId, out var skinTexPos);
                this.skinTexPos = skinTexPos;

                textureSpaceAllocated = true;
            }

            return null;
        }

        public bool doReloadShapeAndSkin = true;


        public void reloadSkin()
        {
            if (capi == null || !doReloadShapeAndSkin || skinTexPos == null) return;

            TextureAtlasPosition origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.FirstTexture.Baked.TextureSubId];
            string skinBaseTextureKey = entity.Properties.Attributes?["skinBaseTextureKey"].AsString();
            if (skinBaseTextureKey != null) origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.Textures[skinBaseTextureKey].Baked.TextureSubId];

            LoadedTexture entityAtlas = new LoadedTexture(null)
            {
                TextureId = origTexPos.atlasTextureId,
                Width = capi.EntityTextureAtlas.Size.Width,
                Height = capi.EntityTextureAtlas.Size.Height
            };

            capi.Render.GlToggleBlend(false);
            capi.EntityTextureAtlas.RenderTextureIntoAtlas(
                skinTexPos.atlasTextureId,
                entityAtlas,
                (int)(origTexPos.x1 * AtlasSize.Width),
                (int)(origTexPos.y1 * AtlasSize.Height),
                (int)((origTexPos.x2 - origTexPos.x1) * AtlasSize.Width),
                (int)((origTexPos.y2 - origTexPos.y1) * AtlasSize.Height),
                skinTexPos.x1 * capi.EntityTextureAtlas.Size.Width,
                skinTexPos.y1 * capi.EntityTextureAtlas.Size.Height,
                -1
            );

            capi.Render.GlToggleBlend(true, EnumBlendMode.Overlay);

            OnReloadSkin?.Invoke(entityAtlas, skinTexPos, skinTextureSubId);

            int[] renderOrder = new int[]
            {
                (int)EnumCharacterDressType.LowerBody,
                (int)EnumCharacterDressType.Foot,
                (int)EnumCharacterDressType.UpperBody,
                (int)EnumCharacterDressType.UpperBodyOver,
                (int)EnumCharacterDressType.Waist,
                (int)EnumCharacterDressType.Shoulder,
                (int)EnumCharacterDressType.Emblem,
                (int)EnumCharacterDressType.Neck,
                (int)EnumCharacterDressType.Head,
                (int)EnumCharacterDressType.Hand,
                (int)EnumCharacterDressType.Arm,
                (int)EnumCharacterDressType.Face
            };


            for (int i = 0; i < renderOrder.Length; i++)
            {
                int slotid = renderOrder[i];

                ItemStack stack = Inventory[slotid]?.Itemstack;
                if (stack == null) continue;
                if (hideClothing) continue;
                if (stack.Item.FirstTexture == null) continue; // Invalid/Unknown/Corrupted item

                int itemTextureSubId = stack.Item.FirstTexture.Baked.TextureSubId;

                TextureAtlasPosition itemTexPos = capi.ItemTextureAtlas.Positions[itemTextureSubId];

                LoadedTexture itemAtlas = new LoadedTexture(null)
                {
                    TextureId = itemTexPos.atlasTextureId,
                    Width = capi.ItemTextureAtlas.Size.Width,
                    Height = capi.ItemTextureAtlas.Size.Height
                };

                capi.EntityTextureAtlas.RenderTextureIntoAtlas(
                    skinTexPos.atlasTextureId,
                    itemAtlas,
                    itemTexPos.x1 * capi.ItemTextureAtlas.Size.Width,
                    itemTexPos.y1 * capi.ItemTextureAtlas.Size.Height,
                    (itemTexPos.x2 - itemTexPos.x1) * capi.ItemTextureAtlas.Size.Width,
                    (itemTexPos.y2 - itemTexPos.y1) * capi.ItemTextureAtlas.Size.Height,
                    skinTexPos.x1 * capi.EntityTextureAtlas.Size.Width,
                    skinTexPos.y1 * capi.EntityTextureAtlas.Size.Height
                );
            }

            capi.Render.GlToggleBlend(true);
            capi.Render.BindTexture2d(skinTexPos.atlasTextureId);
            capi.Render.GlGenerateTex2DMipmaps();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);

            capi?.EntityTextureAtlas.FreeTextureSpace(skinTextureSubId);
        }

        public override string PropertyName() => "clothing";
    }
}
