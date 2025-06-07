using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;
using MessengerServer.Services;
using System.Linq;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly DefaultDbContext _context;

        public ChatHub(DefaultDbContext context)
        {
            _context = context;
        }

        // Отправка текстового сообщения
        public async Task SendMessage(int userId, string message, int chatId)
        {
            var newMessage = new Message
            {
                Content = message,
                SenderId = userId,
                ChatId = chatId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            var recipients = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            var messageDto = new MessageDto
            {
                MessageId = newMessage.MessageId,
                Content = newMessage.Content,
                UserID = userId,
                CreatedAt = (DateTime)newMessage.CreatedAt
            };

            foreach (var recipientId in recipients)
            {
                await UpdateMessageStatus(newMessage.MessageId, recipientId, (int)MessageStatusType.Delivered);
            }

            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", messageDto);
        }

        // Пометка сообщений как прочитанных
        public async Task MarkMessagesAsRead(int chatId, int userId)
        {
            // Упрощенный запрос для непрочитанных сообщений
            var unreadMessages = await _context.Messages
                .Where(m => m.ChatId == chatId && m.SenderId != userId)
                .ToListAsync();

            // Фильтрация на клиентской стороне для упрощения
            var unreadMessageIds = unreadMessages
                .Where(m => !m.MessageStatuses.Any(ms => 
                    ms.UserId == userId && ms.Status >= (int)MessageStatusType.Read))
                .Select(m => m.MessageId)
                .ToList();

            if (!unreadMessageIds.Any())
                return;

            var now = DateTime.UtcNow;
            var readStatus = (int)MessageStatusType.Read;
            var statusDtos = new List<StatusDto>();

            foreach (var messageId in unreadMessageIds)
            {
                var statusEntry = await _context.MessageStatuses
                    .FirstOrDefaultAsync(ms => ms.MessageId == messageId && ms.UserId == userId);

                if (statusEntry == null)
                {
                    statusEntry = new MessageStatus
                    {
                        MessageId = messageId,
                        UserId = userId,
                        Status = readStatus,
                        UpdatedAt = now
                    };
                    _context.MessageStatuses.Add(statusEntry);
                }
                else
                {
                    statusEntry.Status = readStatus;
                    statusEntry.UpdatedAt = now;
                }

                statusDtos.Add(new StatusDto
                {
                    MessageId = messageId,
                    Status = readStatus
                });
            }

            await _context.SaveChangesAsync();
            await Clients.Group($"user_{userId}").SendAsync("BatchUpdateStatuses", statusDtos);
        }

        // Обновление статуса сообщения
        private async Task UpdateMessageStatus(int messageId, int userId, int status)
        {
            var statusEntry = await _context.MessageStatuses
                .FirstOrDefaultAsync(ms => ms.MessageId == messageId && ms.UserId == userId);

            if (statusEntry == null)
            {
                statusEntry = new MessageStatus
                {
                    MessageId = messageId,
                    UserId = userId,
                    Status = status,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.MessageStatuses.Add(statusEntry);
            }
            else
            {
                statusEntry.Status = status;
                statusEntry.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            
            var statusDto = new StatusDto
            {
                MessageId = messageId,
                Status = status
            };
            
            await Clients.Group($"user_{userId}").SendAsync("UpdateMessageStatus", statusDto);
        }

        // Вход в группу чата
        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        }

        // Регистрация пользователя в персональной группе
        public async Task RegisterUser(int userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }
    }
    
    public enum MessageStatusType
    {
        Sent = 0,
        Delivered = 1,
        Read = 2
    }
    
    // DTO классы
    public class MessageDto
    {
        public int MessageId { get; set; }
        public string Content { get; set; }
        public int UserID { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    
    public class StatusDto
    {
        public int MessageId { get; set; }
        public int Status { get; set; }
    }
}