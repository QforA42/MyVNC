using System.Security.Cryptography;

namespace MyVNC.Rfb;

/// <summary>
/// VNC authentication uses DES with a byte-reversed key (RFB 3.8 spec, section 7.2.2).
/// Each byte of the password-derived key has its bits reversed before use as a DES key.
/// </summary>
internal static class VncAuth
{
    public static byte[] EncryptChallenge(string password, byte[] challenge)
    {
        var keyBytes = new byte[8];
        var pwBytes = System.Text.Encoding.Latin1.GetBytes(password);
        for (int i = 0; i < 8; i++)
            keyBytes[i] = i < pwBytes.Length ? ReverseBits(pwBytes[i]) : (byte)0;

        using var des = DES.Create();
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key = keyBytes;

        using var encryptor = des.CreateEncryptor();
        var result = new byte[16];
        encryptor.TransformBlock(challenge, 0, 16, result, 0);
        return result;
    }

    private static byte ReverseBits(byte b)
    {
        byte result = 0;
        for (int i = 0; i < 8; i++)
        {
            result = (byte)((result << 1) | (b & 1));
            b >>= 1;
        }
        return result;
    }
}
