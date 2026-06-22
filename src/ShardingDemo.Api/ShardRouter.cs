using ConsistentHashing;

namespace ShardingDemo.Api;

/// <summary>appsettings / ortam değişkeninden okunan tek bir shard tanımı.</summary>
public sealed class ShardConfig
{
    public string Name { get; set; } = "";
    public string ConnectionString { get; set; } = "";
}

/// <summary>
/// Consistent hashing halkasını sarmalar. "Bu anahtar hangi shard'a gider?"
/// sorusunu cevaplar ve shard adı → bağlantı dizesi eşlemesini tutar.
/// Uygulama boyunca tek örnek (singleton) yaşar.
/// </summary>
public sealed class ShardRouter
{
    private readonly ConsistentHashRing _ring;
    private readonly Dictionary<string, string> _connectionStrings = new();

    public ShardRouter(IEnumerable<ShardConfig> shards, int virtualNodes)
    {
        _ring = new ConsistentHashRing(virtualNodes);
        foreach (var s in shards)
        {
            if (string.IsNullOrWhiteSpace(s.Name)) continue;
            _ring.AddNode(s.Name);
            _connectionStrings[s.Name] = s.ConnectionString;
        }

        if (_connectionStrings.Count == 0)
            throw new InvalidOperationException("Hiç shard tanımlanmamış. appsettings.json > Shards bölümünü kontrol edin.");
    }

    public IReadOnlyCollection<string> ShardNames => _connectionStrings.Keys.ToArray();
    public int VirtualNodesPerShard => _ring.VirtualNodesPerNode;

    /// <summary>Anahtarın yönleneceği shard adı (DB'ye dokunmaz, saf hesap).</summary>
    public string RouteKey(string key) => _ring.GetNode(key);

    public string ConnectionStringFor(string shard) => _connectionStrings[shard];
}
