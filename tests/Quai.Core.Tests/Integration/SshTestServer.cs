using Quai.Core.Models;

namespace Quai.Core.Tests.Integration;

/// <summary>
/// Serveur OpenSSH de test démarré par <c>tests/integration/start-sshd.sh</c> et décrit par les variables
/// <c>QUAI_TEST_SSH_*</c>. Sans ces variables (poste de dev, CI Windows), les tests d'intégration sont ignorés.
/// </summary>
internal static class SshTestServer
{
    public static string? Host => Read("QUAI_TEST_SSH_HOST");

    public static int Port => int.Parse(Read("QUAI_TEST_SSH_PORT") ?? "0");

    /// <summary>Même machine, autre clé d'hôte.</summary>
    public static int AlternatePort => int.Parse(Read("QUAI_TEST_SSH_ALT_PORT") ?? "0");

    public static string User => Read("QUAI_TEST_SSH_USER") ?? string.Empty;

    public static string Password => Read("QUAI_TEST_SSH_PASSWORD") ?? string.Empty;

    public static string KeyFile => Read("QUAI_TEST_SSH_KEY") ?? string.Empty;

    public static string RsaKeyFile => Read("QUAI_TEST_SSH_KEY_RSA") ?? string.Empty;

    public static string EncryptedKeyFile => Read("QUAI_TEST_SSH_KEY_ENCRYPTED") ?? string.Empty;

    public static string KeyPassphrase => Read("QUAI_TEST_SSH_KEY_PASSPHRASE") ?? string.Empty;

    public static void SkipUnlessAvailable() =>
        Assert.SkipWhen(string.IsNullOrEmpty(Host), "Serveur SSH de test absent (variables QUAI_TEST_SSH_* non définies).");

    public static ConnectionProfile PasswordProfile(int? port = null) => new()
    {
        Name = "Test (mot de passe)",
        Host = Host ?? string.Empty,
        Port = port ?? Port,
        Username = User,
        Authentication = AuthenticationKind.Password,
    };

    public static ConnectionProfile KeyProfile(string keyFile) => new()
    {
        Name = "Test (clé)",
        Host = Host ?? string.Empty,
        Port = Port,
        Username = User,
        Authentication = AuthenticationKind.PrivateKey,
        PrivateKeyPath = keyFile,
    };

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
