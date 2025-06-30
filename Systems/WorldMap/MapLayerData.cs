namespace Vintagestory.GameContent;

/// <summary>
///     Represents a single map layer and its associated serialised data, sent to or from the client.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class MapLayerData
{
    /// <summary>
    ///     The identifier or key corresponding to the map layer this data belongs to.
    /// </summary>
    public string ForMapLayer { get; set; }

    /// <summary>
    ///     The raw, serialised binary data representing the layer's content or state.
    /// </summary>
    public byte[] Data { get; set; }
}