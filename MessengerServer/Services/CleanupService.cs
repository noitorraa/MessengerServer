using System.Collections.Concurrent;

namespace MessengerServer.Services
{
    public class CleanupService : ICleanupService
    {
        private static readonly ConcurrentDictionary<string, (string Code, DateTime Expires)> _pendingVerifications = new();
        private static readonly ConcurrentDictionary<string, int> _verificationRetryCount = new();

        public async Task CleanupExpiredData()
        {
            var expired = _pendingVerifications
                .Where(kvp => kvp.Value.Expires < DateTime.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var phone in expired)
            {
                _pendingVerifications.TryRemove(phone, out _);
                _verificationRetryCount.TryRemove(phone, out _);
            }
            
            await Task.CompletedTask;
        }
    }

    public interface ICleanupService
    {
        Task CleanupExpiredData();
    }
}