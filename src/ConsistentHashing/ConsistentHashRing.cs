namespace ConsistentHashing;

/// <summary>
/// Sanal düğümlü (virtual node) consistent hashing halkası.
///
/// FİKİR:
///   - Hem düğümler (shard'lar) hem de anahtarlar 0..2^32-1 aralığında bir
///     "halkaya" yerleştirilir. Halka çembersel düşünülür: en büyük değerden
///     sonra başa (0) sarar.
///   - Bir anahtarın sahibi: anahtarın konumundan SAAT YÖNÜNDE ilerlerken
///     karşılaşılan İLK düğümdür.
///
/// NEDEN SANAL DÜĞÜM?
///   - Her fiziksel düğümü halkaya tek noktayla değil, YÜZLERCE noktayla
///     ("vnode") koyarız. Böylece yük çok daha dengeli dağılır ve bir düğüm
///     düşünce yükü tek komşuya değil, birçok düğüme yayılır.
///
/// NEDEN consistent?
///   - Düğüm eklenip çıkınca anahtarların yalnızca ~1/N kadarı taşınır;
///     gerisi yerinde kalır (naif "hash % N" ise neredeyse hepsini taşır).
/// </summary>
public sealed class ConsistentHashRing
{
    private readonly object _gate = new();
    private readonly SortedDictionary<uint, string> _ring = new();
    private readonly HashSet<string> _nodes = new();
    private readonly int _virtualNodes;
    private readonly IHashFunction _hash;

    // Halka konumlarının sıralı kopyası — GetNode'da ikili arama (binary search)
    // için. _ring her değiştiğinde yeniden üretilir.
    private uint[] _sortedKeys = Array.Empty<uint>();

    public ConsistentHashRing(int virtualNodes = 150, IHashFunction? hashFunction = null)
    {
        if (virtualNodes < 1)
            throw new ArgumentOutOfRangeException(nameof(virtualNodes), "En az 1 sanal düğüm olmalı.");
        _virtualNodes = virtualNodes;
        _hash = hashFunction ?? new Md5HashFunction();
    }

    /// <summary>Halkadaki fiziksel düğümler (shard adları).</summary>
    public IReadOnlyCollection<string> Nodes
    {
        get { lock (_gate) return _nodes.ToArray(); }
    }

    /// <summary>Fiziksel düğüm başına sanal düğüm sayısı.</summary>
    public int VirtualNodesPerNode => _virtualNodes;

    /// <summary>Halkaya bir düğüm ekler (sanal kopyalarıyla birlikte).</summary>
    public void AddNode(string node)
    {
        if (string.IsNullOrWhiteSpace(node))
            throw new ArgumentException("Düğüm adı boş olamaz.", nameof(node));

        lock (_gate)
        {
            if (!_nodes.Add(node)) return; // zaten var

            for (int i = 0; i < _virtualNodes; i++)
            {
                uint pos = _hash.Hash($"{node}#{i}");
                // Çok nadir de olsa iki vnode aynı konuma düşebilir; çakışmada
                // bir sonraki boş konuma kaydırarak veri kaybını önleriz.
                while (_ring.ContainsKey(pos)) pos++;
                _ring[pos] = node;
            }

            RebuildSortedKeys();
        }
    }

    /// <summary>Bir düğümü ve tüm sanal kopyalarını halkadan çıkarır.</summary>
    public void RemoveNode(string node)
    {
        lock (_gate)
        {
            if (!_nodes.Remove(node)) return;

            var positions = _ring.Where(kv => kv.Value == node)
                                 .Select(kv => kv.Key)
                                 .ToList();
            foreach (var p in positions) _ring.Remove(p);

            RebuildSortedKeys();
        }
    }

    /// <summary>Bir anahtarın gideceği düğümü (shard'ı) döndürür.</summary>
    public string GetNode(string key)
    {
        lock (_gate)
        {
            if (_sortedKeys.Length == 0)
                throw new InvalidOperationException("Halkada hiç düğüm yok.");

            uint h = _hash.Hash(key);
            int idx = LowerBound(h);
            if (idx == _sortedKeys.Length) idx = 0; // halkanın sonunu geçtiysek başa sar
            return _ring[_sortedKeys[idx]];
        }
    }

    // İlk (value'dan büyük veya eşit) konumun indeksini bulur (ikili arama).
    private int LowerBound(uint value)
    {
        int lo = 0, hi = _sortedKeys.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_sortedKeys[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // SortedDictionary anahtarları zaten sıralı döndürür.
    private void RebuildSortedKeys() => _sortedKeys = _ring.Keys.ToArray();
}
