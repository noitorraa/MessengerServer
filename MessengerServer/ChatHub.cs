using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;
using MessengerServer.Services;
using Microsoft.Extensions.Logging;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly DefaultDbContext _context;
        private readonly IFileService _fileService;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(DefaultDbContext context, IFileService fileService, ILogger<ChatHub> logger)
        {
            _context = context;
            _fileService = fileService;
            _logger = logger;
        }

        // При подключении добавляем сокет в группы user_{userId} и chat_{chatId}
        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();

            if (http.Request.Query.TryGetValue("userId", out var u) && int.TryParse(u, out var userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.LogInformation("Connection {ConnectionId} added to user_{UserId}", Context.ConnectionId, userId);
            }

            if (http.Request.Query.TryGetValue("chatId", out var c) && int.TryParse(c, out var chatId))
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

                if (fileId.HasValue)
                {
                    await _context.Entry(message)
                        .Reference(m => m.File)
                        .LoadAsync();
                }

                // Получаем всех собеседников (исключая отправителя)
                var recipientIds = await _context.ChatMembers
                    .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                    .Select(cm => cm.UserId)
                    .ToListAsync();

                // Создаём статусы для каждого получателя
                var now = DateTime.UtcNow;
                var statuses = new List<MessageStatus>();

                foreach (var recId in recipientIds)
                {
                    var status = new MessageStatus
                    {
                        MessageId = message.MessageId,
                        UserId = recId,
                        Status = (int)MessageStatusType.Delivered,
                        UpdatedAt = now
                    };
                    statuses.Add(status);
                    _context.MessageStatuses.Add(status);
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // DTO для отправителя (со статусом Delivered)
                var senderDto = new MessageDto
                {
                    MessageId = message.MessageId,
                    Content = message.Content,
                    UserID = userId,
                    CreatedAt = message.CreatedAt.Value,
                    FileId = fileId,
                    FileName = message.File?.FileName,
                    FileType = message.File?.FileType,
                    Status = (int)MessageStatusType.Delivered
                };

                // DTO для получателя (без статуса)
                var recipientDto = new MessageDto
                {
                    MessageId = message.MessageId,
                    Content = message.Content,
                    UserID = userId,
                    CreatedAt = message.CreatedAt.Value,
                    FileId = fileId,
                    FileName = message.File?.FileName,
                    FileType = message.File?.FileType,
                    Status = null
                };

                // Отправляем отправителю
                await Clients.Group($"user_{userId}")
                    .SendAsync("ReceiveMessage", senderDto);

                // Отправляем всем получателям
                foreach (var recId in recipientIds)
                {
                    await Clients.Group($"user_{recId}")
                        .SendAsync("ReceiveMessage", recipientDto);
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "SendMessage error");
                throw;
            }
        }


        public async Task MarkMessageAsRead(int messageId, int recipientId)
        {
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Находим статус для обновления
                var status = await _context.MessageStatuses
                    .FirstOrDefaultAsync(ms =>
                        ms.MessageId == messageId &&
                        ms.UserId == recipientId);

                // Если статус еще не Read, обновляем
                if (status != null && status.Status < (int)MessageStatusType.Read)
                {
                    status.Status = (int)MessageStatusType.Read;
                    status.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();

                    // Уведомляем отправителя об изменении статуса
                    var senderId = await _context.Messages
                        .Where(m => m.MessageId == messageId)
                        .Select(m => m.SenderId)
                        .FirstOrDefaultAsync();

                    if (senderId != 0)
                    {
                        await Clients.Group($"user_{senderId}")
                            .SendAsync("UpdateMessageStatus", new StatusDto
                            {
                                MessageId = messageId,
                                Status = (int)MessageStatusType.Read
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error in MarkMessageAsRead");
                throw;
            }
        }


        public async Task SendFileMessage(int userId, int fileId, int chatId)
        {
            await SendMessage(userId, "[Файл]", chatId, fileId);
        }

        /// <summary>
        /// Существующий метод для пометки ВСЕХ непрочитанных сообщений в чате как прочитанных
        /// (вызывается один раз при заходе в чат)
        /// </summary>
        public async Task MarkMessagesAsRead(int chatId, int userId)
        {
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Находим статусы для входящих сообщений
                var unreadStatuses = await _context.MessageStatuses
                    .Include(ms => ms.Message)
                    .Where(ms =>
                        ms.Message.ChatId == chatId &&
                        ms.UserId == userId &&
                        ms.Status < (int)MessageStatusType.Read)
                    .ToListAsync();

                if (!unreadStatuses.Any()) return;

                var now = DateTime.UtcNow;
                var updatedMessages = new List<int>();

                foreach (var status in unreadStatuses)
                {
                    status.Status = (int)MessageStatusType.Read;
                    status.UpdatedAt = now;
                    updatedMessages.Add(status.MessageId);
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // Группируем по отправителям для пакетной отправки
                var senderGroups = await _context.Messages
                    .Where(m => updatedMessages.Contains(m.MessageId))
                    .GroupBy(m => m.SenderId)
                    .Select(g => new { SenderId = g.Key, MessageIds = g.Select(m => m.MessageId) })
                    .ToListAsync();

                foreach (var group in senderGroups)
                {
                    await Clients.Group($"user_{group.SenderId}")
                        .SendAsync("BatchUpdateStatuses", new
                        {
                            MessageIds = group.MessageIds,
                            Status = (int)MessageStatusType.Read
                        });
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error in MarkMessagesAsRead");
                throw;
            }
        }

        // Добавление подключения в группу чата
        public async Task JoinChat(int chatId) =>
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");

        // Добавление подключения в личную группу пользователя
        public async Task RegisterUser(int userId) =>
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
    }

    public enum MessageStatusType
    {
        Sent = 0,
        Delivered = 1,
        Read = 2
    }
}
