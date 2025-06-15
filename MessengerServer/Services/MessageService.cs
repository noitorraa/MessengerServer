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

        public async Task<IActionResult> GetMessages(
            int userId, 
            int chatId,
            [FromQuery] DateTime? since = null,
            [FromQuery] DateTime? before = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            // Получаем ID собеседника
            var otherUserId = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .FirstOrDefaultAsync();
            
            // Базовый запрос с фильтрацией по чату
            var baseQuery = _context.Messages
                .Where(m => m.ChatId == chatId);
            
            // Применяем фильтр по времени если указан
            if (since.HasValue)
            {
                baseQuery = baseQuery.Where(m => m.CreatedAt > since.Value);
            }
            if (before.HasValue)
            {
                baseQuery = baseQuery.Where(m => m.CreatedAt < before.Value);
            }
                    
            // Получаем сообщения с пагинацией
            var messages = await baseQuery
                .Include(m => m.File)
                .OrderByDescending(m => m.CreatedAt) // Сначала самые новые
                .Skip(skip)
                .Take(take)
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
                .ToListAsync();
            
            // Создаем DTO и упорядочиваем от старых к новым
            var result = messages
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
                })
                .OrderBy(m => m.CreatedAt) // Важно: возвращаем в хронологическом порядке
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