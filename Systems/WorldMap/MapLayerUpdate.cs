#nullable disable

namespace Vintagestory.GameContent;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class MapLayerUpdate
    {
        public MapLayerData[] Maplayers;
    }
