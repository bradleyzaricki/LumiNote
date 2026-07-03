using Avalonia.Media;

namespace LumikitApp
{
    public enum ProviderType
    {
        Spotify,
        LocalFiles
    }

    // Per-provider display metadata. Add a case here when adding a provider — UI is data-driven off it.
    public static class ProviderMetadata
    {
        public static string DisplayName(this ProviderType p) => p switch
        {
            ProviderType.Spotify    => "Spotify",
            ProviderType.LocalFiles => "Local",
            _ => p.ToString()
        };

        public static Color BadgeColor(this ProviderType p) => p switch
        {
            ProviderType.Spotify    => Color.Parse("#1DB954"),
            ProviderType.LocalFiles => Color.Parse("#3a6ea5"),
            _ => Colors.Gray
        };

        public static string LinkLabel(this ProviderType p) => $"Link {p.DisplayName()}";
    }
}