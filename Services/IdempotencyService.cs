using OrderProcessingSystem.Data;
using OrderProcessingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace OrderProcessingSystem.Services
{
    /// <summary>
    /// Service for managing idempotency keys to prevent duplicate operations
    /// </summary>
    public class IdempotencyService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<IdempotencyService> _logger;

        public IdempotencyService(
            AppDbContext dbContext,
            ILogger<IdempotencyService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Checks if an idempotency key has already been processed
        /// </summary>
        /// <param name="key">Idempotency key to check</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if key exists and is not expired, false otherwise</returns>
        public async Task<bool> HasBeenProcessedAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Idempotency key cannot be empty", nameof(key));
            }

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
                    _logger.LogInformation("Idempotency key {Key} has expired", key);
                    return false;
                }

                _logger.LogInformation("Idempotency key {Key} found and is valid", key);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking idempotency key: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// Stores an idempotency key with associated data
        /// </summary>
        /// <param name="key">Idempotency key</param>
        /// <param name="orderId">Associated order ID</param>
        /// <param name="responseData">Response data to store</param>
        /// <param name="expiryHours">Hours until expiration (default: 24)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task StoreAsync(
            string key,
            int orderId,
            string responseData,
            int expiryHours = 24,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Idempotency key cannot be empty", nameof(key));
            }

            try
            {
                var idempotencyKey = new IdempotencyKey
                {
                    Key = key,
                    OrderId = orderId,
                    Status = "Processed",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(expiryHours),
                    ResponseData = responseData
                };

                await _dbContext.Set<IdempotencyKey>().AddAsync(idempotencyKey, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Stored idempotency key {Key} with expiry in {Hours} hours", key, expiryHours);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing idempotency key: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// Cleans up expired idempotency keys
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of expired keys removed</returns>
        public async Task<int> CleanupExpiredKeysAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var expiredKeys = await _dbContext.Set<IdempotencyKey>()
                    .Where(ik => ik.ExpiresAt < DateTime.UtcNow)
                    .ToListAsync(cancellationToken);

                if (expiredKeys.Any())
                {
                    _dbContext.Set<IdempotencyKey>().RemoveRange(expiredKeys);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation("Cleaned up {Count} expired idempotency keys", expiredKeys.Count);
                }

                return expiredKeys.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired idempotency keys");
                throw;
            }
        }
    }
}
