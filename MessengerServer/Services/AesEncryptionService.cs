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

            // 1) считаем IV = SHA256(plainText).Take(blockSizeBytes)
            byte[] iv = ComputeIv(plainText);

            // 2) шифруем
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV  = iv;

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            // сначала записываем IV в префикс
            ms.Write(iv, 0, iv.Length);

            using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
            using var sw = new StreamWriter(cs, Encoding.UTF8);
            sw.Write(plainText);
            sw.Flush();
            cs.FlushFinalBlock();

            return Convert.ToBase64String(ms.ToArray());
        }

    /// <summary>
    /// Детерминированное расшифрование: тот же IV, что и в EncryptDeterministic.
    /// </summary>
        public string DecryptDeterministic(string cipherText)
        {
            if (cipherText is null) throw new ArgumentNullException(nameof(cipherText));

            var fullCipher = Convert.FromBase64String(cipherText);

            // 1) читаем IV из префикса
            var iv        = fullCipher.Take(_blockSizeBytes).ToArray();
            var cipherRaw = fullCipher.Skip(_blockSizeBytes).ToArray();

            // 2) расшифровываем
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV  = iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms        = new MemoryStream(cipherRaw);
            using var cs        = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr        = new StreamReader(cs, Encoding.UTF8);

            return sr.ReadToEnd();
        }

    // Вспомогательный метод для вычисления IV
        private byte[] ComputeIv(string plainText)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(plainText));
            return hash.Take(_blockSizeBytes).ToArray();
        }
}
