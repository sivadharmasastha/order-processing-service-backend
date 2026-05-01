using StackExchange.Redis;
using System.Text.Json;

namespace OrderProcessingSystem.Cache
{
    /// <summary>
    /// Interface for Redis caching operations
    /// </summary>
    public interface IRedisService
    {
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
        Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Redis cache service implementation for distributed caching
    /// </summary>
    public class RedisService : IRedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _database;
        private readonly ILogger<RedisService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public RedisService(
            IConnectionMultiplexer redis,
            ILogger<RedisService> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _database = _redis.GetDatabase();
            
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        /// <summary>
        /// Gets a cached value by key
        /// </summary>
        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Cache key cannot be empty", nameof(key));
            }

            try
            {
                var value = await _database.StringGetAsync(key);
                
                if (!value.HasValue)
                {
                    _logger.LogDebug("Cache miss for key: {CacheKey}", key);
                    return default;
                }

                _logger.LogDebug("Cache hit for key: {CacheKey}", key);
                return JsonSerializer.Deserialize<T>(value!, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving from cache with key: {CacheKey}", key);
                return default;
            }
        }

        /// <summary>
        /// Sets a value in cache with optional expiry
        /// </summary>
        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Cache key cannot be empty", nameof(key));
            }

            try
            {
                var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
                var result = await _database.StringSetAsync(key, serializedValue, expiry);
                
                _logger.LogDebug("Cache set for key: {CacheKey}, Expiry: {Expiry}", key, expiry?.TotalSeconds ?? 0);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cache with key: {CacheKey}", key);
                return false;
            }
        }

        /// <summary>
        /// Deletes a cached value by key
        /// </summary>
        public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Cache key cannot be empty", nameof(key));
            }

            try
            {
                var result = await _database.KeyDeleteAsync(key);
                _logger.LogDebug("Cache delete for key: {CacheKey}, Result: {Result}", key, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting from cache with key: {CacheKey}", key);
                return false;
            }
        }

        /// <summary>
        /// Checks if a key exists in cache
        /// </summary>
        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Cache key cannot be empty", nameof(key));
            }

            try
            {
                return await _database.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking cache existence with key: {CacheKey}", key);
                return false;
            }
        }
    }
}
