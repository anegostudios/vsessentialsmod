using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntitySkinnableShapeRenderer : EntityShapeRenderer
    {
        
        protected int skinTextureSubId;


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



        public EntitySkinnableShapeRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
        {
            api.Event.ReloadTextures += reloadSkin;
        }




        bool textureSpaceAllocated = false;
        protected override ITexPositionSource GetTextureSource()
        {
            if (!textureSpaceAllocated)
            {
                TextureAtlasPosition origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.FirstTexture.Baked.TextureSubId];
                int width = (int)((origTexPos.x2 - origTexPos.x1) * AtlasSize.Width);
                int height = (int)((origTexPos.y2 - origTexPos.y1) * AtlasSize.Height);

                capi.EntityTextureAtlas.AllocateTextureSpace(width, height, out skinTextureSubId, out skinTexPos);

                textureSpaceAllocated = true;
            }

            return base.GetTextureSource();
        }


        public override void TesselateShape()
        {
            base.TesselateShape();

            if (eagent.GearInventory != null)
            {
                reloadSkin();
            }
        }

        public override void reloadSkin()
        {
            TextureAtlasPosition origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.FirstTexture.Baked.TextureSubId];
            
            LoadedTexture entityAtlas = new LoadedTexture(null) {
                TextureId = origTexPos.atlasTextureId,
                Width = capi.EntityTextureAtlas.Size.Width,
                Height = capi.EntityTextureAtlas.Size.Height
            };

            capi.Render.GlToggleBlend(false);
            capi.EntityTextureAtlas.RenderTextureIntoAtlas(
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
                eagent.GearInventory.SlotModified += gearSlotModified;
                gearInv = eagent.GearInventory;
            }

            for (int i = 0; i < renderOrder.Length; i++)
            {
                int slotid = renderOrder[i];

                ItemStack stack = gearInv[slotid]?.Itemstack;
                if (stack == null) continue;

                int itemTextureSubId = stack.Item.FirstTexture.Baked.TextureSubId;

                TextureAtlasPosition itemTexPos = capi.ItemTextureAtlas.Positions[itemTextureSubId];
                
                LoadedTexture itemAtlas = new LoadedTexture(null) {
                    TextureId = itemTexPos.atlasTextureId,
                    Width = capi.ItemTextureAtlas.Size.Width,
                    Height = capi.ItemTextureAtlas.Size.Height
                };

                capi.EntityTextureAtlas.RenderTextureIntoAtlas(
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

            capi.Event.ReloadTextures -= reloadSkin;
            if (eagent?.GearInventory != null)
            {
                eagent.GearInventory.SlotModified -= gearSlotModified;
            }

            capi.EntityTextureAtlas.FreeTextureSpace(skinTextureSubId);
        }
    }
}
