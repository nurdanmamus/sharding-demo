using Npgsql;

namespace ShardingDemo.Api;

/// <summary>
/// Veriyi DOĞRU shard'a (Postgres örneğine) yazıp okur.
/// Her anahtar için önce ShardRouter ile hedef shard bulunur, sonra o shard'ın
/// kendi veritabanına bağlanılır. Basit ve şeffaf olması için EF değil, doğrudan
/// Npgsql kullanıldı — her sorguda hangi DB'ye gidildiği net görünsün.
/// </summary>
public sealed class ShardRepository
{
    private readonly ShardRouter _router;

    public ShardRepository(ShardRouter router) => _router = router;

    /// <summary>Her shard'da kv tablosunu (yoksa) oluşturur.</summary>
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        foreach (var shard in _router.ShardNames)
        {
            await using var conn = new NpgsqlConnection(_router.ConnectionStringFor(shard));
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS kv (
                    key        text PRIMARY KEY,
                    value      text NOT NULL,
                    updated_at timestamptz NOT NULL DEFAULT now()
                );
                """, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Anahtar-değer çiftini doğru shard'a yazar (upsert).</summary>
    public async Task UpsertAsync(string key, string value, CancellationToken ct = default)
    {
        var shard = _router.RouteKey(key);
        await using var conn = new NpgsqlConnection(_router.ConnectionStringFor(shard));
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO kv (key, value) VALUES (@k, @v)
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now();
            """, conn);
        cmd.Parameters.AddWithValue("k", key);
        cmd.Parameters.AddWithValue("v", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Anahtarı, bulunduğu shard'dan okur (yoksa null).</summary>
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var shard = _router.RouteKey(key);
        await using var conn = new NpgsqlConnection(_router.ConnectionStringFor(shard));
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT value FROM kv WHERE key = @k;", conn);
        cmd.Parameters.AddWithValue("k", key);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>Belirli bir shard'daki toplam anahtar sayısı.</summary>
    public async Task<long> CountAsync(string shard, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_router.ConnectionStringFor(shard));
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM kv;", conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (long)(result ?? 0L);
    }
}
