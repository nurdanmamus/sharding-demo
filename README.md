# Consistent Hashing ile Sharding — .NET Demo

Veriyi birden çok veritabanına (shard) dağıtırken kullanılan **consistent hashing** algoritmasını sıfırdan yazan; bu kütüphaneyi gerçek bir **ASP.NET Core Web API** ve **3 ayrı PostgreSQL** örneği üzerinde çalıştıran küçük bir demo.

> Amaç: "sharding nasıl çalışır, neden `hash % N` yetmez, consistent hashing ne kazandırır" sorularını **çalışan kodla** göstermek.

---

## İçindekiler

- [Neyi gösteriyor?](#neyi-gösteriyor)
- [Consistent hashing 60 saniyede](#consistent-hashing-60-saniyede)
- [Mimari](#mimari)
- [Çalıştırma](#çalıştırma)
- [API uçları](#api-uçları)
- [Denemeye değer senaryolar](#denemeye-değer-senaryolar)
- [Proje yapısı](#proje-yapısı)
- [Testler](#testler)

---

## Neyi gösteriyor?

- **Sıfırdan consistent hashing**: `ConsistentHashing` kütüphanesi, sanal düğümlü (virtual node) bir hash halkası. Dış bağımlılık yok.
- **Gerçek sharding**: Her anahtar, halka tarafından **3 Postgres'ten birine** yönlendirilip orada saklanır. Okuma da aynı hesapla doğru shard'a gider.
- **Dengeyi ölçen uç**: Binlerce rastgele anahtarın shard'lara ne kadar dengeli dağıldığını standart sapmayla raporlayan bir endpoint.
- **Test edilmiş garanti**: Bir shard çıkınca anahtarların yalnızca küçük bir kısmının taşındığını kanıtlayan birim testleri.

---

## Consistent hashing 60 saniyede

**Naif yöntemin sorunu:**

```
shard = hash(key) % shard_sayısı
```

Bu çalışır, ama shard sayısı değişince (`% 3` → `% 4`) neredeyse **bütün** anahtarların yeri değişir — yani tüm veriyi taşımak gerekir. Cache için felaket: bir anda her şey "miss" olur.

**Consistent hashing'in çözümü:**

Hem shard'ları hem anahtarları çembersel bir "halkaya" yerleştir. Bir anahtarın sahibi, halkada saat yönünde ilerlerken karşılaşılan ilk shard'dır.

```
        0 / max
          •
    shard3   shard1
       •       •
        •     •
        shard2
```

Bir shard eklenip çıkınca yalnızca **komşu yaydaki** anahtarlar (~%1/N) taşınır; gerisi yerinde kalır.

**Sanal düğümler (virtual nodes):** Her fiziksel shard'ı halkaya tek noktayla değil, yüzlerce noktayla koyarız. Böylece yük çok daha dengeli dağılır ve bir shard düşünce yükü tek komşuya değil, birçok shard'a yayılır. (Bu projede shard başına 150 sanal düğüm var.)

---

## Mimari

```
                       ┌─────────────────────────────┐
  HTTP (Swagger) ────► │      ShardingDemo.Api        │
                       │                              │
                       │   ShardRouter                │
                       │   └─ ConsistentHashRing      │  ← anahtar → shard kararı
                       │   ShardRepository (Npgsql)   │
                       └───────┬───────┬───────┬──────┘
                               │       │       │
                          ┌────▼─┐ ┌───▼──┐ ┌──▼───┐
                          │shard1│ │shard2│ │shard3│   ← 3 ayrı Postgres
                          └──────┘ └──────┘ └──────┘
```

- **`ConsistentHashRing`** — saf algoritma, hiçbir altyapı bilmez. Tek işi: "bu anahtar hangi düğüme ait?"
- **`ShardRouter`** — halkayı sarmalar, shard adını bağlantı dizesine eşler.
- **`ShardRepository`** — kararı uygular: doğru Postgres'e bağlanıp okur/yazar (EF değil, şeffaflık için doğrudan Npgsql).

---

## Çalıştırma

### Gereksinim
- [Docker](https://www.docker.com/) ve Docker Compose

### Adımlar

```bash
docker compose up --build
```

Hepsi ayağa kalkınca tarayıcıdan **Swagger UI**:

```
http://localhost:8080/swagger
```

İlk açılışta API, Postgres'ler hazır olana kadar bekleyip her shard'da tabloyu otomatik oluşturur.

### Docker olmadan (sadece API'yi lokal çalıştırmak)

Önce Postgres'leri kaldırın, sonra API'yi `Development` profilinde çalıştırın:

```bash
docker compose up shard1 shard2 shard3
dotnet run --project src/ShardingDemo.Api
```

`appsettings.Development.json` host portlarına (5432/5433/5434) bağlanır.

---

## API uçları

| Metot | Yol | Açıklama |
|-------|-----|----------|
| `POST` | `/api/data` | `{ "key": "...", "value": "..." }` — anahtarı doğru shard'a yazar, hangi shard'a gittiğini döndürür |
| `GET`  | `/api/data/{key}` | Anahtarı bulunduğu shard'dan okur |
| `GET`  | `/api/route/{key}` | DB'ye dokunmadan anahtarın hangi shard'a gideceğini hesaplar |
| `GET`  | `/api/shards` | Shard listesi + her birindeki anahtar sayısı |
| `GET`  | `/api/simulate/distribution?count=10000` | N rastgele anahtarın dağılımını + standart sapmasını ölçer |

### Örnek

```bash
# Yaz
curl -X POST http://localhost:8080/api/data \
  -H "Content-Type: application/json" \
  -d '{"key":"kullanici:42","value":"Ahmet"}'
# → { "key": "kullanici:42", "shard": "shard2" }

# Oku
curl http://localhost:8080/api/data/kullanici:42
# → { "key": "kullanici:42", "value": "Ahmet", "shard": "shard2" }
```

---

## Denemeye değer senaryolar

1. **Dağılım dengesi:** `GET /api/simulate/distribution?count=100000` çağırın. Üç shard'ın da yaklaşık %33'er pay aldığını ve standart sapmanın küçük olduğunu görün.
2. **Tutarlılık:** Aynı anahtarı `/api/route/{key}` ile defalarca sorgulayın — hep aynı shard döner.
3. **Gerçekten dağıtıldı mı?** Birkaç kayıt yazdıktan sonra her Postgres'e ayrı ayrı bağlanıp `SELECT * FROM kv;` çalıştırın; verinin üç DB'ye yayıldığını doğrulayın:
   ```bash
   docker exec -it $(docker compose ps -q shard1) psql -U postgres -d shard -c "SELECT * FROM kv;"
   ```

---

## Proje yapısı

```
sharding-demo/
├── docker-compose.yml          # 3 Postgres + API
├── Dockerfile
├── ShardingDemo.sln
├── src/
│   ├── ConsistentHashing/      # ⭐ Algoritma (bağımsız kütüphane)
│   │   ├── ConsistentHashRing.cs
│   │   ├── IHashFunction.cs
│   │   └── Md5HashFunction.cs
│   └── ShardingDemo.Api/       # Web API
│       ├── Program.cs          # endpoint'ler
│       ├── ShardRouter.cs      # anahtar → shard
│       └── ShardRepository.cs  # doğru Postgres'e okuma/yazma
└── tests/
    └── ConsistentHashing.Tests/
```

---

## Testler

```bash
dotnet test
```

Kapsanan garantiler: aynı anahtarın hep aynı düğüme gitmesi, dağılımın dengeli olması ve **bir düğüm çıkınca yalnızca etkilenen anahtarların taşınması** (consistent hashing'in asıl vaadi).

---

## Notlar / sınırlamalar

Bu bir **öğretici demo**. Üretim için eksik olan başlıca şeyler: shard yeniden dengeleme sırasında veri taşıma (rebalancing), çoğaltma (replication), bağlantı havuzu ayarları, hata/yeniden deneme politikaları ve güvenlik. Amaç algoritmayı ve sharding fikrini net göstermek.
