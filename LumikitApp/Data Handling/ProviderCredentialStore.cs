using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LumikitApp
{
    /// <summary>Credentials the user supplied for one music source.</summary>
    public sealed class ProviderCredentials
    {
        /// <summary>The user's own client id / API key for this provider.</summary>
        public string ClientId { get; set; } = "";

        public bool IsComplete => !string.IsNullOrWhiteSpace(ClientId);
    }

    /// <summary>
    /// Per-provider API credentials supplied by the user, persisted to
    /// <c>Settings/provider_credentials.json</c>.
    ///
    /// LumiNote ships no client id of its own. Spotify's Developer Terms forbid disclosing our
    /// Security Codes to third parties, and a client id hardcoded into a distributed desktop app
    /// is exactly that — so each user registers their own developer app and enters its id here.
    /// This also sidesteps the shared quota entirely: every install is its own app.
    ///
    /// Keyed by <see cref="ProviderType"/> name rather than ordinal so reordering the enum can't
    /// silently repoint someone's saved credentials at the wrong provider.
    /// </summary>
    public sealed class ProviderCredentialStore
    {
        private static string StorePath =>
            Path.Combine(DirectoryPaths.SettingsDir, "provider_credentials.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private Dictionary<string, ProviderCredentials> _entries = new();

        /// <summary>Raised after any provider's credentials are saved or cleared.</summary>
        public event Action? CredentialsChanged;

        public ProviderCredentialStore() => Load();

        private void Load()
        {
            try
            {
                if (!File.Exists(StorePath)) return;
                _entries = JsonSerializer.Deserialize<Dictionary<string, ProviderCredentials>>(
                               File.ReadAllText(StorePath)) ?? new();
            }
            catch
            {
                // A corrupt store must not block startup — the user is prompted to set up again.
                _entries = new();
            }
        }

        private void Persist()
        {
            try
            {
                Directory.CreateDirectory(DirectoryPaths.SettingsDir);

                // Write to a temp file and swap it in, so a crash mid-save can't leave a
                // truncated store that Load() then has to discard wholesale.
                var tempPath = StorePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(_entries, JsonOptions));
                if (File.Exists(StorePath))
                    File.Replace(tempPath, StorePath, null);
                else
                    File.Move(tempPath, StorePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not save provider credentials: {ex.Message}");
            }
        }

        /// <summary>True when this provider is ready to use: either it needs no credentials, or complete ones are stored.</summary>
        public bool IsConfigured(ProviderType provider) =>
            !provider.RequiresUserCredentials() || Get(provider)?.IsComplete == true;

        public ProviderCredentials? Get(ProviderType provider) =>
            _entries.GetValueOrDefault(provider.ToString());

        public void Save(ProviderType provider, ProviderCredentials credentials)
        {
            _entries[provider.ToString()] = credentials;
            Persist();
            CredentialsChanged?.Invoke();
        }

        public void Clear(ProviderType provider)
        {
            if (!_entries.Remove(provider.ToString())) return;
            Persist();
            CredentialsChanged?.Invoke();
        }
    }
}
