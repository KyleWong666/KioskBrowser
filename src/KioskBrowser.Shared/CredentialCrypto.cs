using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace KioskBrowser.Shared;

/// <summary>
/// AES-256-GCM 加密，密钥派生自设备硬件指纹（CPU ID + 主板序列号 + MachineGuid）。
/// 配置文件拷贝到其他设备无法解密。
/// </summary>
public static class CredentialCrypto
{
    private static byte[]? _key;

    private static byte[] GetKey()
    {
        if (_key != null) return _key;
        var fp = GetFingerprint();
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("KioskBrowser|" + fp));
        return _key;
    }

    private static string GetFingerprint()
    {
        var cpu = QueryWmi("Win32_Processor", "ProcessorId");
        var board = QueryWmi("Win32_BaseBoard", "SerialNumber");
        var machineGuid = "";
        try
        {
            machineGuid = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "")?.ToString() ?? "";
        }
        catch { /* ignore */ }
        return $"{cpu}|{board}|{machineGuid}";
    }

    private static string QueryWmi(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            foreach (ManagementObject obj in searcher.Get())
                return obj[property]?.ToString() ?? "";
        }
        catch { /* ignore */ }
        return "";
    }

    /// <summary>加密，返回 Base64(nonce[12] + tag[16] + ciphertext)。空输入返回空串。</summary>
    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        using (var aes = new AesGcm(key))
            aes.Encrypt(nonce, data, cipher, tag);
        var blob = new byte[12 + 16 + data.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, 12);
        cipher.CopyTo(blob, 28);
        return Convert.ToBase64String(blob);
    }

    /// <summary>解密。失败（密钥不符/数据损坏）返回空串。</summary>
    public static string Decrypt(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return "";
        try
        {
            var blob = Convert.FromBase64String(base64);
            if (blob.Length < 28) return "";
            var nonce = blob[..12];
            var tag = blob[12..28];
            var cipher = blob[28..];
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(GetKey()))
                aes.Decrypt(nonce, cipher, tag, plain);
            var result = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            return result;
        }
        catch
        {
            return "";
        }
    }
}
