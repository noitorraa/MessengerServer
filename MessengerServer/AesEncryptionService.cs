using System.Security.Cryptography;
using System.Text;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public AesEncryptionService(IConfiguration config)
    {
        // Ключ храните в конфиге или лучше — в защищённом хранилище (Key Vault)
        var keyBase64 = config["AES:Key"];
        if (string.IsNullOrEmpty(keyBase64))
        {
            throw new ArgumentException("AES:Key is missing or empty in the configuration.");
        }
        _key = Convert.FromBase64String(keyBase64);
    }

    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        // сначала IV
        ms.Write(aes.IV, 0, aes.IV.Length);
        using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        using var sw = new StreamWriter(cs);
        sw.Write(plainText);
        sw.Close();
        return Convert.ToBase64String(ms.ToArray());
    }

    public string Decrypt(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);
        using var aes = Aes.Create();
        aes.Key = _key;
        var ivLength = aes.BlockSize / 8;
        var iv = fullCipher.Take(ivLength).ToArray();
        var cipher = fullCipher.Skip(ivLength).ToArray();
        using var decryptor = aes.CreateDecryptor(aes.Key, iv);
        using var ms = new MemoryStream(cipher);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd();
    }
}
