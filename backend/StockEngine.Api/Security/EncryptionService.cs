using System.Security.Cryptography;
using System.Text;

namespace StockEngine.Api.Security;

public interface IEncryptionService
{
    string Encrypt(string plaintext, string purpose);
    string Decrypt(string payload, string purpose);
    // Aynı değeri bulabilmek için (ör. aynı kartın tekrar kaydedilmesini engelleme) deterministik anahtarlı özet.
    string Fingerprint(string value);
}

/// <summary>
/// AES-256-GCM ile alan düzeyinde şifreleme. Biçim: "v1." + base64(nonce[12] | tag[16] | ciphertext).
/// `purpose` ilişkili veri (AAD) olarak bağlanır; bir kolonun şifreli değeri başka bir kolona taşınırsa çözülemez.
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const string Prefix = "v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;
    private readonly byte[] _fingerprintKey;

    public AesGcmEncryptionService(byte[] masterKey)
    {
        if (masterKey.Length < 32) throw new ArgumentException("Anahtar en az 32 bayt olmalı.", nameof(masterKey));
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: Encoding.UTF8.GetBytes("field-encryption"));
        _fingerprintKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: Encoding.UTF8.GetBytes("fingerprint"));
    }

    public string Encrypt(string plaintext, string purpose)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(purpose));

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceSize);
        cipher.CopyTo(blob, NonceSize + TagSize);
        return Prefix + Convert.ToBase64String(blob);
    }

    public string Decrypt(string payload, string purpose)
    {
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Bilinmeyen şifreli veri biçimi.");

        var blob = Convert.FromBase64String(payload[Prefix.Length..]);
        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("Şifreli veri bozuk.");

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(purpose));
        return Encoding.UTF8.GetString(plain);
    }

    public string Fingerprint(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(_fingerprintKey, Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
