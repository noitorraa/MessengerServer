using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Hubs;
using MessengerServer.Model;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

namespace MessengerServer.Services
{
    public class ChatService : IChatService
    {
        private readonly DefaultDbContext _context;
        private readonly IServiceProvider _serviceProvider;
        private static readonly ConcurrentDictionary<string, (string Code, DateTime Expires)> _pendingVerifications = new();

        public ChatService(DefaultDbContext context, IServiceProvider serviceProvider)
        {
            _context = context;
            _serviceProvider = serviceProvider;
        }

        public async Task<IActionResult> DeleteChat(int chatId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var chat = await _context.Chats
                    .Include(c => c.ChatMembers)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.MessageStatuses)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.File)
                    .FirstOrDefaultAsync(c => c.ChatId == chatId);
                
                if (chat == null)
                    return new NotFoundObjectResult(new { Message = "Чат не найден" });

                var filesToDelete = chat.Messages
                    .Where(m => m.File != null)
                    .Select(m => m.File)
                    .ToList();
                
                _context.Files.RemoveRange(filesToDelete.Where(file => file != null)!);
                _context.MessageStatuses.RemoveRange(chat.Messages.SelectMany(m => m.MessageStatuses));
                _context.Messages.RemoveRange(chat.Messages);
                _context.ChatMembers.RemoveRange(chat.ChatMembers);
                _context.Chats.Remove(chat);
                
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var hubContext = _serviceProvider.GetRequiredService<IHubContext<ChatHub>>();
                await hubContext.Clients.Users(chat.ChatMembers.Select(cm => cm.UserId.ToString()).ToList())
                    .SendAsync("NotifyUpdateChatList");

                return new OkObjectResult(new { Message = "Чат успешно удалён" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new ObjectResult(new { Message = "Ошибка при удалении чата", Error = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        public async Task<ActionResult<List<ChatDto>>> GetUserChats(int userId)
        {
            var chats = await _context.Chats
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
                .Include(c => c.ChatMembers)
                    .ThenInclude(cm => cm.User)
                .Select(c => new ChatDto
                {
                    ChatId = c.ChatId,
                    Members = c.ChatMembers.Select(cm => new UserDto
                    {
                        UserId = cm.User.UserId,
                        Username = cm.User.Username
                    }).ToList(),
                    CreatedAt = c.CreatedAt ?? DateTime.MinValue
                })
                .ToListAsync();
            
            return new OkObjectResult(chats);
        }

        public async Task<ActionResult<Chat>> CreateChat(ChatCreationRequest request)
        {
            if (request.UserIds.Count == 2)
            {
                var existingChat = await _context.Chats
                    .Include(c => c.ChatMembers)
                    .FirstOrDefaultAsync(c =>
                        c.ChatMembers.All(cm => request.UserIds.Contains(cm.UserId)) &&
                        c.ChatMembers.Count == 2
                    );
                
                if (existingChat != null)
                {
                    return new OkObjectResult(existingChat);
                }
            }

            if (request.UserIds.Count > 2 && string.IsNullOrEmpty(request.ChatName))
            {
                request.ChatName = "New chat";
            }

            var chat = new Chat
            {
                ChatName = "",
                CreatedAt = DateTime.UtcNow
            };
            
            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            foreach (var userId in request.UserIds)
            {
                _context.ChatMembers.Add(new ChatMember
                {
                    ChatId = chat.ChatId,
                    UserId = userId
                });
            }
            
            await _context.SaveChangesAsync();

            var hubContext = _serviceProvider.GetRequiredService<IHubContext<ChatHub>>();
            foreach (var id in request.UserIds)
            {
                await hubContext.Clients
                    .Group($"user_{id}")
                    .SendAsync("NotifyUpdateChatList");
            }
            
            return new OkObjectResult(chat);
        }

        public async Task<IActionResult> CleanupExpiredData()
        {
            var expired = _pendingVerifications
                .Where(kvp => kvp.Value.Expires < DateTime.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var phone in expired)
            {
                _pendingVerifications.TryRemove(phone, out _);
            }
            
            return await Task.FromResult(new OkResult());
        }
    }
    public interface IChatService
    {
        Task<IActionResult> DeleteChat(int chatId);
        Task<ActionResult<List<ChatDto>>> GetUserChats(int userId);
        Task<ActionResult<Chat>> CreateChat(ChatCreationRequest request);
        Task<IActionResult> CleanupExpiredData();
    }
}