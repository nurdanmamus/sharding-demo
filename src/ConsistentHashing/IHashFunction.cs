namespace ConsistentHashing;

/// <summary>
/// Bir metni halka üzerindeki bir konuma (uint) çeviren hash fonksiyonu.
/// Soyutlandı ki testlerde sahte/deterministik bir hash kullanılabilsin
/// ya da ileride farklı bir algoritmaya (xxHash, Murmur3...) geçilebilsin.
/// </summary>
public interface IHashFunction
{
    uint Hash(string key);
}
