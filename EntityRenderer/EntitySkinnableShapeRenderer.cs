using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{

    public class EntitySkinnableShapeRenderer : EntityShapeRenderer
    {
        public event Action<LoadedTexture, TextureAtlasPosition> OnReloadSkin;
        protected int skinTextureSubId;
        protected bool IsSelf => entity.EntityId == capi.World.Player.Entity.EntityId;
        public double RenderOrder => 1;
        public int RenderRange => 1;


        public EntitySkinnableShapeRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            api.Event.ReloadTextures += MarkShapeModified;
        }


        public override TextureAtlasPosition this[string textureCode]
        {
            get
            {
                CompositeTexture cpt = null;
                if (extraTexturesByTextureName?.TryGetValue(textureCode, out cpt) == true)
                {
                    return capi.EntityTextureAtlas.Positions[cpt.Baked.TextureSubId];
                }

                return skinTexPos;
            }
        }


        public override void TesselateShape()
        {
            base.TesselateShape();

            if (eagent.GearInventory != null)
            {
                reloadSkin();
            }
        }



        bool textureSpaceAllocated = false;
        protected override ITexPositionSource GetTextureSource()
        {
            if (!textureSpaceAllocated)
            {
                TextureAtlasPosition origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.FirstTexture.Baked.TextureSubId];
                string skinBaseTextureKey = entity.Properties.Attributes?["skinBaseTextureKey"].AsString();
                if (skinBaseTextureKey != null) origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.Textures[skinBaseTextureKey].Baked.TextureSubId];

                int width = (int)((origTexPos.x2 - origTexPos.x1) * AtlasSize.Width);
                int height = (int)((origTexPos.y2 - origTexPos.y1) * AtlasSize.Height);

                capi.EntityTextureAtlas.AllocateTextureSpace(width, height, out skinTextureSubId, out skinTexPos);

                textureSpaceAllocated = true;
            }

            return base.GetTextureSource();
        }

        public bool doReloadShapeAndSkin = true;




        public override void reloadSkin()
        {
            if (!doReloadShapeAndSkin) return;

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

            OnReloadSkin?.Invoke(entityAtlas, skinTexPos);

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

            if (gearInv == null && eagent?.GearInventory != null)
            {
                registerSlotModified(false);
            }

            for (int i = 0; i < renderOrder.Length; i++)
            {
                int slotid = renderOrder[i];

                ItemStack stack = gearInv[slotid]?.Itemstack;
                if (stack == null) continue;
                if (eagent.hideClothing) continue;
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


        public override void Dispose()
        {
            base.Dispose();

            capi.Event.ReloadTextures -= MarkShapeModified;
            if (eagent?.GearInventory != null)
            {
                eagent.GearInventory.SlotModified -= gearSlotModified;
            }

            capi.EntityTextureAtlas.FreeTextureSpace(skinTextureSubId);
        }
    }
}
