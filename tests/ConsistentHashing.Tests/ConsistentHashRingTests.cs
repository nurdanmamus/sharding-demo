using ConsistentHashing;
using Xunit;

namespace ConsistentHashing.Tests;

public class ConsistentHashRingTests
{
    private static ConsistentHashRing RingWith(params string[] nodes)
    {
        var ring = new ConsistentHashRing(virtualNodes: 150);
        foreach (var n in nodes) ring.AddNode(n);
        return ring;
    }

    [Fact]
    public void AyniAnahtar_HepAyniDugume_Gider()
    {
        var ring = RingWith("shard1", "shard2", "shard3");

        var first = ring.GetNode("kullanici:42");
        for (int i = 0; i < 1000; i++)
            Assert.Equal(first, ring.GetNode("kullanici:42"));
    }

    [Fact]
    public void Bos_Halkada_GetNode_Hata_Firlatir()
    {
        var ring = new ConsistentHashRing();
        Assert.Throws<InvalidOperationException>(() => ring.GetNode("x"));
    }

    [Fact]
    public void Dagilim_Makul_Dengeli()
    {
        var ring = RingWith("shard1", "shard2", "shard3");
        var counts = new Dictionary<string, int> { ["shard1"] = 0, ["shard2"] = 0, ["shard3"] = 0 };

        const int total = 60_000;
        for (int i = 0; i < total; i++)
            counts[ring.GetNode($"key-{i}")]++;

        // İdeal pay her shard için ~%33. Her shard'ın %25–%42 arasında olmasını
        // bekleriz (150 vnode ile dağılım oldukça dengeli olur).
        foreach (var c in counts.Values)
        {
            double share = (double)c / total;
            Assert.InRange(share, 0.25, 0.42);
        }
    }

    [Fact]
    public void Dugum_Cikinca_Sadece_Etkilenen_Anahtarlar_Tasinir()
    {
        var ring = RingWith("shard1", "shard2", "shard3");

        const int total = 50_000;
        var keys = Enumerable.Range(0, total).Select(i => $"key-{i}").ToArray();

        // Çıkarmadan önceki yerleşim.
        var before = keys.ToDictionary(k => k, k => ring.GetNode(k));

        // Bir shard'ı çıkar.
        ring.RemoveNode("shard2");

        int moved = 0;
        foreach (var k in keys)
        {
            var now = ring.GetNode(k);
            // shard2'de olmayan anahtarlar YERİNDE kalmalı (consistent hashing
            // garantisi). Sadece shard2'deki anahtarlar başka shard'a geçer.
            if (before[k] != "shard2")
                Assert.Equal(before[k], now);
            if (before[k] != now)
                moved++;
        }

        // Taşınan anahtar sayısı, kabaca shard2'nin payı kadar (~1/3) olmalı —
        // yani çoğunluk taşınmamalı. Üst sınırı bolca tutuyoruz (%45).
        Assert.True(moved < total * 0.45,
            $"Beklenenden çok anahtar taşındı: {moved}/{total}");
    }

    [Fact]
    public void Ayni_Dugum_Iki_Kez_Eklenirse_Yok_Sayilir()
    {
        var ring = RingWith("shard1");
        ring.AddNode("shard1"); // tekrar
        Assert.Single(ring.Nodes);
    }
}
