using StackExchange.Redis;
using System.Text.Json;
using System.Diagnostics;

namespace OrderProcessingSystem.Cache
{
    /// <summary>
    /// Interface for Redis caching operations
    /// </summary>
    public interface IRedisService
    {
        // String Operations
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
        Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
        Task<long> DeleteByPatternAsync(string pattern, CancellationToken cancellationToken = default);
        
        // Batch Operations
        Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default);
        Task<bool> SetManyAsync<T>(Dictionary<string, T> keyValues, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task<long> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
        
        // Hash Operations
        Task<bool> HashSetAsync<T>(string key, string field, T value, CancellationToken cancellationToken = default);
        Task<T?> HashGetAsync<T>(string key, string field, CancellationToken cancellationToken = default);
        Task<Dictionary<string, T?>> HashGetAllAsync<T>(string key, CancellationToken cancellationToken = default);
        Task<bool> HashDeleteAsync(string key, string field, CancellationToken cancellationToken = default);
        Task<bool> HashExistsAsync(string key, string field, CancellationToken cancellationToken = default);
        
        // List Operations
        Task<long> ListPushAsync<T>(string key, T value, CancellationToken cancellationToken = default);
        Task<T?> ListPopAsync<T>(string key, CancellationToken cancellationToken = default);
        Task<List<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1, CancellationToken cancellationToken = default);
        Task<long> ListLengthAsync(string key, CancellationToken cancellationToken = default);
        
        // Set Operations
        Task<bool> SetAddAsync<T>(string key, T value, CancellationToken cancellationToken = default);
        Task<bool> SetRemoveAsync<T>(string key, T value, CancellationToken cancellationToken = default);
        Task<List<T>> SetMembersAsync<T>(string key, CancellationToken cancellationToken = default);
        Task<bool> SetContainsAsync<T>(string key, T value, CancellationToken cancellationToken = default);
        
        // Sorted Set Operations
        Task<bool> SortedSetAddAsync<T>(string key, T value, double score, CancellationToken cancellationToken = default);
        Task<List<T>> SortedSetRangeAsync<T>(string key, long start = 0, long stop = -1, Order order = Order.Ascending, CancellationToken cancellationToken = default);
        Task<bool> SortedSetRemoveAsync<T>(string key, T value, CancellationToken cancellationToken = default);
        
        // TTL Operations
        Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default);
        Task<bool> ExpireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);
        
        // Atomic Operations
        Task<long> IncrementAsync(string key, long value = 1, CancellationToken cancellationToken = default);
        Task<long> DecrementAsync(string key, long value = 1, CancellationToken cancellationToken = default);
        
        // Connection Health
        bool IsConnected { get; }
        Task<bool> PingAsync(CancellationToken cancellationToken = default);
        Task<Dictionary<string, string>> GetInfoAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Production-grade Redis cache service implementation with comprehensive features
    /// </summary>
    public class RedisService : IRedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _database;
        private readonly ILogger<RedisService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IServer _server;

        public bool IsConnected => _redis?.IsConnected ?? false;

        public RedisService(
            IConnectionMultiplexer redis,
            ILogger<RedisService> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _database = _redis.GetDatabase();
            _server = _redis.GetServer(_redis.GetEndPoints().First());
            
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            // Monitor connection events
            _redis.ConnectionFailed += OnConnectionFailed;
            _redis.ConnectionRestored += OnConnectionRestored;
            _redis.ErrorMessage += OnErrorMessage;

            _logger.LogInformation("RedisService initialized successfully");
        }

        #region String Operations

        /// <summary>
        /// Gets a cached value by key
        /// </summary>
        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var value = await _database.StringGetAsync(key);
                stopwatch.Stop();
                
                if (!value.HasValue)
                {
                    _logger.LogDebug("Cache miss for key: {CacheKey} ({ElapsedMs}ms)", key, stopwatch.ElapsedMilliseconds);
                    return default;
                }

                _logger.LogDebug("Cache hit for key: {CacheKey} ({ElapsedMs}ms)", key, stopwatch.ElapsedMilliseconds);
                return JsonSerializer.Deserialize<T>(value!, _jsonOptions);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error retrieving from cache with key: {CacheKey} ({ElapsedMs}ms)", key, stopwatch.ElapsedMilliseconds);
                return default;
            }
        }

        /// <summary>
        /// Sets a value in cache with optional expiry
        /// </summary>
        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
                var result = await _database.StringSetAsync(key, serializedValue, expiry);
                stopwatch.Stop();
                
                _logger.LogDebug("Cache set for key: {CacheKey}, Expiry: {Expiry}s ({ElapsedMs}ms)", 
                    key, expiry?.TotalSeconds ?? 0, stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error setting cache with key: {CacheKey} ({ElapsedMs}ms)", key, stopwatch.ElapsedMilliseconds);
                return false;
            }
        }

        /// <summary>
        /// Deletes a cached value by key
        /// </summary>
        public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

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
            ValidateKey(key);

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

        /// <summary>
        /// Deletes all keys matching a pattern
        /// </summary>
        public async Task<long> DeleteByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new ArgumentException("Pattern cannot be empty", nameof(pattern));
            }

            try
            {
                var keys = _server.Keys(pattern: pattern).ToArray();
                if (keys.Length == 0)
                {
                    _logger.LogDebug("No keys found matching pattern: {Pattern}", pattern);
                    return 0;
                }

                var deletedCount = await _database.KeyDeleteAsync(keys);
                _logger.LogInformation("Deleted {Count} keys matching pattern: {Pattern}", deletedCount, pattern);
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting keys by pattern: {Pattern}", pattern);
                return 0;
            }
        }

        #endregion

        #region Batch Operations

        /// <summary>
        /// Gets multiple cached values by keys
        /// </summary>
        public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            if (keys == null || !keys.Any())
            {
                throw new ArgumentException("Keys collection cannot be null or empty", nameof(keys));
            }

            var result = new Dictionary<string, T?>();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
                var values = await _database.StringGetAsync(redisKeys);

                for (int i = 0; i < redisKeys.Length; i++)
                {
                    var key = keys.ElementAt(i);
                    if (values[i].HasValue)
                    {
                        result[key] = JsonSerializer.Deserialize<T>(values[i]!, _jsonOptions);
                    }
                    else
                    {
                        result[key] = default;
                    }
                }

                stopwatch.Stop();
                _logger.LogDebug("Batch get completed for {Count} keys ({ElapsedMs}ms)", redisKeys.Length, stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error in batch get operation ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);
                return result;
            }
        }

        /// <summary>
        /// Sets multiple values in cache with optional expiry
        /// </summary>
        public async Task<bool> SetManyAsync<T>(Dictionary<string, T> keyValues, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            if (keyValues == null || !keyValues.Any())
            {
                throw new ArgumentException("Key-value dictionary cannot be null or empty", nameof(keyValues));
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var batch = _database.CreateBatch();
                var tasks = new List<Task<bool>>();

                foreach (var kvp in keyValues)
                {
                    var serializedValue = JsonSerializer.Serialize(kvp.Value, _jsonOptions);
                    tasks.Add(batch.StringSetAsync(kvp.Key, serializedValue, expiry));
                }

                batch.Execute();
                var results = await Task.WhenAll(tasks);
                stopwatch.Stop();

                var success = results.All(r => r);
                _logger.LogDebug("Batch set completed for {Count} keys, Success: {Success} ({ElapsedMs}ms)", 
                    keyValues.Count, success, stopwatch.ElapsedMilliseconds);
                return success;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error in batch set operation ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);
                return false;
            }
        }

        /// <summary>
        /// Deletes multiple keys
        /// </summary>
        public async Task<long> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            if (keys == null || !keys.Any())
            {
                throw new ArgumentException("Keys collection cannot be null or empty", nameof(keys));
            }

            try
            {
                var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
                var deletedCount = await _database.KeyDeleteAsync(redisKeys);
                _logger.LogDebug("Batch delete completed for {Count} keys, Deleted: {DeletedCount}", redisKeys.Length, deletedCount);
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch delete operation");
                return 0;
            }
        }

        #endregion

        #region Hash Operations

        /// <summary>
        /// Sets a field in a hash
        /// </summary>
        public async Task<bool> HashSetAsync<T>(string key, string field, T value, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new ArgumentException("Field cannot be empty", nameof(field));
            }

            try
            {
                var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
                var result = await _database.HashSetAsync(key, field, serializedValue);
                _logger.LogDebug("Hash set for key: {CacheKey}, field: {Field}", key, field);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting hash field: {CacheKey}.{Field}", key, field);
                return false;
            }
        }

        /// <summary>
        /// Gets a field from a hash
        /// </summary>
        public async Task<T?> HashGetAsync<T>(string key, string field, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new ArgumentException("Field cannot be empty", nameof(field));
            }

            try
            {
                var value = await _database.HashGetAsync(key, field);
                if (!value.HasValue)
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(value!, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting hash field: {CacheKey}.{Field}", key, field);
                return default;
            }
        }

        /// <summary>
        /// Gets all fields from a hash
        /// </summary>
        public async Task<Dictionary<string, T?>> HashGetAllAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var entries = await _database.HashGetAllAsync(key);
                var result = new Dictionary<string, T?>();

                foreach (var entry in entries)
                {
                    result[entry.Name!] = JsonSerializer.Deserialize<T>(entry.Value!, _jsonOptions);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all hash fields: {CacheKey}", key);
                return new Dictionary<string, T?>();
            }
        }

        /// <summary>
        /// Deletes a field from a hash
        /// </summary>
        public async Task<bool> HashDeleteAsync(string key, string field, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new ArgumentException("Field cannot be empty", nameof(field));
            }

            try
            {
                return await _database.HashDeleteAsync(key, field);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting hash field: {CacheKey}.{Field}", key, field);
                return false;
            }
        }

        /// <summary>
        /// Checks if a field exists in a hash
        /// </summary>
        public async Task<bool> HashExistsAsync(string key, string field, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new ArgumentException("Field cannot be empty", nameof(field));
            }

            try
            {
                return await _database.HashExistsAsync(key, field);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking hash field existence: {CacheKey}.{Field}", key, field);
                return false;
            }
        }

        #endregion

        #region List Operations

        /// <summary>
        /// Pushes a value to the right of a list
        /// </summary>
        public async Task<long> ListPushAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
                var length = await _database.ListRightPushAsync(key, serializedValue);
                _logger.LogDebug("List push for key: {CacheKey}, new length: {Length}", key, length);
                return length;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pushing to list: {CacheKey}", key);
                return 0;
            }
        }

        /// <summary>
        /// Pops a value from the left of a list
        /// </summary>
        public async Task<T?> ListPopAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var value = await _database.ListLeftPopAsync(key);
                if (!value.HasValue)
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(value!, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error popping from list: {CacheKey}", key);
                return default;
            }
        }

        /// <summary>
        /// Gets a range of values from a list
        /// </summary>
        public async Task<List<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var values = await _database.ListRangeAsync(key, start, stop);
                return values.Select(v => JsonSerializer.Deserialize<T>(v!, _jsonOptions)!).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting list range: {CacheKey}", key);
                return new List<T>();
            }
        }

        /// <summary>
        /// Gets the length of a list
        /// </summary>
        public async Task<long> ListLengthAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                return await _database.ListLengthAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting list length: {CacheKey}", key);
                return 0;
            }
        }

        #endregion

        #region Set Operations

        /// <summary>
        /// Adds a value to a set
        /// </summary>
        public async Task<bool> SetAddAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
                return await _database.SetAddAsync(key, serializedValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding to set: {CacheKey}", key);
                return false;
            }
        }

        /// <summary>
        /// Removes a value from a set
        /// </summary>
        public async Task<bool> SetRemoveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
                return await _database.SetRemoveAsync(key, serializedValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing from set: {CacheKey}", key);
                return false;
            }
        }

        /// <summary>
        /// Gets all members of a set
        /// </summary>
        public async Task<List<T>> SetMembersAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var values = await _database.SetMembersAsync(key);
                return values.Select(v => JsonSerializer.Deserialize<T>(v!, _jsonOptions)!).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting set members: {CacheKey}", key);
                return new List<T>();
            }
        }

        /// <summary>
        /// Checks if a value is a member of a set
        /// </summary>
        public async Task<bool> SetContainsAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
                return await _database.SetContainsAsync(key, serializedValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking set membership: {CacheKey}", key);
                return false;
            }
        }

        #endregion

        #region Sorted Set Operations

        /// <summary>
        /// Adds a value to a sorted set with a score
        /// </summary>
        public async Task<bool> SortedSetAddAsync<T>(string key, T value, double score, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
                return await _database.SortedSetAddAsync(key, serializedValue, score);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding to sorted set: {CacheKey}", key);
                return false;
            }
        }

        /// <summary>
        /// Gets a range of values from a sorted set
        /// </summary>
        public async Task<List<T>> SortedSetRangeAsync<T>(string key, long start = 0, long stop = -1, Order order = Order.Ascending, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var values = await _database.SortedSetRangeByRankAsync(key, start, stop, order);
                return values.Select(v => JsonSerializer.Deserialize<T>(v!, _jsonOptions)!).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sorted set range: {CacheKey}", key);
                return new List<T>();
            }
        }

        /// <summary>
        /// Removes a value from a sorted set
        /// </summary>
        public async Task<bool> SortedSetRemoveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
                return await _database.SortedSetRemoveAsync(key, serializedValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing from sorted set: {CacheKey}", key);
                return false;
            }
        }

        #endregion

        #region TTL Operations

        /// <summary>
        /// Gets the time-to-live for a key
        /// </summary>
        public async Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                return await _database.KeyTimeToLiveAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting TTL for key: {CacheKey}", key);
                return null;
            }
        }

        /// <summary>
        /// Sets an expiration time for a key
        /// </summary>
        public async Task<bool> ExpireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                return await _database.KeyExpireAsync(key, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting expiration for key: {CacheKey}", key);
                return false;
            }
        }

        #endregion

        #region Atomic Operations

        /// <summary>
        /// Increments a value atomically
        /// </summary>
        public async Task<long> IncrementAsync(string key, long value = 1, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                return await _database.StringIncrementAsync(key, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error incrementing key: {CacheKey}", key);
                return 0;
            }
        }

        /// <summary>
        /// Decrements a value atomically
        /// </summary>
        public async Task<long> DecrementAsync(string key, long value = 1, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                return await _database.StringDecrementAsync(key, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decrementing key: {CacheKey}", key);
                return 0;
            }
        }

        #endregion

        #region Connection Health

        /// <summary>
        /// Pings the Redis server to check connectivity
        /// </summary>
        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var latency = await _database.PingAsync();
                _logger.LogDebug("Redis ping successful, latency: {Latency}ms", latency.TotalMilliseconds);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis ping failed");
                return false;
            }
        }

        /// <summary>
        /// Gets Redis server information
        /// </summary>
        public async Task<Dictionary<string, string>> GetInfoAsync(CancellationToken cancellationToken = default)
        {
            var info = new Dictionary<string, string>();

            try
            {
                var serverInfo = await _server.InfoAsync();
                
                foreach (var section in serverInfo)
                {
                    foreach (var kvp in section)
                    {
                        info[$"{section.Key}:{kvp.Key}"] = kvp.Value;
                    }
                }

                info["IsConnected"] = IsConnected.ToString();
                info["EndPoints"] = string.Join(", ", _redis.GetEndPoints().Select(ep => ep.ToString()));

                _logger.LogDebug("Retrieved Redis server info with {Count} properties", info.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Redis server info");
                info["Error"] = ex.Message;
            }

            return info;
        }

        #endregion

        #region Event Handlers

        private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
        {
            _logger.LogError(e.Exception, 
                "Redis connection failed. EndPoint: {EndPoint}, FailureType: {FailureType}", 
                e.EndPoint, e.FailureType);
        }

        private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
        {
            _logger.LogInformation(
                "Redis connection restored. EndPoint: {EndPoint}", 
                e.EndPoint);
        }

        private void OnErrorMessage(object? sender, RedisErrorEventArgs e)
        {
            _logger.LogError("Redis error message: {Message}, EndPoint: {EndPoint}", 
                e.Message, e.EndPoint);
        }

        #endregion

        #region Helper Methods

        private void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Cache key cannot be empty", nameof(key));
            }
        }

        #endregion
    }
}
