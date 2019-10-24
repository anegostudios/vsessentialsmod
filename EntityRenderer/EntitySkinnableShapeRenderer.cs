using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Vintagestory.GameContent
{
    public class EntitySkinnableShapeRenderer : EntityShapeRenderer, ITexPositionSource
    {
        TextureAtlasPosition skinTexPos;
        int skinTextureSubId;
        IInventory gearInv;
        
        public int AtlasSize { get { return capi.EntityTextureAtlas.Size; } }

        public TextureAtlasPosition this[string textureCode]
        {
            get {
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

        void slotModified(int slotid)
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

        public override void BeforeRender(float dt)
        {
            if (gearInv == null && eagent?.GearInventory != null)
            {
                eagent.GearInventory.SlotModified += slotModified;
                gearInv = eagent.GearInventory;
                TesselateShape();
            }

            base.BeforeRender(dt);
        }

        public override void PrepareForGuiRender(float dt, double posX, double posY, double posZ, float yawDelta, float size, out MeshRef meshRef, out float[] modelviewMatrix)
        {
            if (gearInv == null && eagent?.GearInventory != null)
            {
                eagent.GearInventory.SlotModified += slotModified;
                gearInv = eagent.GearInventory;
                TesselateShape();
            }

            base.PrepareForGuiRender(dt, posX, posY, posZ, yawDelta, size, out meshRef, out modelviewMatrix);
        }


        bool textureSpaceAllocated = false;
        protected override ITexPositionSource GetTextureSource()
        {
            if (!textureSpaceAllocated)
            {
                TextureAtlasPosition origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.FirstTexture.Baked.TextureSubId];
                int width = (int)((origTexPos.x2 - origTexPos.x1) * AtlasSize);
                int height = (int)((origTexPos.y2 - origTexPos.y1) * AtlasSize);

                capi.EntityTextureAtlas.AllocateTextureSpace(width, height, out skinTextureSubId, out skinTexPos);

                textureSpaceAllocated = true;
            }

            return this;
        }


        public override void TesselateShape()
        {
            base.TesselateShape();

            if (eagent.GearInventory != null)
            {
                reloadSkin();
            }
        }

        public void reloadSkin()
        {
            TextureAtlasPosition origTexPos = capi.EntityTextureAtlas.Positions[entity.Properties.Client.FirstTexture.Baked.TextureSubId];
            
            LoadedTexture entityAtlas = new LoadedTexture(null) {
                TextureId = origTexPos.atlasTextureId,
                Width = capi.EntityTextureAtlas.Size,
                Height = capi.EntityTextureAtlas.Size
            };

            capi.Render.GlToggleBlend(false);
            capi.EntityTextureAtlas.RenderTextureIntoAtlas(
                entityAtlas,
                (int)(origTexPos.x1 * AtlasSize),
                (int)(origTexPos.y1 * AtlasSize),
                (int)((origTexPos.x2 - origTexPos.x1) * AtlasSize),
                (int)((origTexPos.x2 - origTexPos.x1) * AtlasSize),
                skinTexPos.x1 * capi.EntityTextureAtlas.Size,
                skinTexPos.y1 * capi.EntityTextureAtlas.Size,
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
                eagent.GearInventory.SlotModified += (slotid) => reloadSkin();
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
                    Width = capi.ItemTextureAtlas.Size,
                    Height = capi.ItemTextureAtlas.Size
                };

                capi.EntityTextureAtlas.RenderTextureIntoAtlas(
                    itemAtlas,
                    itemTexPos.x1 * capi.ItemTextureAtlas.Size,
                    itemTexPos.y1 * capi.ItemTextureAtlas.Size,
                    (itemTexPos.x2 - itemTexPos.x1) * capi.ItemTextureAtlas.Size,
                    (itemTexPos.y2 - itemTexPos.y1) * capi.ItemTextureAtlas.Size,
                    skinTexPos.x1 * capi.EntityTextureAtlas.Size,
                    skinTexPos.y1 * capi.EntityTextureAtlas.Size
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
            capi.EntityTextureAtlas.FreeTextureSpace(skinTextureSubId);
        }
    }
}
