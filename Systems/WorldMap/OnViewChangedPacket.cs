#nullable disable

namespace Vintagestory.GameContent;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OnViewChangedPacket
    {
        public int X1;
        public int Z1;
        public int X2;
        public int Z2;
    }
