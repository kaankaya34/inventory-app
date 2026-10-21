namespace StockEngine.Api.Security;

// Gizli anahtarları sırayla şuradan okur: yapılandırma/ortam değişkeni (base64) -> App_Data dosyası.
// Dosya yoksa güçlü rastgele bir anahtar üretip App_Data'ya yazar (geliştirme kolaylığı; App_Data git'e eklenmez).
// Canlıda anahtarlar mutlaka ortam değişkeni / secret manager ile verilmelidir.
public static class SecretProvider
{
    public static byte[] GetOrCreateKey(
        IConfiguration config, string configKey, int length, string fileName, string contentRoot, ILogger logger)
    {
        var configured = config[configKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var bytes = Convert.FromBase64String(configured);
            if (bytes.Length < length)
                throw new InvalidOperationException($"{configKey} en az {length} bayt (base64) olmalıdır.");
            return bytes;
        }

        var dir = Path.Combine(contentRoot, "App_Data");
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
            return Convert.FromBase64String(File.ReadAllText(path).Trim());

        Directory.CreateDirectory(dir);
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
        File.WriteAllText(path, Convert.ToBase64String(key));
        logger.LogWarning("{ConfigKey} tanımlı değil; geliştirme anahtarı oluşturuldu: {Path}. Canlıda ortam değişkeni kullanın.", configKey, path);
        return key;
    }
}
