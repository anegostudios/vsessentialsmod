using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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
                    new TextBackground() { FillColor = GuiStyle.DialogLightBgColor, Padding = 3, Radius = GuiStyle.ElementBGRadius }
                );
            }

            return null;
        };

        public static Dictionary<string, NameTagRendererDelegate> nameTagRenderersByEntitlementCode = new Dictionary<string, NameTagRendererDelegate>()
        {
            { "vsteam", vsTeamNameTagRenderer },
            { "vscontributor", vsContributorNameTagRenderer },
            { "vssupporter", vsSupporterNameTagRenderer },
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

            if (eplr?.Player != null && eplr.Player.Entitlements.Count > 0)
            {
                Entitlement ent = eplr.Player.Entitlements[0];
                NameTagRendererDelegate dele = null;

                if (nameTagRenderersByEntitlementCode.TryGetValue(ent.Code, out dele))
                {
                    return dele;
                }
            }

            return DefaultNameTagRenderer;
        }


        private static LoadedTexture vsSupporterNameTagRenderer(ICoreClientAPI capi, Entity entity)
        {
            EntityBehaviorNameTag behavior = entity.GetBehavior<EntityBehaviorNameTag>();
            string name = behavior?.DisplayName;

            if (name != null && name.Length > 0)
            {
                return capi.Gui.TextTexture.GenUnscaledTextTexture(
                    name,
                    CairoFont.WhiteMediumText().WithColor(new double[] { 254/255.0, 197/255.0, 0, 1 }),
                    new TextBackground() { FillColor = GuiStyle.DialogDefaultBgColor, Padding = 3, Radius = GuiStyle.ElementBGRadius }
                );
            }

            return null;
        }


        private static LoadedTexture vsContributorNameTagRenderer(ICoreClientAPI capi, Entity entity)
        {
            EntityBehaviorNameTag behavior = entity.GetBehavior<EntityBehaviorNameTag>();
            string name = behavior?.DisplayName;

            if (name != null && name.Length > 0)
            {
                return capi.Gui.TextTexture.GenUnscaledTextTexture(
                    name,
                    CairoFont.WhiteMediumText().WithColor(new double[] { 135 / 255.0, 179 / 255.0, 148 / 255.0, 1 } ),
                    new TextBackground() { FillColor = GuiStyle.DialogDefaultBgColor, Padding = 3, Radius = GuiStyle.ElementBGRadius }
                );
            }

            return null;
        }


        private static LoadedTexture vsTeamNameTagRenderer(ICoreClientAPI capi, Entity entity)
        {
            EntityBehaviorNameTag behavior = entity.GetBehavior<EntityBehaviorNameTag>();
            string name = behavior?.DisplayName;

            if (name != null && name.Length > 0)
            {
                return capi.Gui.TextTexture.GenUnscaledTextTexture(
                    name,
                    CairoFont.WhiteMediumText().WithColor(new double[] { 14 / 255.0, 114 / 255.0, 57 / 255.0, 1 }),
                    new TextBackground() { FillColor = GuiStyle.DialogDefaultBgColor, Padding = 3, Radius = GuiStyle.ElementBGRadius }
                );
            }

            return null;
        }




    }
}
