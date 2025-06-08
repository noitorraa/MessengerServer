using System.Security.Cryptography;
using System.Text;

public interface IEncryptionService
{
    string EncryptDeterministic(string plainText);
    string DecryptDeterministic(string cipherText);
}

public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private readonly int _blockSizeBytes;

    public AesEncryptionService(IConfiguration config)
    {
        // Загружаем ключ из конфига (Base64)
        var keyBase64 = config["AES:Key"];
        if (string.IsNullOrEmpty(keyBase64))
            throw new ArgumentException("AES:Key is missing in configuration.");
        _key = Convert.FromBase64String(keyBase64);

        using var aes = Aes.Create();
        _blockSizeBytes = aes.BlockSize / 8;
    }

    /// <summary>
    /// Детерминированное шифрование: IV = первые _blockSizeBytes байт SHA-256(plainText).
    /// Результат — Base64 от чистого шифротекста (без IV в префиксе).
    /// </summary>
      public string EncryptDeterministic(string plainText)
    {
        if (plainText is null) throw new ArgumentNullException(nameof(plainText));

        byte[] iv = ComputeIv(plainText);
        
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = iv;
        aes.Padding = PaddingMode.PKCS7; // Явно укажите паддинг

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        // Шифруем только данные, без записи IV
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plainText);
        }
        
        return Convert.ToBase64String(iv.Concat(ms.ToArray()).ToArray());
    }

    public string DecryptDeterministic(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return string.Empty;

        try
        {
            var fullCipher = Convert.FromBase64String(cipherText);
            
            // Первые _blockSizeBytes - это IV
            var iv = fullCipher.Take(_blockSizeBytes).ToArray();
            var cipherData = fullCipher.Skip(_blockSizeBytes).ToArray();

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = iv;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(cipherData);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            
            return sr.ReadToEnd();
        }
        catch
        {
            return string.Empty; // Возвращаем пустую строку при ошибке
        }
    }

    private byte[] ComputeIv(string plainText)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(plainText))
                  .Take(_blockSizeBytes)
                  .ToArray();
    }
}
