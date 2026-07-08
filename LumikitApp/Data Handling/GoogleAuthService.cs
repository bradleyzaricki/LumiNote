using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LumikitApp;

/// <summary>
/// Google sign-in for the shared lightmap library, via the OAuth "installed app"
/// PKCE flow: open the system browser, catch the redirect on a loopback HttpListener,
/// exchange the code for tokens. The refresh token is persisted in the Settings dir so
/// sign-in survives restarts; the short-lived ID token is refreshed on demand and sent
/// to the Worker as a Bearer token (the Worker verifies it against Google's JWKS).
/// </summary>
public class GoogleAuthService
{
    // OAuth 2.0 "Desktop app" client — create at console.cloud.google.com → APIs & Services
    // → Credentials. Loaded from the environment (.env, read by DotNetEnv in Program.Main) so
    // no client secret lives in source; see LumikitApp/.env.example for the required keys.
    private static readonly string ClientId =
        Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? "";
    private static readonly string ClientSecret =
        Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? "";

    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string Scopes = "openid email profile";

    private static readonly HttpClient Http = new();
    private static string StatePath => Path.Combine(DirectoryPaths.SettingsDir, "google_auth.json");

    private AuthState? _state;

    /// <summary>Raised after a successful sign-in or a sign-out.</summary>
    public event Action? SignedInChanged;

    public bool IsSignedIn => _state?.RefreshToken != null;
    public string? UserId => _state?.UserId;
    public string? UserName => _state?.Name;
    public string? Email => _state?.Email;

    public GoogleAuthService()
    {
        try
        {
            if (File.Exists(StatePath))
                _state = JsonSerializer.Deserialize<AuthState>(File.ReadAllText(StatePath));
        }
        catch
        {
            _state = null; // corrupt state file — treated as signed out
        }
    }

    /// <summary>
    /// Interactive sign-in: opens the browser and waits (up to 5 minutes) for the redirect.
    /// Returns true when signed in. Safe to call when already signed in (re-consents).
    /// </summary>
    public async Task<bool> SignInAsync()
    {
        if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
            throw new Exception(
                "Google sign-in isn't configured: set GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET " +
                "in LumikitApp/.env (copy .env.example).");

        // PKCE verifier/challenge pair.
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        // Loopback redirect on a free ephemeral port (installed-app flow allows any port).
        int port = GetFreeTcpPort();
        string redirectUri = $"http://127.0.0.1:{port}/";

        var authUrl = AuthEndpoint +
            $"?client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scopes)}" +
            $"&code_challenge={challenge}" +
            "&code_challenge_method=S256" +
            "&access_type=offline" +      // ask for a refresh token
            "&prompt=consent";            // guarantees the refresh token is (re)issued

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        OpenBrowser(authUrl);

        // Wait for Google to redirect back, with a timeout so an abandoned sign-in
        // doesn't leave the listener running forever.
        var ctxTask = listener.GetContextAsync();
        var done = await Task.WhenAny(ctxTask, Task.Delay(TimeSpan.FromMinutes(5)));
        if (done != ctxTask) { listener.Stop(); return false; }

        var ctx = ctxTask.Result;
        var code = ctx.Request.QueryString["code"];

        // Small response page so the browser tab isn't left hanging.
        var html = Encoding.UTF8.GetBytes(code != null
            ? "<html><body style='font-family:sans-serif'><h3>Signed in — you can return to LumiNote.</h3></body></html>"
            : "<html><body style='font-family:sans-serif'><h3>Sign-in cancelled.</h3></body></html>");
        ctx.Response.ContentType = "text/html";
        ctx.Response.ContentLength64 = html.Length;
        await ctx.Response.OutputStream.WriteAsync(html);
        ctx.Response.Close();
        listener.Stop();

        if (code == null) return false;

        // Exchange the code for tokens.
        var resp = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier
        }));
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new Exception($"Google token exchange failed: {body}");

        using var doc = JsonDocument.Parse(body);
        var idToken = doc.RootElement.GetProperty("id_token").GetString()!;
        var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

        _state = StateFromIdToken(idToken, refreshToken ?? _state?.RefreshToken);
        Persist();
        SignedInChanged?.Invoke();
        return true;
    }

    public void SignOut()
    {
        _state = null;
        try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { }
        SignedInChanged?.Invoke();
    }

    /// <summary>
    /// A currently-valid ID token for API calls, refreshing it if expired.
    /// Null when signed out (or the grant was revoked) — callers should offer SignInAsync.
    /// </summary>
    public async Task<string?> GetIdTokenAsync()
    {
        if (_state == null) return null;

        // Still valid (with a minute of slack)?
        if (_state.IdToken != null && _state.IdTokenExpiryUnix - 60 > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return _state.IdToken;

        if (_state.RefreshToken == null) return null;

        var resp = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = _state.RefreshToken,
            ["grant_type"] = "refresh_token"
        }));
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            // Grant revoked/expired → drop to signed-out so the UI prompts a fresh sign-in.
            if (body.Contains("invalid_grant")) SignOut();
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        var idToken = doc.RootElement.GetProperty("id_token").GetString()!;
        _state = StateFromIdToken(idToken, _state.RefreshToken);
        Persist();
        return idToken;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AuthState StateFromIdToken(string idToken, string? refreshToken)
    {
        // The client only *reads* the claims for display/identity; verification is the
        // Worker's job (it checks the signature against Google's JWKS).
        var payload = idToken.Split('.')[1];
        var json = Encoding.UTF8.GetString(Base64UrlDecode(payload));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new AuthState
        {
            IdToken = idToken,
            RefreshToken = refreshToken,
            IdTokenExpiryUnix = root.TryGetProperty("exp", out var exp) ? exp.GetInt64() : 0,
            UserId = root.GetProperty("sub").GetString(),
            Email = root.TryGetProperty("email", out var em) ? em.GetString() : null,
            Name = root.TryGetProperty("name", out var nm) ? nm.GetString() : null
        };
    }

    private void Persist()
    {
        try { File.WriteAllText(StatePath, JsonSerializer.Serialize(_state)); } catch { }
    }

    private static int GetFreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static void OpenBrowser(string url)
    {
        // Cross-platform default-browser launch (repo targets Win/Mac/Linux).
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", url);
        else
            Process.Start("xdg-open", url);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    private class AuthState
    {
        public string? IdToken { get; set; }
        public string? RefreshToken { get; set; }
        public long IdTokenExpiryUnix { get; set; }
        public string? UserId { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
    }
}
