using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;
using static MessengerServer.Controllers.UsersController;
using MessengerServer.Services;
using Microsoft.Extensions.Logging;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly DefaultDbContext _context;
        private readonly IFileService  _fileService;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(DefaultDbContext context, IFileService  fileService, ILogger<ChatHub> logger)
        {
            _context = context;
            _fileService = fileService;
            _logger = logger;
        }

        // При подключении добавляем в группы, если указаны query-параметры
        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();
            if (http.Request.Query.TryGetValue("userId", out var u)
                && int.TryParse(u, out var userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.LogInformation("Connection {ConnectionId} added to user_{UserId}", Context.ConnectionId, userId);
            }
            if (http.Request.Query.TryGetValue("chatId", out var c)
                && int.TryParse(c, out var chatId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
                _logger.LogInformation("Connection {ConnectionId} added to chat_{ChatId}", Context.ConnectionId, chatId);
            }
            else
            {
                _logger.LogInformation("Connection {ConnectionId} connected without chatId query", Context.ConnectionId);
            }
            await base.OnConnectedAsync();
        }

        public async Task SendMessage(int userId, string content, int chatId, int? fileId = null)
        {
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var message = new Message
                {
                    SenderId = userId,
                    ChatId = chatId,
                    Content = content,
                    FileId = fileId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Messages.Add(message);
                await _context.SaveChangesAsync();

                // создаём статусы для всех участников чата
                var members = await _context.ChatMembers
                    .Where(cm => cm.ChatId == chatId)
                    .Select(cm => cm.UserId)
                    .ToListAsync();
                _logger.LogInformation("_sendMessage: chat {ChatId} has {MemberCount} members", chatId, members.Count);

                var now = DateTime.UtcNow;
                var statuses = members
                    .Select(id => new MessageStatus
                    {
                        MessageId = message.MessageId,
                        UserId = id,
                        Status = id == userId ? (int)MessageStatusType.Sent : (int)MessageStatusType.Delivered,
                        UpdatedAt = now
                    }).ToList();
                _context.MessageStatuses.AddRange(statuses);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                var dto = new MessageDto
                {
                    MessageId = message.MessageId,
                    Content = message.Content,
                    UserID = userId,
                    CreatedAt = message.CreatedAt.Value,
                    FileId = fileId,
                    FileName = message.File?.FileName,
                    FileType = message.File?.FileType,
                    Status = (int)MessageStatusType.Sent
                };

                // уведомляем участников чата о новом сообщении
                await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", dto);

                // уведомляем каждого участника о статусе Delivered/Sent
                foreach (var st in statuses)
                {
                    await Clients.Group($"user_{st.UserId}")
                        .SendAsync("UpdateMessageStatus", new StatusDto
                        {
                            MessageId = st.MessageId,
                            Status = st.Status
                        });
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "SendMessage error");
                throw;
            }
        }

        public async Task SendFileMessage(int userId, int fileId, int chatId)
        {
            //await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", messageDto);
        }

        public async Task MarkMessagesAsRead(int chatId, int userId)
        {
            var unread = await _context.MessageStatuses
                .Include(ms => ms.Message)
                .Where(ms => ms.Message.ChatId == chatId && ms.UserId == userId && ms.Status < (int)MessageStatusType.Read)
                .ToListAsync();
            _logger.LogInformation("MarkMessagesAsRead: found {Count} unread statuses for user {UserId} in chat {ChatId}", unread.Count, userId, chatId);

            if (!unread.Any())
            {
                _logger.LogInformation("No unread messages to update for user {UserId} in chat {ChatId}", userId, chatId);
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var ms in unread)
            {
                ms.Status = (int)MessageStatusType.Read;
                ms.UpdatedAt = now;
            }
            await _context.SaveChangesAsync();

            var statusDtos = unread.Select(ms => new StatusDto
            {
                MessageId = ms.MessageId,
                Status = ms.Status
            }).ToList();

            // уведомляем читающего пользователя
            await Clients.Group($"user_{userId}").SendAsync("BatchUpdateStatuses", statusDtos);

            // уведомляем авторов сообщений
            var senderGroups = unread
                .Select(ms => ms.Message.SenderId.Value)
                .Distinct();
            foreach (var senderId in senderGroups)
            {
                await Clients.Group($"user_{senderId}")
                    .SendAsync("BatchUpdateStatuses", statusDtos);
            }
        }

        private async Task UpdateMessageStatus(int messageId, int userId, int status)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            
            try
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
                await transaction.CommitAsync();

                var statusDto = new StatusDto
                {
                    MessageId = messageId,
                    Status = status
                };

                // Отправляем обновление получателю
                await Clients.Group($"user_{userId}").SendAsync("UpdateMessageStatus", statusDto);

                // Отправляем обновление отправителю
                var message = await _context.Messages.FindAsync(messageId);
                if (message != null)
                {
                    await Clients.Group($"user_{message.SenderId.Value}").SendAsync("UpdateMessageStatus", statusDto);
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error updating message status");
                throw;
            }
        }

        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        }

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
}
