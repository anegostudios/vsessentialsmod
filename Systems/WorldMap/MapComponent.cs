using System;
using System.Text;
using Vintagestory.API.Client;

#nullable disable

namespace Vintagestory.GameContent
{
    public abstract class MapComponent : IDisposable
    {
        public ICoreClientAPI capi;

        public MapComponent(ICoreClientAPI capi)
        {
            this.capi = capi;
        }



        public virtual void Render(GuiElementMap map, float dt)
        {

        }


        public virtual void Dispose()
        {

        }

        public virtual void OnMouseMove(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {

        }

        public virtual void OnMouseUpOnElement(MouseEvent args, GuiElementMap mapElem)
        {

        }
    }
}
