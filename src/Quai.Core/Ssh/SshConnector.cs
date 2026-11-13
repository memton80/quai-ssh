using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quai.Core.Models;
using Quai.Core.Security;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Quai.Core.Ssh;

/// <summary>
/// Établit une connexion SSH.NET pour une entrée du carnet : secrets du coffre ou saisis,
/// vérification systématique de la clé d'hôte (confiance au premier contact, alerte si elle change).
/// </summary>
public sealed class SshConnector
{
    private const int MaxAuthenticationAttempts = 3;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(30);

    private readonly IKnownHostsStore _knownHosts;
    private readonly ISecretStore _secrets;
    private readonly IHostKeyPrompt _hostKeyPrompt;
    private readonly ICredentialPrompt _credentialPrompt;
    private readonly ILogger _logger;

    public SshConnector(
        IKnownHostsStore knownHosts,
        ISecretStore secrets,
        IHostKeyPrompt hostKeyPrompt,
        ICredentialPrompt credentialPrompt,
        ILogger<SshConnector>? logger = null)
    {
        _knownHosts = knownHosts;
        _secrets = secrets;
        _hostKeyPrompt = hostKeyPrompt;
        _credentialPrompt = credentialPrompt;
        _logger = logger ?? NullLogger<SshConnector>.Instance;
    }

    /// <summary>
    /// Connecte un client SSH.NET (<see cref="SshClient"/>, <see cref="SftpClient"/>…).
    /// </summary>
    /// <exception cref="OperationCanceledException">L'utilisateur a annulé une saisie.</exception>
    /// <exception cref="HostKeyRejectedException">La clé d'hôte n'a pas été acceptée.</exception>
    /// <exception cref="SshLoginFailedException">Le serveur a refusé l'authentification.</exception>
    public async Task<TClient> ConnectAsync<TClient>(
        ConnectionProfile profile,
        Func<ConnectionInfo, TClient> create,
        CancellationToken cancellationToken = default)
        where TClient : BaseClient
    {
        ArgumentNullException.ThrowIfNull(profile);

        var usesKey = profile.Authentication == AuthenticationKind.PrivateKey;
        var secretKind = usesKey ? SecretKind.Passphrase : SecretKind.Password;
        var secretKey = usesKey ? SecretKeys.Passphrase(profile.Id) : SecretKeys.Password(profile.Id);

        var storedSecret = ReadSecret(secretKey);
        var secret = storedSecret;
        SecretInput? typed = null;
        var askSecret = !usesKey && string.IsNullOrEmpty(secret);
        var isRetry = false;
        var hostVerified = false;
        var failures = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (askSecret && !usesKey && !hostVerified)
            {
                // Sonde sans authentification : la clé d'hôte est vérifiée avant de demander le mot de passe.
                var probe = await AttemptAsync(profile, create, BuildProbeInfo(profile), cancellationToken);
                if (probe.Outcome == AttemptOutcome.Connected)
                {
                    return probe.Client!;
                }

                if (probe.Outcome == AttemptOutcome.HostKeyResolved)
                {
                    continue;
                }

                hostVerified = true;
            }

            if (askSecret)
            {
                typed = await _credentialPrompt.PromptSecretAsync(profile, secretKind, isRetry)
                    ?? throw new OperationCanceledException("Saisie annulée par l'utilisateur.");
                secret = typed.Secret;
                askSecret = false;
            }

            PrivateKeyFile? keyFile = null;
            if (usesKey)
            {
                var loaded = LoadPrivateKey(profile, secret);
                if (loaded.NeedsPassphrase)
                {
                    // Clé chiffrée : passphrase absente ou erronée (elle ne quitte jamais cette machine).
                    isRetry = !string.IsNullOrEmpty(secret);
                    if (isRetry && ++failures >= MaxAuthenticationAttempts)
                    {
                        throw new SshLoginFailedException("Passphrase de la clé privée refusée.");
                    }

                    askSecret = true;
                    continue;
                }

                keyFile = loaded.Key;
            }

            Attempt<TClient> attempt;
            try
            {
                attempt = await AttemptAsync(profile, create, BuildConnectionInfo(profile, secret, keyFile), cancellationToken);
            }
            catch
            {
                keyFile?.Dispose();
                throw;
            }

            switch (attempt.Outcome)
            {
                case AttemptOutcome.Connected:
                    RememberSecret(secretKey, typed, storedSecret);
                    _logger.LogInformation("Connecté à {Endpoint}", profile.Endpoint);
                    return attempt.Client!;

                case AttemptOutcome.HostKeyResolved:
                    keyFile?.Dispose();
                    continue;

                default:
                    keyFile?.Dispose();
                    failures++;
                    _logger.LogWarning("Authentification refusée pour {Endpoint} (tentative {Attempt})", profile.Endpoint, failures);
                    if (usesKey || failures >= MaxAuthenticationAttempts)
                    {
                        throw new SshLoginFailedException(attempt.Error?.Message ?? "Authentification refusée.", attempt.Error);
                    }

                    hostVerified = true;
                    isRetry = true;
                    askSecret = true;
                    continue;
            }
        }
    }

    private enum AttemptOutcome
    {
        Connected,

        /// <summary>La clé d'hôte n'était pas de confiance ; l'utilisateur vient de l'accepter : il faut se reconnecter.</summary>
        HostKeyResolved,

        AuthenticationFailed,
    }

    private readonly record struct Attempt<TClient>(AttemptOutcome Outcome, TClient? Client, Exception? Error)
        where TClient : BaseClient;

    /// <summary>
    /// Une tentative de connexion. La clé d'hôte est toujours comparée aux hôtes connus ; si elle n'est pas
    /// de confiance, la connexion est coupée avant toute authentification (aucun secret n'est envoyé).
    /// </summary>
    private async Task<Attempt<TClient>> AttemptAsync<TClient>(
        ConnectionProfile profile,
        Func<ConnectionInfo, TClient> create,
        ConnectionInfo connectionInfo,
        CancellationToken cancellationToken)
        where TClient : BaseClient
    {
        var client = create(connectionInfo);
        client.KeepAliveInterval = KeepAlive;

        HostKeyInfo? presented = null;
        var status = HostKeyStatus.Trusted;
        client.HostKeyReceived += (_, e) =>
        {
            // Appelé sur le fil réseau de SSH.NET : on ne fait que comparer avec les hôtes connus.
            presented = new HostKeyInfo(profile.Host, profile.Port, e.HostKeyName, e.HostKey);
            status = _knownHosts.Check(presented);
            e.CanTrust = status == HostKeyStatus.Trusted;
        };

        try
        {
            await Task.Run(() => client.ConnectAsync(cancellationToken), cancellationToken);
            return new Attempt<TClient>(AttemptOutcome.Connected, client, null);
        }
        catch (Exception ex)
        {
            client.Dispose();

            if (presented is not null && status != HostKeyStatus.Trusted)
            {
                await ResolveHostKeyAsync(presented, status);
                return new Attempt<TClient>(AttemptOutcome.HostKeyResolved, null, null);
            }

            if (ex is SshAuthenticationException)
            {
                return new Attempt<TClient>(AttemptOutcome.AuthenticationFailed, null, ex);
            }

            if (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Connexion à {Endpoint} impossible ({Error})", profile.Endpoint, ex.GetType().Name);
            }

            throw;
        }
    }

    private static ConnectionInfo BuildProbeInfo(ConnectionProfile profile) =>
        new(profile.Host, profile.Port, profile.Username, new NoneAuthenticationMethod(profile.Username))
        {
            Timeout = ConnectTimeout,
        };

    private static ConnectionInfo BuildConnectionInfo(ConnectionProfile profile, string? secret, PrivateKeyFile? keyFile)
    {
        var methods = new List<AuthenticationMethod>();
        if (keyFile is not null)
        {
            methods.Add(new PrivateKeyAuthenticationMethod(profile.Username, keyFile));
        }
        else
        {
            var password = secret ?? string.Empty;
            methods.Add(new PasswordAuthenticationMethod(profile.Username, password));

            // Beaucoup de serveurs (PAM) n'acceptent le mot de passe qu'en « keyboard-interactive ».
            var interactive = new KeyboardInteractiveAuthenticationMethod(profile.Username);
            interactive.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    if (prompt.Request.Contains("password", StringComparison.OrdinalIgnoreCase)
                        || prompt.Request.Contains("mot de passe", StringComparison.OrdinalIgnoreCase))
                    {
                        prompt.Response = password;
                    }
                }
            };
            methods.Add(interactive);
        }

        return new ConnectionInfo(profile.Host, profile.Port, profile.Username, [.. methods])
        {
            Timeout = ConnectTimeout,
        };
    }

    private static (PrivateKeyFile? Key, bool NeedsPassphrase) LoadPrivateKey(ConnectionProfile profile, string? passphrase)
    {
        var path = profile.PrivateKeyPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new PrivateKeyUnavailableException(path ?? string.Empty);
        }

        try
        {
            return (string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(path) : new PrivateKeyFile(path, passphrase), false);
        }
        catch (SshPassPhraseNullOrEmptyException)
        {
            return (null, true);
        }
        catch (Exception ex) when (!string.IsNullOrEmpty(passphrase) && ex is SshException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            // Avec une passphrase fournie, l'échec de déchiffrement signifie qu'elle est erronée.
            return (null, true);
        }
        catch (Exception ex) when (ex is SshException or IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
        {
            throw new PrivateKeyUnavailableException(path, ex);
        }
    }

    private async Task ResolveHostKeyAsync(HostKeyInfo presented, HostKeyStatus status)
    {
        bool accepted;
        if (status == HostKeyStatus.Unknown)
        {
            accepted = await _hostKeyPrompt.ConfirmUnknownHostAsync(presented);
        }
        else
        {
            _logger.LogWarning("La clé d'hôte de {Endpoint} a changé ({Fingerprint})", presented.Endpoint, presented.Fingerprint);
            accepted = await _hostKeyPrompt.ConfirmChangedHostAsync(presented, _knownHosts.Find(presented.Host, presented.Port));
        }

        if (!accepted)
        {
            throw new HostKeyRejectedException(presented, status);
        }

        _knownHosts.Trust(presented);
        _logger.LogInformation("Clé d'hôte enregistrée pour {Endpoint} ({Fingerprint})", presented.Endpoint, presented.Fingerprint);
    }

    private string? ReadSecret(string key)
    {
        try
        {
            return _secrets.Get(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Lecture du coffre impossible ({Error})", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Après une connexion réussie : mémorise le secret saisi, ou retire du coffre celui qui a été refusé.</summary>
    private void RememberSecret(string key, SecretInput? typed, string? stored)
    {
        if (typed is null)
        {
            return;
        }

        try
        {
            if (typed.Remember)
            {
                _secrets.Set(key, typed.Secret);
            }
            else if (!string.IsNullOrEmpty(stored) && stored != typed.Secret)
            {
                _secrets.Remove(key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Écriture dans le coffre impossible ({Error})", ex.GetType().Name);
        }
    }
}
