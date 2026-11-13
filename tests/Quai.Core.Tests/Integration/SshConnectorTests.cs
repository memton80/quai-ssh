using Quai.Core.Security;
using Quai.Core.Ssh;
using Quai.Core.Tests.Support;
using Renci.SshNet;

namespace Quai.Core.Tests.Integration;

/// <summary>Connexions réelles vers le serveur OpenSSH de test.</summary>
public sealed class SshConnectorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly KnownHostsStore _knownHosts;
    private readonly InMemorySecretStore _secrets = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly SshConnector _connector;

    public SshConnectorTests()
    {
        _knownHosts = new KnownHostsStore(_temp.File("known_hosts"));
        _connector = new SshConnector(_knownHosts, _secrets, _prompts, _prompts);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Timeout => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private async Task<string> RunAsync(Quai.Core.Models.ConnectionProfile profile, string command)
    {
        using var client = await _connector.ConnectAsync(profile, info => new SshClient(info), Timeout);
        using var result = client.RunCommand(command);
        return result.Result.Trim();
    }

    // ---- Clé d'hôte --------------------------------------------------------------------------

    [Fact]
    public async Task First_contact_asks_the_user_and_remembers_the_host_key()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile();
        _secrets.Set(SecretKeys.Password(profile.Id), SshTestServer.Password);

        Assert.Equal(SshTestServer.User, await RunAsync(profile, "id -un"));

        var asked = Assert.Single(_prompts.UnknownHostRequests);
        Assert.StartsWith("SHA256:", asked.Fingerprint);
        Assert.Equal("ssh-ed25519", asked.KeyType);
        Assert.Equal(HostKeyStatus.Trusted, _knownHosts.Check(asked));

        // Deuxième connexion : plus aucune question.
        Assert.Equal(SshTestServer.User, await RunAsync(profile, "id -un"));
        Assert.Single(_prompts.UnknownHostRequests);
    }

    [Fact]
    public async Task Declining_an_unknown_host_aborts_before_authentication()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile();
        _prompts.AcceptUnknownHost = false;

        var error = await Assert.ThrowsAsync<HostKeyRejectedException>(() => RunAsync(profile, "id -un"));

        Assert.Equal(HostKeyStatus.Unknown, error.Status);
        Assert.Empty(_knownHosts.Find(profile.Host, profile.Port));
        Assert.Empty(_prompts.SecretRequests);
    }

    [Fact]
    public async Task A_changed_host_key_raises_the_alert_and_is_refused_by_default()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile();
        _secrets.Set(SecretKeys.Password(profile.Id), SshTestServer.Password);
        var impostor = await TrustAlternateServerKeyAsAsync(profile.Host, profile.Port);

        var error = await Assert.ThrowsAsync<HostKeyRejectedException>(() => RunAsync(profile, "id -un"));

        Assert.Equal(HostKeyStatus.Changed, error.Status);
        var (presented, known) = Assert.Single(_prompts.ChangedHostRequests);
        Assert.NotEqual(impostor.Fingerprint, presented.Fingerprint);
        Assert.Equal(impostor.Fingerprint, Assert.Single(known).Fingerprint);

        // La clé enregistrée n'a pas été touchée.
        Assert.Equal(impostor.Fingerprint, Assert.Single(_knownHosts.Find(profile.Host, profile.Port)).Fingerprint);
    }

    [Fact]
    public async Task A_changed_host_key_can_be_replaced_only_by_an_explicit_choice()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile();
        _secrets.Set(SecretKeys.Password(profile.Id), SshTestServer.Password);
        await TrustAlternateServerKeyAsAsync(profile.Host, profile.Port);
        _prompts.AcceptChangedHost = true;

        Assert.Equal(SshTestServer.User, await RunAsync(profile, "id -un"));

        var presented = Assert.Single(_prompts.ChangedHostRequests).Presented;
        Assert.Equal(presented.Fingerprint, Assert.Single(_knownHosts.Find(profile.Host, profile.Port)).Fingerprint);
    }

    /// <summary>Enregistre, pour l'hôte testé, la clé du second serveur : simule un changement d'empreinte.</summary>
    private async Task<HostKeyInfo> TrustAlternateServerKeyAsAsync(string host, int port)
    {
        var alternate = SshTestServer.PasswordProfile(SshTestServer.AlternatePort);
        _secrets.Set(SecretKeys.Password(alternate.Id), SshTestServer.Password);
        await RunAsync(alternate, "true");

        var other = Assert.Single(_knownHosts.Find(host, SshTestServer.AlternatePort));
        var impostor = other with { Port = port };
        _knownHosts.Trust(impostor);
        _prompts.UnknownHostRequests.Clear();
        return impostor;
    }

    // ---- Mot de passe ------------------------------------------------------------------------

    [Fact]
    public async Task A_missing_password_is_asked_and_stored_only_if_requested()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile();
        _prompts.Secrets.Enqueue(new SecretInput(SshTestServer.Password, Remember: true));

        Assert.Equal(SshTestServer.User, await RunAsync(profile, "id -un"));

        Assert.Equal([(SecretKind.Password, false)], _prompts.SecretRequests);
        Assert.Equal(SshTestServer.Password, _secrets.Items[SecretKeys.Password(profile.Id)]);
    }

    [Fact]
    public async Task A_password_typed_without_remember_never_reaches_the_vault()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile();
        _prompts.Secrets.Enqueue(new SecretInput(SshTestServer.Password, Remember: false));

        await RunAsync(profile, "true");

        Assert.Empty(_secrets.Items);
    }

    [Fact]
    public async Task A_wrong_stored_password_triggers_a_retry_prompt()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile();
        _secrets.Set(SecretKeys.Password(profile.Id), "mauvais-mot-de-passe");
        _prompts.Secrets.Enqueue(new SecretInput(SshTestServer.Password, Remember: true));

        Assert.Equal(SshTestServer.User, await RunAsync(profile, "id -un"));

        Assert.Equal([(SecretKind.Password, true)], _prompts.SecretRequests);
        Assert.Equal(SshTestServer.Password, _secrets.Items[SecretKeys.Password(profile.Id)]);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_end_with_a_login_failure()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile();
        for (var i = 0; i < 5; i++)
        {
            _prompts.Secrets.Enqueue(new SecretInput($"faux-{i}", Remember: true));
        }

        await Assert.ThrowsAsync<SshLoginFailedException>(() => RunAsync(profile, "true"));

        Assert.Equal(3, _prompts.SecretRequests.Count);
        Assert.Empty(_secrets.Items);
    }

    [Fact]
    public async Task Cancelling_the_password_prompt_cancels_the_connection()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile();
        _prompts.Secrets.Enqueue(null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(profile, "true"));
    }

    // ---- Clé privée --------------------------------------------------------------------------

    [Fact]
    public async Task An_unencrypted_ed25519_key_authenticates_without_any_prompt()
    {
        SshTestServer.SkipUnlessAvailable();

        Assert.Equal(SshTestServer.User, await RunAsync(SshTestServer.KeyProfile(SshTestServer.KeyFile), "id -un"));
        Assert.Empty(_prompts.SecretRequests);
    }

    [Fact]
    public async Task An_rsa_key_authenticates()
    {
        SshTestServer.SkipUnlessAvailable();

        Assert.Equal(SshTestServer.User, await RunAsync(SshTestServer.KeyProfile(SshTestServer.RsaKeyFile), "id -un"));
    }

    [Fact]
    public async Task An_encrypted_key_asks_for_its_passphrase_and_can_store_it()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.KeyProfile(SshTestServer.EncryptedKeyFile);
        _prompts.Secrets.Enqueue(new SecretInput(SshTestServer.KeyPassphrase, Remember: true));

        Assert.Equal(SshTestServer.User, await RunAsync(profile, "id -un"));

        Assert.Equal([(SecretKind.Passphrase, false)], _prompts.SecretRequests);
        Assert.Equal(SshTestServer.KeyPassphrase, _secrets.Items[SecretKeys.Passphrase(profile.Id)]);

        // Avec la passphrase dans le coffre : plus de question.
        Assert.Equal(SshTestServer.User, await RunAsync(profile, "id -un"));
        Assert.Single(_prompts.SecretRequests);
    }

    [Fact]
    public async Task A_wrong_passphrase_is_asked_again()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.KeyProfile(SshTestServer.EncryptedKeyFile);
        _prompts.Secrets.Enqueue(new SecretInput("mauvaise-passphrase", Remember: false));
        _prompts.Secrets.Enqueue(new SecretInput(SshTestServer.KeyPassphrase, Remember: false));

        Assert.Equal(SshTestServer.User, await RunAsync(profile, "id -un"));

        Assert.Equal([(SecretKind.Passphrase, false), (SecretKind.Passphrase, true)], _prompts.SecretRequests);
        Assert.Empty(_secrets.Items);
    }

    [Fact]
    public async Task A_missing_key_file_is_reported_clearly()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.KeyProfile(_temp.File("absente"));

        var error = await Assert.ThrowsAsync<PrivateKeyUnavailableException>(() => RunAsync(profile, "true"));

        Assert.Equal(profile.PrivateKeyPath, error.Path);
    }

    [Fact]
    public async Task A_key_the_server_does_not_know_fails_the_login()
    {
        SshTestServer.SkipUnlessAvailable();
        var stranger = _temp.File("inconnue");
        using (var generator = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ssh-keygen", ["-q", "-t", "ed25519", "-N", string.Empty, "-f", stranger])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!)
        {
            await generator.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<SshLoginFailedException>(() => RunAsync(SshTestServer.KeyProfile(stranger), "true"));
    }

    // ---- Erreurs réseau ----------------------------------------------------------------------

    [Fact]
    public async Task A_closed_port_fails_without_asking_anything()
    {
        SshTestServer.SkipUnlessAvailable();
        var profile = SshTestServer.PasswordProfile(port: 1);
        _secrets.Set(SecretKeys.Password(profile.Id), SshTestServer.Password);

        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(profile, "true"));

        Assert.Empty(_prompts.UnknownHostRequests);
        Assert.Empty(_prompts.SecretRequests);
    }
}
