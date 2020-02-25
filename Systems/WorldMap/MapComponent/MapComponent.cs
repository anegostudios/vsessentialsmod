using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;

namespace Vintagestory.GameContent
{
    public abstract class MapComponent
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
