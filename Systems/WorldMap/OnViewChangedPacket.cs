namespace Vintagestory.GameContent;

/// <summary>
///     Represents a network packet indicating that the client's view bounds have changed.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class OnViewChangedPacket
{
    /// <summary>
    ///     The minimum X coordinate of the view bounds.
    /// </summary>
    public int X1 { get; set; }

    /// <summary>
    ///     The minimum Z coordinate of the view bounds.
    /// </summary>
    public int Z1 { get; set; }

    /// <summary>
    ///     The maximum X coordinate of the view bounds.
    /// </summary>
    public int X2 { get; set; }

    /// <summary>
    ///     The maximum Z coordinate of the view bounds.
    /// </summary>
    public int Z2 { get; set; }
}