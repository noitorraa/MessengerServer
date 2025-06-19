using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;
using Microsoft.AspNetCore.Mvc;
using MessengerServer.Hubs;

namespace MessengerServer.Services
{
    public class MessageService : IMessageService
    {
        private readonly DefaultDbContext _context;

        public MessageService(DefaultDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> GetMessages(
            int userId,
            int chatId,
            DateTime? since = null,
            DateTime? before = null,
            int skip = 0,
            int take = 100)
        {
            var baseQuery = _context.Messages
                .Where(m => m.ChatId == chatId);

            if (since.HasValue)
                baseQuery = baseQuery.Where(m => m.CreatedAt > since.Value);

            if (before.HasValue)
                baseQuery = baseQuery.Where(m => m.CreatedAt < before.Value);

            var messages = await baseQuery
                .Include(m => m.File)
                .OrderByDescending(m => m.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(m => new
                {
                    Message = m,
                    FileInfo = m.File,
                    Status = m.SenderId == userId
                        ? m.MessageStatuses
                            .Where(ms => ms.UserId != userId)
                            .Select(ms => (int?)ms.Status)
                            .FirstOrDefault()
                        : null
                })
                .ToListAsync();

            var result = messages
                .Select(x => new MessageDto
                {
                    MessageId = x.Message.MessageId,
                    Content = x.Message.Content,
                    UserID = x.Message.SenderId ?? 0,
                    CreatedAt = x.Message.CreatedAt ?? DateTime.MinValue,
                    FileId = x.Message.FileId,
                    FileName = x.FileInfo?.FileName,
                    FileType = x.FileInfo?.FileType,
                    Status = x.Message.SenderId == userId
                        ? x.Status ?? (int)MessageStatusType.Sent
                        : null
                })
                .OrderBy(m => m.CreatedAt)
                .ToList();

            return new OkObjectResult(result);
        }
    }

    public interface IMessageService
    {
        Task<IActionResult> GetMessages(    int userId, 
    int chatId,
    [FromQuery] DateTime? since = null,
    [FromQuery] DateTime? before = null,
    [FromQuery] int skip = 0,
    [FromQuery] int take = 100);
    }
}