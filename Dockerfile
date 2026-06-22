# ---- Build aşaması ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Önce sadece csproj'leri kopyalayıp restore et (katman önbelleği için).
COPY ShardingDemo.sln .
COPY src/ConsistentHashing/ConsistentHashing.csproj src/ConsistentHashing/
COPY src/ShardingDemo.Api/ShardingDemo.Api.csproj src/ShardingDemo.Api/
RUN dotnet restore src/ShardingDemo.Api/ShardingDemo.Api.csproj

# Sonra tüm kaynağı kopyalayıp yayınla.
COPY . .
RUN dotnet publish src/ShardingDemo.Api/ShardingDemo.Api.csproj -c Release -o /app

# ---- Runtime aşaması ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ShardingDemo.Api.dll"]
