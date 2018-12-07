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
        }

        private bool OnHelpHotkey(KeyCombination key)
        {
            if (dialog != null)
            {
                dialog.TryClose();
                dialog = null;
            } else
            {
                dialog = new GuiDialogHandbook(capi);
                dialog.OnClosed += () => dialog = null;

                dialog.TryOpen();

                if (capi.World.Player.InventoryManager.CurrentHoveredSlot?.Itemstack != null)
                {
                    dialog.OpenDetailPageFor(capi.World.Player.InventoryManager.CurrentHoveredSlot.Itemstack);
                }

                if (capi.World.Player.Entity.Controls.Sneak && capi.World.Player.CurrentBlockSelection != null)
                {
                    Block block = capi.World.BlockAccessor.GetBlock(capi.World.Player.CurrentBlockSelection.Position);
                    dialog.OpenDetailPageFor(new ItemStack(block));
                }
            }

            return true;
        }
        
    }
}
