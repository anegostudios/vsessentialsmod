using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public delegate LoadedTexture NameTagRendererDelegate(ICoreClientAPI capi, Entity forEntity);

    public class EntityNameTagRendererRegistry : ModSystem
    {
        public static NameTagRendererDelegate DefaultNameTagRenderer = (capi, entity) =>
        {
            EntityBehaviorNameTag behavior = entity.GetBehavior<EntityBehaviorNameTag>();
            string name = behavior?.DisplayName;

            if (name != null && name.Length > 0)
            {
                return capi.Gui.TextTexture.GenUnscaledTextTexture(
                    name,
                    CairoFont.WhiteMediumText().WithColor(ColorUtil.WhiteArgbDouble),
                    new TextBackground() {
                        FillColor = GuiStyle.DialogLightBgColor,
                        Padding = 3,
                        Radius = GuiStyle.ElementBGRadius,
                        Shade = true,
                        BorderColor = GuiStyle.DialogBorderColor,
                        BorderWidth = 3,
                    }
                );
            }

            return null;
        };


        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Client;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
        }


        internal NameTagRendererDelegate GetNameTagRenderer(Entity entity)
        {
            EntityPlayer eplr = entity as EntityPlayer;
            var entitlements = eplr?.Player.Entitlements;

            if (entitlements?.Count > 0)
            {
                Entitlement ent = entitlements[0];
                double[] color = null;
                
                if (GlobalConstants.playerColorByEntitlement.TryGetValue(ent.Code, out color))
                {
                    TextBackground bg;
                    GlobalConstants.playerTagBackgroundByEntitlement.TryGetValue(ent.Code, out bg);
                    DefaultEntitlementTagRenderer var = new DefaultEntitlementTagRenderer() { color = color, background = bg };

                    return var.renderTag;
                }
            }

            return DefaultNameTagRenderer;
        }


        public class DefaultEntitlementTagRenderer
        {
            public double[] color;
            public TextBackground background;

            public LoadedTexture renderTag(ICoreClientAPI capi, Entity entity)
            {
                EntityBehaviorNameTag behavior = entity.GetBehavior<EntityBehaviorNameTag>();
                string name = behavior?.DisplayName;

                if (name != null && name.Length > 0)
                {
                    return capi.Gui.TextTexture.GenUnscaledTextTexture(
                        name,
                        CairoFont.WhiteMediumText().WithColor(color),
                        background
                    );
                }

                return null;
            }

        }



    }
}
