using System.Security.Cryptography;
using System.Text;

namespace Otp.Application.Services;

public static class PhoneUtil
{
    public static string Mask(string? e164) =>
        string.IsNullOrEmpty(e164) || e164.Length < 6 ? "***" : $"{e164[..2]}•••••{e164[^4..]}";

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool IsE164(string? v) =>
        v is { Length: >= 8 and <= 16 } && v[0] == '+' && v.Skip(1).All(char.IsAsciiDigit) && v[1] != '0';
}
