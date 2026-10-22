using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace NguyenDuongNhutKhang_app.Services;

/// <summary>Sinh RTC token Agora định dạng AccessToken2 ("007") theo đặc tả công khai của Agora.</summary>
public static class AgoraTokenBuilder
{
    private const ushort ServiceRtc = 1;

    public static string BuildRtcToken(string appId, string appCertificate, string channel, uint uid, uint ttlSeconds)
    {
        var issueTs = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var salt = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

        var signing = Hmac(BitConverter.GetBytes(salt),
            Hmac(BitConverter.GetBytes(issueTs), Encoding.UTF8.GetBytes(appCertificate)));

        using var info = new MemoryStream();
        PutString(info, Encoding.UTF8.GetBytes(appId));
        PutU32(info, issueTs);
        PutU32(info, ttlSeconds);
        PutU32(info, salt);
        PutU16(info, 1); // số service
        PutU16(info, ServiceRtc);
        // 1 join channel, 2 publish audio, 3 publish video, 4 publish data stream
        ushort[] privileges = [1, 2, 3, 4];
        PutU16(info, (ushort)privileges.Length);
        foreach (var k in privileges) { PutU16(info, k); PutU32(info, ttlSeconds); }
        PutString(info, Encoding.UTF8.GetBytes(channel));
        PutString(info, Encoding.UTF8.GetBytes(uid == 0 ? "" : uid.ToString()));
        var infoBytes = info.ToArray();

        var signature = Hmac(signing, infoBytes);
        using var content = new MemoryStream();
        PutString(content, signature);
        content.Write(infoBytes);

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) z.Write(content.ToArray());
        return "007" + Convert.ToBase64String(compressed.ToArray());
    }

    private static byte[] Hmac(byte[] key, byte[] data) => HMACSHA256.HashData(key, data);
    private static void PutU16(Stream s, ushort v) => s.Write(BitConverter.GetBytes(v));
    private static void PutU32(Stream s, uint v) => s.Write(BitConverter.GetBytes(v));
    private static void PutString(Stream s, byte[] b) { PutU16(s, (ushort)b.Length); s.Write(b); }
}
