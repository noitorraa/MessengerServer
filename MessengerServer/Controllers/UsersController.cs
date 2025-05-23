using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using MessengerServer.Hubs;
using MessengerServer.Model;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace MessengerServer.Controllers
{
    [ApiController] // тут получаем данные из базы данных 
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private const long MAX_FILE_SIZE = 10_000_000;
        private readonly DefaultDbContext _context;
        private readonly IServiceProvider _serviceProvider;
        private readonly Timer _cleanupTimer;

        // Хранилище для кодов сброса паролей
        private static readonly ConcurrentDictionary<string, (string Code, DateTime Expires)> _smsResetCodes = new();
        private static readonly ConcurrentDictionary<string, int> _smsResetRetryCount = new();


        // Новое хранилище для кода подтверждения регистрации
        private static readonly ConcurrentDictionary<string, (string Code, DateTime Expires)> _pendingVerifications = new();
        private static readonly ConcurrentDictionary<string, int> _verificationRetryCount = new();

        // Хранилище подтверждённых номеров телефонов
        private static readonly ConcurrentDictionary<string, bool> _verifiedPhones = new();


        public UsersController(DefaultDbContext context, IServiceProvider serviceProvider)
        {
            _context = context;
            _serviceProvider = serviceProvider;
            _cleanupTimer = new Timer(CleanupExpiredData, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        }

        private async void CleanupExpiredData(object? state)
        {
            // Очистка просроченных кодов для регистрации
            var expired = _pendingVerifications
                .Where(kvp => kvp.Value.Expires < DateTime.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var phone in expired)
            {
                _pendingVerifications.TryRemove(phone, out _);
                _verificationRetryCount.TryRemove(phone, out _);
            }

            // Если требуется, можно добавить очистку для подтверждённых номеров
            await Task.CompletedTask;
        }

        [HttpGet("authorization")]
        public async Task<ActionResult<User>> GetUserByLoginAndPassword(string login, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == login);
            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                return NotFound(new { Message = "Пользователь не найден" });
            }
            return Ok(user);
        }

        [HttpPost("send-verification-code")]
        public IActionResult SendVerificationCode(string phone)
        {
            // Приводим номер к стандартному виду – оставляем только цифры
            phone = Regex.Replace(phone ?? "", @"[^\d]", "");
            if (string.IsNullOrEmpty(phone))
            {
                return BadRequest("Неверный формат телефона");
            }

            // Ограничение количества попыток
            var retry = _verificationRetryCount.AddOrUpdate(phone, 1, (k, v) => v + 1);
            if (retry > 5)
            {
                return BadRequest("Слишком много попыток. Попробуйте позже.");
            }

            // Генерируем случайный 6-значный код
            var code = new Random().Next(100000, 999999).ToString();
            var expiration = DateTime.UtcNow.AddMinutes(5);
            _pendingVerifications[phone] = (code, expiration);

            Console.WriteLine($"Code: {code}, phone: {phone}");
            // Здесь нужно добавить вызов API для реальной отправки СМС, например:
            // await _smsService.SendAsync(phone, $"Ваш код: {code}");

            return Ok("Код отправлен");
        }

        // Endpoint для проверки кода подтверждения регистрации
        [HttpPost("verify-code")]
        public IActionResult VerifyCode(string phone, string code)
        {
            // Приводим номер к стандартному виду
            phone = Regex.Replace(phone ?? "", @"[^\d]", "");
            if (!_pendingVerifications.TryGetValue(phone, out var codeInfo))
            {
                return BadRequest("Код не найден или просрочен");
            }
            if (codeInfo.Expires < DateTime.UtcNow)
            {
                _pendingVerifications.TryRemove(phone, out _);
                return BadRequest("Срок действия кода истёк");
            }
            if (codeInfo.Code != code)
            {
                return BadRequest("Неверный код подтверждения");
            }
            // После успешной проверки удаляем код и помечаем номер как подтверждённый
            _pendingVerifications.TryRemove(phone, out _);
            _verifiedPhones[phone] = true;
            return Ok("Код подтверждён");
        }

        [HttpGet("chats/{userId}")]
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

            return Ok(chats);
        }

        [HttpGet("chats/{chatId}/{userId}/messages")] // Изменить
        public async Task<IActionResult> GetMessages(int userId, int chatId)
        {
            // Получаем ID второго участника чата
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
                // Исправленные строки:
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
            })
    .ToListAsync();

            return Ok(messages);
        }


        [HttpDelete("chats/{chatId}")]
        public async Task<IActionResult> DeleteChat(int chatId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var chat = await _context.Chats
                    .Include(c => c.ChatMembers)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.MessageStatuses)
                    .Include(c => c.Messages) // Добавлено: подгрузка файлов
                        .ThenInclude(m => m.File)
                    .FirstOrDefaultAsync(c => c.ChatId == chatId);

                if (chat == null)
                    return NotFound(new { Message = "Чат не найден" });

                // Удаляем файлы, связанные с сообщениями
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

                // Уведомление через SignalR
                var hubContext = _serviceProvider.GetRequiredService<IHubContext<ChatHub>>();
                await hubContext.Clients.Users(chat.ChatMembers.Select(cm => cm.UserId.ToString()).ToList())
                    .SendAsync("NotifyUpdateChatList");

                return Ok(new { Message = "Чат успешно удалён" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "Ошибка при удалении чата", Error = ex.Message });
            }
        }


        [HttpPost("registration")]
        public async Task<IActionResult> Registration([FromBody] User user)
        {
            // Чистим номер телефона от лишних символов
            user.PhoneNumber = Regex.Replace(user.PhoneNumber ?? "", @"[^\d]", "");
            // Проверяем, что номер был ранее подтверждён
            if (!_verifiedPhones.TryGetValue(user.PhoneNumber, out var verified) || !verified)
            {
                return BadRequest("Номер телефона не подтверждён");
            }
            // Удаляем запись о подтверждении, чтобы не использовать её повторно
            _verifiedPhones.TryRemove(user.PhoneNumber, out _);

            if (await _context.Users.AnyAsync(u => u.Username == user.Username))
            {
                return Conflict(new { Message = "Username already exists." });
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            }
            if (user.PhoneNumber.Length < 10 || user.PhoneNumber.Length > 15)
            {
                return BadRequest("Неверный формат телефона");
            }
            // Хэшируем пароль (на сервере требуется хранить хэш, а не сырой пароль)
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "User registered successfully." });
        }

        [HttpGet("search")]
        public async Task<ActionResult<List<User>>> SearchUsersByLogin(string login)
        {
            // Ищем пользователей, у которых логин содержит переданную строку
            var users = await _context.Users
                                      .Where(u => u.Username.Contains(login))
                                      .ToListAsync();

            if (users == null || !users.Any())
            {
                return NotFound(new { Message = "Пользователи не найдены" });
            }

            return Ok(users);
        }

        [HttpPost("chats")]
        public async Task<ActionResult<Chat>> CreateChat([FromBody] ChatCreationRequest request)
        {
            // Проверяем существующий чат между пользователями (только для чатов с двумя участниками)
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
                    return Ok(existingChat); // Возвращаем существующий чат
                }
            }

            if (request.UserIds.Count > 2 && string.IsNullOrEmpty(request.ChatName))
            {
                request.ChatName = "New chat";
            }

            // Создаём новый чат, если дубликата нет
            var chat = new Chat
            {
                ChatName = "", // Поле больше не используется для личных чатов
                CreatedAt = DateTime.UtcNow
            };

            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            // Добавляем участников
            foreach (var userId in request.UserIds)
            {
                _context.ChatMembers.Add(new ChatMember
                {
                    ChatId = chat.ChatId,
                    UserId = userId
                });
            }

            await _context.SaveChangesAsync();

            // Отправляем уведомление через SignalR
            var hubContext = _serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<ChatHub>>();
            foreach (var id in request.UserIds)
            {
                await hubContext.Clients
                    .Group($"user_{id}")
                    .SendAsync("NotifyUpdateChatList");
            }

            return Ok(chat);
        }

        [HttpPost("send-reset-code")]
        public IActionResult SendResetCode(string phone)
        {
            // Приводим номер к стандартному виду: оставляем только цифры
            phone = Regex.Replace(phone ?? "", @"[^\d]", "");

            // Ищем пользователя с таким телефоном
            var user = _context.Users.FirstOrDefault(u => u.PhoneNumber == phone);
            if (user == null)
            {
                return NotFound("Пользователь не найден");
            }

            // Ограничение количества попыток отправки кода (максимум 5)
            int retry = _smsResetRetryCount.AddOrUpdate(phone, 1, (k, v) => v + 1);
            if (retry > 5)
            {
                return BadRequest("Слишком много попыток. Попробуйте позже.");
            }

            // Генерируем случайный 6-значный код и устанавливаем время его жизни (5 минут)
            var code = new Random().Next(100000, 999999).ToString();
            var expiration = DateTime.UtcNow.AddMinutes(5);
            _smsResetCodes[phone] = (code, expiration);

            Console.WriteLine($"Reset Code: {code}, phone: {phone}");
            // Здесь необходимо вызвать реальный API для отправки SMS, например:
            // await _smsService.SendAsync(phone, $"Ваш код для сброса пароля: {code}");

            return Ok("Код отправлен");
        }


        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetModel model)
        {
            // Приводим номер к стандартному виду
            string phone = Regex.Replace(model.Phone ?? "", @"[^\d]", "");

            // Проверяем, существует ли пользователь с таким телефоном
            var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone);
            if (user == null)
            {
                return NotFound("Пользователь не найден");
            }

            // Проверяем наличие кода в памяти
            if (!_smsResetCodes.TryGetValue(phone, out var codeInfo))
            {
                return BadRequest("Код не найден или просрочен");
            }

            // Если время действия кода истекло, удаляем запись и возвращаем ошибку
            if (codeInfo.Expires < DateTime.UtcNow)
            {
                _smsResetCodes.TryRemove(phone, out _);
                return BadRequest("Срок действия кода истек");
            }

            // Проверяем соответствие введенного кода отправленному
            if (codeInfo.Code != model.Code)
            {
                return BadRequest("Неверный код подтверждения");
            }

            // Если проверка кода прошла успешно, обновляем пароль пользователя
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);

            // Очищаем код из памяти, чтобы повторно им нельзя было воспользоваться
            _smsResetCodes.TryRemove(phone, out _);

            await _context.SaveChangesAsync();

            return Ok("Пароль успешно изменен");
        }


        [HttpGet("chats/existing")]
        public async Task<ActionResult<Chat>> GetExistingChat(int user1Id, int user2Id)
        {
            var chat = await _context.Chats
                .Include(c => c.ChatMembers)
                .FirstOrDefaultAsync(c =>
                    c.ChatMembers.Any(cm => cm.UserId == user1Id) &&
                    c.ChatMembers.Any(cm => cm.UserId == user2Id) &&
                    c.ChatMembers.Count == 2
                );

            return chat != null ? Ok(chat) : NotFound();
        }

        [HttpPost("upload/{chatId}/{userId}")]
        [RequestSizeLimit(MAX_FILE_SIZE)]
        [RequestFormLimits(MultipartBodyLengthLimit = MAX_FILE_SIZE)]
        public async Task<IActionResult> UploadFile(
    [FromRoute] int chatId,
    [FromRoute] int userId,
    IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Файл не загружен");

            // Проверка размера файла (для MEDIUMBLOB - 16 МБ)
            if (file.Length > 16 * 1024 * 1024)
                return BadRequest("Максимальный размер файла: 16 МБ");

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);

            // Создаем запись о файле
            var fileEntity = new Model.File
            {
                FileName = file.FileName,
                FileType = file.ContentType,
                FileData = memoryStream.ToArray(),
                CreatedAt = DateTime.UtcNow
            };

            _context.Files.Add(fileEntity);
            await _context.SaveChangesAsync(); // Сохраняем файл, чтобы получить FileId

            // Создаем сообщение с привязкой к файлу
            var message = new Message
            {
                ChatId = chatId,
                SenderId = userId,
                Content = "Файл",
                FileId = fileEntity.FileId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            return Ok(new { FileId = fileEntity.FileId });
        }


        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(int fileId)
        {
            var file = await _context.Files.FindAsync(fileId);
            if (file == null)
                return NotFound();

            return File(
                file.FileData,
                file.FileType,
                file.FileName);
        }


        public class ValidateFileAttribute : Attribute, IParameterModelConvention
        {
            private readonly string[] _allowedExtensions;
            private readonly long _maxSize;

            public ValidateFileAttribute(string[] allowedExtensions, long maxSize)
            {
                _allowedExtensions = allowedExtensions;
                _maxSize = maxSize;
            }

            public void Apply(ParameterModel parameter)
            {
                if (parameter.ParameterType == typeof(IFormFile))
                {
                    parameter.Action.Filters.Add(new ValidateFileFilter(_allowedExtensions, _maxSize));
                }
            }
        }

        public class ValidateFileFilter : IAsyncActionFilter
        {
            private readonly string[] _allowedExtensions;
            private readonly long _maxSize;

            public ValidateFileFilter(string[] allowedExtensions, long maxSize)
            {
                _allowedExtensions = allowedExtensions;
                _maxSize = maxSize;
            }

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) // потом реализовать
            {
                if (!context.ActionArguments.TryGetValue("file", out var fileObj) ||
                    !(fileObj is IFormFile file))
                {
                    context.Result = new BadRequestObjectResult("File required");
                    return;
                }

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_allowedExtensions.Contains(extension))
                {
                    context.Result = new BadRequestObjectResult("Invalid file type");
                    return;
                }

                if (file.Length > _maxSize)
                {
                    context.Result = new BadRequestObjectResult("File too large");
                    return;
                }

                await next();
            }

            
        }

        public class ResetModel
        {
            public required string Phone { get; set; }
            public string? Code { get; set; }
            public string? NewPassword { get; set; }
        }

        public class MessageDto
        {
            [JsonProperty("messageId")]
            public int MessageId { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }

            [JsonProperty("userID")]
            public int UserID { get; set; }

            [JsonProperty("createdAt")]
            public DateTime CreatedAt { get; set; }

            // Обновленные поля для файлов
            [JsonProperty("fileId")]
            public int? FileId { get; set; }

            [JsonProperty("fileName")]
            public string? FileName { get; set; } // Новое поле

            [JsonProperty("fileType")]
            public string? FileType { get; set; }

            [JsonProperty("status")]
            public int Status { get; set; }
        }

        public class ChatDto
        {
            public int ChatId { get; set; }
            public List<UserDto>? Members { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class UserDto
        {
            public int UserId { get; set; }
            public string? Username { get; set; }
        }

        public class ChatCreationRequest
        {
            public string? ChatName { get; set; }
            public List<int>? UserIds { get; set; }
        }

        public enum MessageStatusType
        {
            Sent = 0,       // Отправлено
            Delivered = 1,  // Доставлено
            Read = 2        // Прочитано
        }
    }
}