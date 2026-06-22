using ShardingDemo.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Shard tanımlarını konfigürasyondan oku (appsettings.json veya ortam değişkeni).
var shards = builder.Configuration.GetSection("Shards").Get<List<ShardConfig>>() ?? new();
var virtualNodes = builder.Configuration.GetValue<int?>("VirtualNodesPerShard") ?? 150;

builder.Services.AddSingleton(new ShardRouter(shards, virtualNodes));
builder.Services.AddSingleton<ShardRepository>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Açılışta her shard'da tabloyu oluştur. Postgres container'ları geç hazır
// olabileceği için bağlanana kadar yeniden dene.
using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<ShardRepository>();
    await WaitAndMigrateAsync(repo, app.Logger);
}

// ---- ENDPOINT'LER ----

// Yaz: anahtar-değeri doğru shard'a koyar; hangi shard'a gittiğini döndürür.
app.MapPost("/api/data", async (KeyValueRequest req, ShardRepository repo, ShardRouter router) =>
{
    if (string.IsNullOrWhiteSpace(req.Key))
        return Results.BadRequest(new { error = "key boş olamaz" });

    await repo.UpsertAsync(req.Key, req.Value ?? "");
    return Results.Ok(new { key = req.Key, shard = router.RouteKey(req.Key) });
})
.WithSummary("Anahtar-değeri doğru shard'a yazar");

// Oku: anahtarı bulunduğu shard'dan getirir.
app.MapGet("/api/data/{key}", async (string key, ShardRepository repo, ShardRouter router) =>
{
    var value = await repo.GetAsync(key);
    return value is null
        ? Results.NotFound(new { key, shard = router.RouteKey(key) })
        : Results.Ok(new { key, value, shard = router.RouteKey(key) });
})
.WithSummary("Anahtarı bulunduğu shard'dan okur");

// Sadece yönlendirme: DB'ye dokunmadan anahtarın hangi shard'a gideceğini gösterir.
app.MapGet("/api/route/{key}", (string key, ShardRouter router) =>
    Results.Ok(new { key, shard = router.RouteKey(key) }))
.WithSummary("Anahtarın gideceği shard'ı hesaplar (DB'ye dokunmaz)");

// Shard'lar + içlerindeki anahtar sayıları.
app.MapGet("/api/shards", async (ShardRepository repo, ShardRouter router) =>
{
    var list = new List<object>();
    foreach (var name in router.ShardNames)
        list.Add(new { name, keyCount = await repo.CountAsync(name) });

    return Results.Ok(new { virtualNodesPerShard = router.VirtualNodesPerShard, shards = list });
})
.WithSummary("Shard listesi ve her birindeki anahtar sayısı");

// Dengeyi ölç: N rastgele anahtarın shard'lara dağılımını ve standart sapmasını verir.
app.MapGet("/api/simulate/distribution", (int? count, ShardRouter router) =>
{
    int n = count is >= 1 and <= 1_000_000 ? count.Value : 10_000;

    var counts = router.ShardNames.ToDictionary(s => s, _ => 0);
    for (int i = 0; i < n; i++)
        counts[router.RouteKey(Guid.NewGuid().ToString("N"))]++;

    double avg = (double)n / counts.Count;
    double stdDev = Math.Sqrt(counts.Values.Sum(c => Math.Pow(c - avg, 2)) / counts.Count);

    return Results.Ok(new
    {
        totalKeys = n,
        idealPerShard = Math.Round(avg, 1),
        standardDeviation = Math.Round(stdDev, 2),
        distribution = counts
            .OrderBy(kv => kv.Key)
            .ToDictionary(
                kv => kv.Key,
                kv => new { keys = kv.Value, percent = Math.Round(100.0 * kv.Value / n, 2) })
    });
})
.WithSummary("N rastgele anahtarın shard'lara dağılımını ölçer (dengeyi gösterir)");

app.Run();

// Postgres hazır olana kadar bekleyip şemayı kuran yardımcı.
static async Task WaitAndMigrateAsync(ShardRepository repo, ILogger logger)
{
    const int maxAttempts = 30;
    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await repo.EnsureSchemaAsync();
            logger.LogInformation("Tüm shard şemaları hazır.");
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Shard'lar henüz hazır değil (deneme {Attempt}/{Max}): {Message}",
                attempt, maxAttempts, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
    throw new InvalidOperationException("Shard veritabanlarına bağlanılamadı.");
}

// POST gövdesi.
public record KeyValueRequest(string Key, string? Value);
