namespace FloatSpotify.Storage;

[System.Text.Json.Serialization.JsonConverter(
    typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum OverlayPlacement
{
    Custom,
    Top,
    Center,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}
