using System.Security.Cryptography;
using System.Text;

namespace ConsistentHashing;

/// <summary>
/// MD5'in ilk 4 baytını bir uint olarak alan hash fonksiyonu.
/// (memcached'in "ketama" istemcisinin kullandığı klasik yöntem.)
/// MD5 burada GÜVENLİK için değil, sadece düzgün/dengeli dağılım için
/// kullanılıyor — kriptografik bir amaç yok.
/// </summary>
public sealed class Md5HashFunction : IHashFunction
{
    public uint Hash(string key)
    {
        Span<byte> digest = stackalloc byte[16];
        MD5.HashData(Encoding.UTF8.GetBytes(key), digest);
        // İlk 4 baytı tek bir 32-bit sayıya çevir → halka üzerindeki konum.
        return BitConverter.ToUInt32(digest[..4]);
    }
}
