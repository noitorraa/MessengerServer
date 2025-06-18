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

                // Подгружаем файл, если есть
                if (fileId.HasValue)
                {
                    await _context.Entry(message)
                        .Reference(m => m.File)
                        .LoadAsync();
                }

                // создаём статусы для всех участников: Sent для автора, Delivered для остальных
                var members = await _context.ChatMembers
                    .Where(cm => cm.ChatId == chatId)
                    .Select(cm => cm.UserId)
                    .ToListAsync();

                var now = DateTime.UtcNow;
                var statuses = members.Select(id => new MessageStatus
                {
                    MessageId = message.MessageId,
                    UserId = id,
                    Status = id == userId
                        ? (int)MessageStatusType.Sent
                        : (int)MessageStatusType.Delivered,
                    UpdatedAt = now
                }).ToList();

                _context.MessageStatuses.AddRange(statuses);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // DTO для рассылки
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

                // всем в чате — новое сообщение
                await Clients.Group($"chat_{chatId}")
                    .SendAsync("ReceiveMessage", dto);

                // каждому участнику — его статус Delivered/Sent
                var senderStatus = statuses.First(st => st.UserId == message.SenderId);
                await Clients.Group($"user_{message.SenderId}")
                    .SendAsync("UpdateMessageStatus", new StatusDto
                    {
                        MessageId = message.MessageId,
                        Status = senderStatus.Status
                    });
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
            await SendMessage(userId, "[Файл]", chatId, fileId);
        }

        /// <summary>
        /// **CHANGED**: Новый метод для пометки одного сообщения как прочитанного
        /// Вызывается при получении каждого нового сообщения в клиенте
        /// </summary>
        public async Task MarkMessageAsRead(int messageId, int userId)
        {
            // Просто делегируем в общий метод обновления статуса
            await UpdateMessageStatus(messageId, userId, (int)MessageStatusType.Read);
        }

        /// <summary>
        /// Существующий метод для пометки ВСЕХ непрочитанных сообщений в чате как прочитанных
        /// (вызывается один раз при заходе в чат)
        /// </summary>
        public async Task MarkMessagesAsRead(int chatId, int userId)
        {
            var unread = await _context.MessageStatuses
                .Include(ms => ms.Message)
                .Where(ms => ms.Message.ChatId == chatId
                            && ms.UserId == userId
                            && ms.Status < (int)MessageStatusType.Read)
                .ToListAsync();

            if (!unread.Any()) return;

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

            foreach (var senderId in unread.Select(ms => ms.Message.SenderId).Distinct())
            {
                await Clients.Group($"user_{senderId}")
                    .SendAsync("BatchUpdateStatuses", statusDtos);
            }
        }


        /// <summary>
        /// Общий метод для обновления статуса одного сообщения и рассылки уведомлений
        /// </summary>
        private async Task UpdateMessageStatus(int messageId, int userId, int status)
        {
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var ms = await _context.MessageStatuses
                    .FirstOrDefaultAsync(x => x.MessageId == messageId && x.UserId == userId);

                if (ms == null)
                {
                    ms = new MessageStatus
                    {
                        MessageId = messageId,
                        UserId = userId,
                        Status = status,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.MessageStatuses.Add(ms);
                }
                else
                {
                    ms.Status = status;
                    ms.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                var dto = new StatusDto
                {
                    MessageId = messageId,
                    Status = status
                };

                // уведомляем автора
                var message = await _context.Messages.FindAsync(messageId);
                if (message != null)
                {
                    await Clients.Group($"user_{message.SenderId}")
                        .SendAsync("UpdateMessageStatus", dto);
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error in UpdateMessageStatus");
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
