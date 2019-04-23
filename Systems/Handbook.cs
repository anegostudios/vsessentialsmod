using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{

    public class ModSystemHandbook : ModSystem
    {
        ICoreAPI api;
        ICoreClientAPI capi;

        GuiDialogHandbook dialog;
        

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Client;
        }

        public override void Start(ICoreAPI api)
        {
            this.api = api;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;

            api.Input.RegisterHotKey("handbook", "Show VS Handbook", GlKeys.H, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("handbook", OnHelpHotkey);

            api.Event.LevelFinalize += Event_LevelFinalize;
            api.RegisterLinkProtocol("handbook", onHandBookLinkClicked);
        }

        private void onHandBookLinkClicked(LinkTextComponent comp)
        {
            string target = comp.Href.Substring("handbook://".Length);
            if (!dialog.IsOpened()) dialog.TryOpen();

            dialog.OpenDetailPageFor(target);
        }

        private void Event_LevelFinalize()
        {
            dialog = new GuiDialogHandbook(capi);
        }

        private bool OnHelpHotkey(KeyCombination key)
        {
            if (dialog.IsOpened())
            {
                dialog.TryClose();
            } else
            {
                dialog.TryOpen();
                // dunno why
                dialog.ignoreNextKeyPress = true;

                if (capi.World.Player.InventoryManager.CurrentHoveredSlot?.Itemstack != null)
                {
                    string pageCode = HandbookStacklistElement.PageCodeForCollectible(capi.World.Player.InventoryManager.CurrentHoveredSlot.Itemstack.Collectible); 

                    dialog.OpenDetailPageFor(pageCode);
                }

                if (capi.World.Player.Entity.Controls.Sneak && capi.World.Player.CurrentBlockSelection != null)
                {
                    Block block = capi.World.BlockAccessor.GetBlock(capi.World.Player.CurrentBlockSelection.Position);

                    string pageCode = HandbookStacklistElement.PageCodeForCollectible(block);

                    dialog.OpenDetailPageFor(pageCode);
                }
            }

            return true;
        }
        
    }
}
