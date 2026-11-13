namespace Quai.Core.Security;

/// <summary>Noms des secrets rattachés à une connexion.</summary>
public static class SecretKeys
{
    public static string Password(Guid connectionId) => $"connection/{connectionId:N}/password";

    public static string Passphrase(Guid connectionId) => $"connection/{connectionId:N}/passphrase";
}
