namespace Vintagestory.GameContent;

/// <summary>
///     Represents a network packet indicating that the map should be opened or closed.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class OnMapToggle
{
    /// <summary>
    ///     Whether to open (<c>true</c>) or close (<c>false</c>) the map.
    /// </summary>
    public bool OpenOrClose { get; set; }
}