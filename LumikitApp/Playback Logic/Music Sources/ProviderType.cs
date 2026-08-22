using Avalonia.Media;

namespace LumikitApp
{
    public enum ProviderType
    {
        Spotify,
        LocalFiles
    }

    // Per-provider display metadata. Add a case here when adding a provider — UI is data-driven off it.
    //
    // The picker, the credential setup window and the DI wiring in App.BuildServices all read
    // this and nothing else, so a new source needs: an entry in the enum above, a case in each
    // switch here, and a factory case in App.BuildServices. No UI file changes.
    public static class ProviderMetadata
    {
        /// <summary>
        /// Every provider the app knows about, in picker order. This order also decides which
        /// source starts active, because RoutingMusicSession takes the first enabled pair — so
        /// don't reorder it casually.
        /// </summary>
        public static readonly ProviderType[] All =
        {
            ProviderType.Spotify,
            ProviderType.LocalFiles
        };

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

        public static string IconPath(this ProviderType p) => p switch
        {
            ProviderType.Spotify    => "avares://LumikitApp/Icons/spotify_icon_black.png",
            ProviderType.LocalFiles => "avares://LumikitApp/Icons/file_icon.png",
            _ => ""
        };

        /// <summary>
        /// True when the user must supply their own API credentials before this source can be
        /// used. Spotify requires it: the Developer Terms forbid disclosing our own client id to
        /// third parties, so each user registers their own app and enters its id.
        /// </summary>
        public static bool RequiresUserCredentials(this ProviderType p) => p switch
        {
            ProviderType.Spotify => true,
            _ => false
        };

        /// <summary>
        /// Sources gated behind a user-supplied developer account. Surfaced in the UI so it's
        /// clear the source needs setup and may be subject to the provider's own review.
        /// </summary>
        public static bool IsExperimental(this ProviderType p) => p switch
        {
            ProviderType.Spotify => true,
            _ => false
        };

        /// <summary>Where the user registers their own app to obtain a client id.</summary>
        public static string? CredentialPortalUrl(this ProviderType p) => p switch
        {
            ProviderType.Spotify => "https://developer.spotify.com/dashboard",
            _ => null
        };

        /// <summary>
        /// The redirect URI the app listens on. Must be entered verbatim in the provider's
        /// dashboard. The loopback IP literal is deliberate — providers commonly reject
        /// "localhost" while still allowing 127.0.0.1.
        ///
        /// The port is a high, uncommon one deliberately: 5000 (an earlier choice) is macOS's
        /// default AirPlay Receiver port, so the loopback listener could fail to bind on Macs
        /// out of the box. Spotify requires the URI to match what's registered in the
        /// dashboard verbatim, so — unlike Google's sign-in flow — the port can't just be
        /// picked freely at runtime; it has to be one fixed value baked in here.
        /// </summary>
        public static string? RedirectUri(this ProviderType p) => p switch
        {
            ProviderType.Spotify => "http://127.0.0.1:53219/callback",
            _ => null
        };

        /// <summary>Label for the credential the user pastes in (providers name these differently).</summary>
        public static string CredentialLabel(this ProviderType p) => p switch
        {
            ProviderType.Spotify => "Client ID",
            _ => "API Key"
        };

        /// <summary>Step-by-step setup shown in the credentials window.</summary>
        public static string SetupInstructions(this ProviderType p) => p switch
        {
            ProviderType.Spotify =>
                "Spotify requires each user to run their own developer app.\n\n" +
                "1.  Open the Spotify Developer Dashboard and log in.\n" +
                "2.  Click \"Create app\". Give it any name and description.\n" +
                "3.  Paste the Redirect URI below into the app's Redirect URIs field.\n" +
                "4.  Under APIs used, tick \"Web API\".\n" +
                "5.  Save, then open Settings and copy the Client ID.\n" +
                "6.  Paste the Client ID below.\n\n" +
                "Controlling Spotify playback requires a Spotify Premium account.",
            _ => ""
        };
    }
}
