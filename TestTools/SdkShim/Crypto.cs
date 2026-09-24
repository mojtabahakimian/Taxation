// ============================================================================
//  شیم TaxCollectData.Library 0.0.34 — فقط برای اجرای هارنس تست روی لینوکس.
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using TaxCollectData.Library.Dto.Content;

namespace TaxCollectData.Library.Business
{
    internal static class Crypto
    {
        public sealed record Encrypted(string Data, string SymmetricKey, string Iv);

        /// <summary>
        /// مطابق moadian_mock.py (DefaultEncryptor / PacketCodec در SDK):
        ///   aesKey       = ۳۲ بایت تصادفی
        ///   symmetricKey = base64(RSA-OAEP-SHA256(hexUpper(aesKey)))
        ///   iv           = hexUpper(۱۶ بایت تصادفی)
        ///   data         = base64(AES-GCM(xor(json, aesKey), aesKey, iv, tag=128))
        /// AesGcm دات‌نت فقط nonce دوازده‌بایتی می‌پذیرد، پس GCM با BouncyCastle است.
        /// </summary>
        public static Encrypted EncryptPacketData(byte[] plain, RSA serverKey)
        {
            byte[] aesKey = RandomNumberGenerator.GetBytes(32);
            byte[] iv = RandomNumberGenerator.GetBytes(16);

            byte[] xored = Xor(plain, aesKey);

            var gcm = new GcmBlockCipher(new AesEngine());
            gcm.Init(true, new AeadParameters(new KeyParameter(aesKey), 128, iv));
            byte[] outBuf = new byte[gcm.GetOutputSize(xored.Length)];
            int len = gcm.ProcessBytes(xored, 0, xored.Length, outBuf, 0);
            len += gcm.DoFinal(outBuf, len);
            if (len != outBuf.Length) Array.Resize(ref outBuf, len);

            byte[] keyHex = Encoding.UTF8.GetBytes(Convert.ToHexString(aesKey));
            byte[] encKey = serverKey.Encrypt(keyHex, RSAEncryptionPadding.OaepSHA256);

            return new Encrypted(Convert.ToBase64String(outBuf), Convert.ToBase64String(encKey),
                                 Convert.ToHexString(iv));
        }

        /// <summary>همان PacketCodec.Xor: آرایهٔ کوتاه‌تر روی بلندتر تکرار می‌شود.</summary>
        public static byte[] Xor(byte[] a, byte[] b)
        {
            byte[] small = a.Length < b.Length ? a : b;
            byte[] big = a.Length < b.Length ? b : a;
            var outp = new byte[big.Length];
            for (int i = 0; i < big.Length; i++) outp[i] = (byte)(small[i % small.Length] ^ big[i]);
            return outp;
        }

        public static RSA LoadPublicKey(string b64)
        {
            var rsa = RSA.Create();
            string s = b64.Trim();
            if (s.StartsWith("-----")) rsa.ImportFromPem(s);
            else rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(s), out _);
            return rsa;
        }

        public static RSA LoadPrivateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var rsa = RSA.Create();
            string s = key.Trim();
            if (s.Contains("-----BEGIN")) { rsa.ImportFromPem(s); return rsa; }
            byte[] der = Convert.FromBase64String(string.Concat(s.Where(c => !char.IsWhiteSpace(c))));
            try { rsa.ImportPkcs8PrivateKey(der, out _); }
            catch (CryptographicException) { rsa.ImportRSAPrivateKey(der, out _); }
            return rsa;
        }
    }

    /// <summary>
    /// JSON صورتحساب روی سیم: کلیدهای camelCase، nullها حاضر (مثل golden/*.json)،
    /// اعداد decimal بدون «.0» اضافه (golden: "am": 3).
    /// </summary>
    internal static class InvoiceJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };

        public static string Serialize(InvoiceDto dto) => JsonSerializer.Serialize(dto, Options);
    }
}
