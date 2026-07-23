using System.Security.Cryptography;

namespace HappyPhoton.Services;

public static class AgentAccessToken
{
    private const string Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, 32);

    public static bool IsValid(string? token) =>
        token is { Length: 32 } && token.All(char.IsAsciiLetterOrDigit);
}
