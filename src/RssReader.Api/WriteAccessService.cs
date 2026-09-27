using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RssReader.Api;

public sealed class WriteAccessService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);
    private readonly string? _password;
    private readonly byte[] _signingKey;

    public WriteAccessService(IConfiguration configuration)
    {
        _password = configuration["AccessControl:Password"];
        _signingKey = RandomNumberGenerator.GetBytes(32);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_password);

    public bool VerifyPassword(string password)
    {
        if (!IsConfigured)
            return false;

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(_password!));
        var provided = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    public (string Token, DateTimeOffset ExpiresAt) CreateToken()
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);
        var payload = string.Concat(
            expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ".",
            Base64UrlEncode(RandomNumberGenerator.GetBytes(24)));
        var signature = HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(payload));
        return ($"{payload}.{Base64UrlEncode(signature)}", expiresAt);
    }

    public bool ValidateToken(string? token)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split('.');
        if (parts.Length != 3
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
            || expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return false;

        try
        {
            var payload = $"{parts[0]}.{parts[1]}";
            var expected = HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(payload));
            var provided = Base64UrlDecode(parts[2]);
            return CryptographicOperations.FixedTimeEquals(expected, provided);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
