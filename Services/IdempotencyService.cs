using OrderProcessingSystem.Data;
using OrderProcessingSystem.Models;
using OrderProcessingSystem.Cache;
using OrderProcessingSystem.Config;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Diagnostics;

namespace OrderProcessingSystem.Services
{
    /// <summary>
    /// Interface for idempotency service operations
    /// </summary>
    public interface IIdempotencyService
    {
        Task<IdempotencyResult> CheckAndProcessAsync<T>(
            string idempotencyKey,
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default) where T : class;
        
        Task<bool> HasBeenProcessedAsync(string key, CancellationToken cancellationToken = default);
        Task<T?> GetCachedResponseAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
        Task StoreAsync<T>(string key, int orderId, T responseData, int expiryHours = 24, CancellationToken cancellationToken = default);
        Task<bool> AcquireLockAsync(string key, TimeSpan lockTimeout, CancellationToken cancellationToken = default);
        Task<bool> ReleaseLockAsync(string key, CancellationToken cancellationToken = default);
        Task<int> CleanupExpiredKeysAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Production-grade idempotency service with Redis caching and distributed locking
    /// </summary>
    public class IdempotencyService : IIdempotencyService
    {
        private readonly AppDbContext _dbContext;
        private readonly IRedisService _redisService;
        private readonly ILogger<IdempotencyService> _logger;
        private readonly TimeSpan _defaultLockTimeout = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _defaultCacheExpiration = TimeSpan.FromHours(24);

        public IdempotencyService(
            AppDbContext dbContext,
            IRedisService redisService,
            ILogger<IdempotencyService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        /// <summary>
        /// Check idempotency and process operation with distributed locking (Production-level method)
        /// </summary>
        /// <typeparam name="T">Response type</typeparam>
        /// <param name="idempotencyKey">Unique idempotency key</param>
        /// <param name="operation">Operation to execute if not already processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>IdempotencyResult containing response and whether it was cached</returns>
        public async Task<IdempotencyResult> CheckAndProcessAsync<T>(
            string idempotencyKey,
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default) where T : class
        {
            ValidateKey(idempotencyKey);

            var stopwatch = Stopwatch.StartNew();
            var cacheKey = RedisCacheKeys.IdempotencyKey(idempotencyKey);
            var lockKey = $"{cacheKey}:lock";

            try
            {
                // 1. Fast path: Check Redis cache first
                var cachedResponse = await GetCachedResponseAsync<T>(idempotencyKey, cancellationToken);
                if (cachedResponse != null)
                {
                    stopwatch.Stop();
                    _logger.LogInformation(
                        "Idempotency key {Key} found in cache, returning cached response ({ElapsedMs}ms)",
                        idempotencyKey, stopwatch.ElapsedMilliseconds);
                    
                    return new IdempotencyResult
                    {
                        WasCached = true,
                        Response = cachedResponse,
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // 2. Acquire distributed lock to prevent concurrent processing
                var lockAcquired = await AcquireLockAsync(lockKey, _defaultLockTimeout, cancellationToken);
                if (!lockAcquired)
                {
                    _logger.LogWarning(
                        "Failed to acquire lock for idempotency key {Key}, waiting and retrying...",
                        idempotencyKey);

                    // Wait briefly and check cache again (another instance might have processed it)
                    await Task.Delay(100, cancellationToken);
                    cachedResponse = await GetCachedResponseAsync<T>(idempotencyKey, cancellationToken);
                    if (cachedResponse != null)
                    {
                        stopwatch.Stop();
                        _logger.LogInformation(
                            "Idempotency key {Key} found after lock wait ({ElapsedMs}ms)",
                            idempotencyKey, stopwatch.ElapsedMilliseconds);
                        
                        return new IdempotencyResult
                        {
                            WasCached = true,
                            Response = cachedResponse,
                            ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                        };
                    }

                    throw new InvalidOperationException(
                        $"Unable to acquire lock for idempotency key: {idempotencyKey}. Please retry.");
                }

                try
                {
                    // 3. Double-check database (fallback if Redis was cleared)
                    var hasBeenProcessed = await HasBeenProcessedInDatabaseAsync(idempotencyKey, cancellationToken);
                    if (hasBeenProcessed)
                    {
                        _logger.LogInformation(
                            "Idempotency key {Key} found in database, retrieving response",
                            idempotencyKey);

                        var dbResponse = await GetResponseFromDatabaseAsync<T>(idempotencyKey, cancellationToken);
                        if (dbResponse != null)
                        {
                            // Restore to Redis cache
                            await _redisService.SetAsync(cacheKey, dbResponse, _defaultCacheExpiration, cancellationToken);
                            
                            stopwatch.Stop();
                            return new IdempotencyResult
                            {
                                WasCached = true,
                                Response = dbResponse,
                                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                            };
                        }
                    }

                    // 4. Execute the operation (first time processing)
                    _logger.LogInformation("Processing new operation for idempotency key {Key}", idempotencyKey);
                    var result = await operation();

                    stopwatch.Stop();
                    _logger.LogInformation(
                        "Operation completed for idempotency key {Key} ({ElapsedMs}ms)",
                        idempotencyKey, stopwatch.ElapsedMilliseconds);

                    return new IdempotencyResult
                    {
                        WasCached = false,
                        Response = result,
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }
                finally
                {
                    // Always release the lock
                    await ReleaseLockAsync(lockKey, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "Error in CheckAndProcessAsync for idempotency key {Key} ({ElapsedMs}ms)",
                    idempotencyKey, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// Checks if an idempotency key has already been processed (Redis + Database)
        /// </summary>
        /// <param name="key">Idempotency key to check</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if key exists and is not expired, false otherwise</returns>
        public async Task<bool> HasBeenProcessedAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            try
            {
                var cacheKey = RedisCacheKeys.IdempotencyKey(key);

                // Check Redis first (fast path)
                if (_redisService.IsConnected)
                {
                    var existsInCache = await _redisService.ExistsAsync(cacheKey, cancellationToken);
                    if (existsInCache)
                    {
                        _logger.LogDebug("Idempotency key {Key} found in Redis cache", key);
                        return true;
                    }
                }

                // Fallback to database check
                _logger.LogDebug("Idempotency key {Key} not found in Redis, checking database", key);
                var existsInDb = await HasBeenProcessedInDatabaseAsync(key, cancellationToken);
                if (existsInDb)
                {
                    _logger.LogInformation("Idempotency key {Key} found in database but not in Redis", key);
                }
                return existsInDb;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking idempotency key: {Key}", key);
                
                // On error, fallback to database
                return await HasBeenProcessedInDatabaseAsync(key, cancellationToken);
            }
        }

        /// <summary>
        /// Gets cached response for an idempotency key
        /// </summary>
        public async Task<T?> GetCachedResponseAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            ValidateKey(key);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var cacheKey = RedisCacheKeys.IdempotencyKey(key);

                // Try Redis first
                if (_redisService.IsConnected)
                {
                    var cachedData = await _redisService.GetAsync<IdempotencyCacheData>(cacheKey, cancellationToken);
                    if (cachedData != null && !string.IsNullOrEmpty(cachedData.ResponseData))
                    {
                        stopwatch.Stop();
                        _logger.LogInformation(
                            "Retrieved cached response for idempotency key {Key} from Redis ({ElapsedMs}ms)", 
                            key, stopwatch.ElapsedMilliseconds);
                        return JsonSerializer.Deserialize<T>(cachedData.ResponseData);
                    }
                    _logger.LogDebug("Idempotency key {Key} not found in Redis cache", key);
                }
                else
                {
                    _logger.LogWarning("Redis not connected, falling back to database for key {Key}", key);
                }

                // Fallback to database
                var dbResult = await GetResponseFromDatabaseAsync<T>(key, cancellationToken);
                stopwatch.Stop();
                if (dbResult != null)
                {
                    _logger.LogInformation(
                        "Retrieved response for idempotency key {Key} from database ({ElapsedMs}ms)", 
                        key, stopwatch.ElapsedMilliseconds);
                }
                return dbResult;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error getting cached response for key: {Key} ({ElapsedMs}ms)", 
                    key, stopwatch.ElapsedMilliseconds);
                return null;
            }
        }

        /// <summary>
        /// Stores an idempotency key with associated data in both Redis and Database
        /// </summary>
        /// <param name="key">Idempotency key</param>
        /// <param name="orderId">Associated order ID</param>
        /// <param name="responseData">Response data to store</param>
        /// <param name="expiryHours">Hours until expiration (default: 24)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task StoreAsync<T>(
            string key,
            int orderId,
            T responseData,
            int expiryHours = 24,
            CancellationToken cancellationToken = default)
        {
            ValidateKey(key);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var serializedData = JsonSerializer.Serialize(responseData);
                var cacheKey = RedisCacheKeys.IdempotencyKey(key);
                var expiresAt = DateTime.UtcNow.AddHours(expiryHours);

                // Store in both Redis and Database for durability
                var tasks = new List<Task>();

                // 1. Store in Redis (fast access)
                if (_redisService.IsConnected)
                {
                    var cacheData = new IdempotencyCacheData
                    {
                        Key = key,
                        OrderId = orderId,
                        Status = "Processed",
                        ResponseData = serializedData,
                        CreatedAt = DateTime.UtcNow,
                        ExpiresAt = expiresAt
                    };

                    tasks.Add(_redisService.SetAsync(
                        cacheKey,
                        cacheData,
                        TimeSpan.FromHours(expiryHours),
                        cancellationToken));
                }

                // 2. Store in Database (durability)
                var idempotencyKey = new IdempotencyKey
                {
                    Key = key,
                    OrderId = orderId,
                    Status = "Processed",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                    ResponseData = serializedData
                };

                tasks.Add(Task.Run(async () =>
                {
                    await _dbContext.Set<IdempotencyKey>().AddAsync(idempotencyKey, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }, cancellationToken));

                // Wait for both operations
                await Task.WhenAll(tasks);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Stored idempotency key {Key} with expiry in {Hours} hours ({ElapsedMs}ms)",
                    key, expiryHours, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error storing idempotency key: {Key} ({ElapsedMs}ms)", key, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// Acquires a distributed lock for idempotency processing
        /// </summary>
        public async Task<bool> AcquireLockAsync(string key, TimeSpan lockTimeout, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_redisService.IsConnected)
                {
                    _logger.LogWarning("Redis not connected, skipping distributed lock for {Key}", key);
                    return true; // Allow processing without lock if Redis is unavailable
                }

                var lockValue = Guid.NewGuid().ToString("N");
                var lockKey = $"lock:{key}";

                // Try to set the lock with NX (only if not exists) and expiration
                var acquired = await _redisService.SetAsync(lockKey, lockValue, lockTimeout, cancellationToken);

                if (acquired)
                {
                    _logger.LogDebug("Acquired lock for {Key} with timeout {Timeout}s", key, lockTimeout.TotalSeconds);
                }
                else
                {
                    _logger.LogDebug("Failed to acquire lock for {Key}", key);
                }

                return acquired;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error acquiring lock for {Key}", key);
                return true; // Allow processing on error
            }
        }

        /// <summary>
        /// Releases a distributed lock
        /// </summary>
        public async Task<bool> ReleaseLockAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_redisService.IsConnected)
                {
                    return true;
                }

                var lockKey = $"lock:{key}";
                var released = await _redisService.DeleteAsync(lockKey, cancellationToken);

                if (released)
                {
                    _logger.LogDebug("Released lock for {Key}", key);
                }

                return released;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing lock for {Key}", key);
                return false;
            }
        }

        /// <summary>
        /// Cleans up expired idempotency keys from database
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of expired keys removed</returns>
        public async Task<int> CleanupExpiredKeysAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("Starting cleanup of expired idempotency keys");
                
                var expiredKeys = await _dbContext.Set<IdempotencyKey>()
                    .Where(ik => ik.ExpiresAt < DateTime.UtcNow)
                    .ToListAsync(cancellationToken);

                if (expiredKeys.Any())
                {
                    _dbContext.Set<IdempotencyKey>().RemoveRange(expiredKeys);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    
                    stopwatch.Stop();
                    _logger.LogInformation(
                        "Cleaned up {Count} expired idempotency keys from database ({ElapsedMs}ms)", 
                        expiredKeys.Count, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    stopwatch.Stop();
                    _logger.LogInformation("No expired idempotency keys found ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);
                }

                return expiredKeys.Count;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error cleaning up expired idempotency keys ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Checks database for idempotency key (fallback method)
        /// </summary>
        private async Task<bool> HasBeenProcessedInDatabaseAsync(string key, CancellationToken cancellationToken)
        {
            try
            {
                var idempotencyKey = await _dbContext.Set<IdempotencyKey>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ik => ik.Key == key, cancellationToken);

                if (idempotencyKey == null)
                {
                    return false;
                }

                // Check if key has expired
                if (idempotencyKey.ExpiresAt < DateTime.UtcNow)
                {
                    _logger.LogDebug("Idempotency key {Key} has expired in database", key);
                    return false;
                }

                _logger.LogDebug("Idempotency key {Key} found and valid in database", key);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking database for idempotency key: {Key}", key);
                return false;
            }
        }

        /// <summary>
        /// Gets response data from database (fallback method)
        /// </summary>
        private async Task<T?> GetResponseFromDatabaseAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            try
            {
                var idempotencyKey = await _dbContext.Set<IdempotencyKey>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ik => ik.Key == key && ik.ExpiresAt > DateTime.UtcNow, cancellationToken);

                if (idempotencyKey?.ResponseData == null)
                {
                    return null;
                }

                _logger.LogDebug("Retrieved response for idempotency key {Key} from database", key);
                return JsonSerializer.Deserialize<T>(idempotencyKey.ResponseData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting response from database for key: {Key}", key);
                return null;
            }
        }

        /// <summary>
        /// Validates idempotency key
        /// </summary>
        private void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Idempotency key cannot be empty", nameof(key));
            }

            if (key.Length > 100)
            {
                throw new ArgumentException("Idempotency key cannot exceed 100 characters", nameof(key));
            }
        }

        #endregion
    }

    /// <summary>
    /// Result of idempotency check and processing
    /// </summary>
    public class IdempotencyResult
    {
        public bool WasCached { get; set; }
        public object? Response { get; set; }
        public long ExecutionTimeMs { get; set; }
    }

    /// <summary>
    /// Cache data structure for idempotency
    /// </summary>
    public class IdempotencyCacheData
    {
        public string Key { get; set; } = string.Empty;
        public int OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ResponseData { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
