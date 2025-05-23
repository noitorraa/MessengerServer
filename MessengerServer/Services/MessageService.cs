using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;
using Microsoft.AspNetCore.Mvc;

namespace MessengerServer.Services
{
    public class MessageService : IMessageService
    {
        private readonly DefaultDbContext _context;

        public MessageService(DefaultDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> GetMessages(int userId, int chatId)
        {
            var otherUserId = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .FirstOrDefaultAsync();
            
            var messages = await _context.Messages
                .Where(m => m.ChatId == chatId)
                .Include(m => m.File)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    Message = m,
                    FileInfo = m.File,
                    StatusForCurrentUser = m.MessageStatuses
                        .Where(ms => ms.UserId == userId)
                        .Select(ms => (int?)ms.Status)
                        .FirstOrDefault(),
                    StatusForRecipient = m.MessageStatuses
                        .Where(ms => ms.UserId == otherUserId)
                        .Select(ms => (int?)ms.Status)
                        .FirstOrDefault()
                })
                .Select(x => new MessageDto
                {
                    MessageId = x.Message.MessageId,
                    Content = x.Message.Content,
                    UserID = x.Message.SenderId ?? 0,
                    CreatedAt = x.Message.CreatedAt ?? DateTime.MinValue,
                    FileId = x.Message.FileId,
                    FileName = x.FileInfo != null ? x.FileInfo.FileName : null,
                    FileType = x.FileInfo != null ? x.FileInfo.FileType : null,
                    Status = x.Message.SenderId == userId
                        ? x.StatusForRecipient ?? 0
                        : x.StatusForCurrentUser ?? 0
                }).ToListAsync();
            
            return new OkObjectResult(messages);
        }
    }

    public interface IMessageService
    {
        Task<IActionResult> GetMessages(int userId, int chatId);
    }
}