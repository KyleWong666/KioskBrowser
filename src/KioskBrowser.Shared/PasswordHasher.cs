using System.Security.Cryptography;
using System.Text;

namespace KioskBrowser.Shared;

/// <summary>设置页入口密码：SHA256(salt + password) 哈希存储，不可逆。</summary>
public static class PasswordHasher
{
    public static string Hash(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("kiosk|" + password)))
            .ToLowerInvariant();

    public static bool Verify(string password, string hash) =>
        !string.IsNullOrEmpty(hash) && Hash(password) == hash;
}
