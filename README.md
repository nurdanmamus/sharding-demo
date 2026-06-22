# Sharding with Consistent Hashing — .NET Demo

A small demo that implements the **consistent hashing** algorithm from scratch and uses it to distribute data across multiple databases (shards). The algorithm runs inside a real **ASP.NET Core Web API** backed by **three separate PostgreSQL** instances.

> Goal: show — with working code — how sharding works, why `hash % N` isn't enough, and what consistent hashing buys you.

---

## Table of Contents

- [What it demonstrates](#what-it-demonstrates)
- [Consistent hashing in 60 seconds](#consistent-hashing-in-60-seconds)
- [Architecture](#architecture)
- [Running it](#running-it)
- [API endpoints](#api-endpoints)
- [Things worth trying](#things-worth-trying)
- [Project structure](#project-structure)
- [Tests](#tests)

---

## What it demonstrates

- **Consistent hashing from scratch**: the `ConsistentHashing` library is a hash ring with virtual nodes. No external dependencies.
- **Real sharding**: every key is routed by the ring to **one of three PostgreSQL** instances and stored there. Reads use the same computation to hit the correct shard.
- **A balance-measuring endpoint**: reports how evenly thousands of random keys spread across the shards, using standard deviation.
- **Tested guarantee**: unit tests prove that when a shard is removed, only a small fraction of keys are moved.

---

## Consistent hashing in 60 seconds

**The problem with the naive approach:**

```
shard = hash(key) % shard_count
```

This works, but when the number of shards changes (`% 3` -> `% 4`), almost **all** keys change place — meaning you'd have to move all your data. For a cache, that's a disaster: suddenly everything is a "miss."

**What consistent hashing does:**

Place both the shards and the keys on a circular "ring." A key's owner is the first shard you reach when walking clockwise.

```
        0 / max
          .
    shard3   shard1
       .       .
        .     .
        shard2
```

When a shard is added or removed, only the keys in the **neighboring arc** (~1/N) move; the rest stay put.

**Virtual nodes:** each physical shard is placed on the ring not once, but hundreds of times. This spreads the load far more evenly, and when a shard goes down its load is distributed across many shards instead of a single neighbor. (In this project there are 150 virtual nodes per shard.)

---

## Architecture

```
                       +-----------------------------+
  HTTP (Swagger) ----> |      ShardingDemo.Api       |
                       |                             |
                       |   ShardRouter               |
                       |   +- ConsistentHashRing     |  <- key -> shard decision
                       |   ShardRepository (Npgsql)  |
                       +------+-------+-------+-------+
                              |       |       |
                          +---v--+ +--v---+ +-v----+
                          |shard1| |shard2| |shard3|   <- 3 separate Postgres
                          +------+ +------+ +------+
```

- **`ConsistentHashRing`** — the pure algorithm; knows nothing about infrastructure. Its only job: "which node owns this key?"
- **`ShardRouter`** — wraps the ring and maps a shard name to its connection string.
- **`ShardRepository`** — applies the decision: connects to the right Postgres and reads/writes (Npgsql directly instead of EF, for transparency).

---

## Running it

### Requirements
- [Docker](https://www.docker.com/) and Docker Compose

### Steps

```bash
docker compose up --build
```

Once everything is up, open the **Swagger UI** in your browser:

```
http://localhost:8080/swagger
```

On first startup the API waits for the Postgres instances to become ready and automatically creates the table in each shard.

### Without Docker (running only the API locally)

Bring up just the Postgres instances, then run the API in the `Development` profile:

```bash
docker compose up shard1 shard2 shard3
dotnet run --project src/ShardingDemo.Api
```

`appsettings.Development.json` connects to the host ports (5432/5433/5434).

---

## API endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/data` | `{ "key": "...", "value": "..." }` — writes the key to the correct shard and returns which shard it went to |
| `GET`  | `/api/data/{key}` | Reads the key from the shard it lives on |
| `GET`  | `/api/route/{key}` | Computes which shard the key would go to, without touching the DB |
| `GET`  | `/api/shards` | List of shards + key count in each |
| `GET`  | `/api/simulate/distribution?count=10000` | Measures the distribution of N random keys + standard deviation |

### Example

```bash
# Write
curl -X POST http://localhost:8080/api/data \
  -H "Content-Type: application/json" \
  -d '{"key":"user:42","value":"Alice"}'
# -> { "key": "user:42", "shard": "shard2" }

# Read
curl http://localhost:8080/api/data/user:42
# -> { "key": "user:42", "value": "Alice", "shard": "shard2" }
```

---

## Things worth trying

1. **Distribution balance:** call `GET /api/simulate/distribution?count=100000`. Notice that all three shards get roughly 33% each and the standard deviation is small.
2. **Consistency:** query the same key via `/api/route/{key}` repeatedly — it always returns the same shard.
3. **Is it really distributed?** After writing a few records, connect to each Postgres individually and run `SELECT * FROM kv;` to confirm the data is spread across all three databases:
   ```bash
   docker exec -it $(docker compose ps -q shard1) psql -U postgres -d shard -c "SELECT * FROM kv;"
   ```

---

## Project structure

```
sharding-demo/
├── docker-compose.yml          # 3 Postgres + API
├── Dockerfile
├── ShardingDemo.sln
├── src/
│   ├── ConsistentHashing/      # The algorithm (standalone library)
│   │   ├── ConsistentHashRing.cs
│   │   ├── IHashFunction.cs
│   │   └── Md5HashFunction.cs
│   └── ShardingDemo.Api/       # Web API
│       ├── Program.cs          # endpoints
│       ├── ShardRouter.cs      # key -> shard
│       └── ShardRepository.cs  # reads/writes to the right Postgres
└── tests/
    └── ConsistentHashing.Tests/
```

---

## Tests

```bash
dotnet test
```

Guarantees covered: the same key always maps to the same node, the distribution is balanced, and **when a node is removed, only the affected keys move** (the core promise of consistent hashing).

---

## Notes / limitations

This is a **teaching demo**. The main things missing for production: data migration during shard rebalancing, replication, connection-pool tuning, error/retry policies, and security. The point is to show the algorithm and the idea of sharding clearly.
