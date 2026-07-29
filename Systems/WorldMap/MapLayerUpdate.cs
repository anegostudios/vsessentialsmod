namespace Vintagestory.GameContent;

/// <summary>
///     Represents a payload used to update the visible map layers for the current session.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class MapLayerUpdate
{
    /// <summary>
    ///     The collection of map layers that should be updated or applied.
    /// </summary>
    public MapLayerData[] Maplayers { get; set; }
}