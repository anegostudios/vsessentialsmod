#nullable disable

namespace Vintagestory.GameContent;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class MapLayerData
    {
        public string ForMapLayer;
        public byte[] Data;
    }
