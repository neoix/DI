using StackExchange.Redis;

namespace DI.ApiService.Services;

public class RedisCacheService
{
  private static Lazy<ConnectionMultiplexer>? lazyConnection;
  private readonly IDatabase db;

  public RedisCacheService(IConfiguration config)
  {
    lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
        ConnectionMultiplexer.Connect(config["RedisCacheConnectionString"]!)
    );

    db = lazyConnection.Value.GetDatabase();
  }

  public async Task<bool> SetAsync(string key, string value, TimeSpan? expiry = null)
  {
    return await db.StringSetAsync(key, value, expiry, true);
  }

  public async Task<string?> GetAsync(string key)
  {
    var value = await db.StringGetAsync(key);
    return value.HasValue ? value.ToString() : null;
  }

  public async Task<bool> DeleteAsync(string key)
  {
    return await db.KeyDeleteAsync(key);
  }

  public async Task<bool> ExistsAsync(string key)
  {
    return await db.KeyExistsAsync(key);
  }

}
