using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Models;
using Org.BouncyCastle.Crypto.Generators;
using Microsoft.Extensions.DependencyInjection;
using MessengerServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNet.SignalR;
using Org.BouncyCastle.Asn1.Ocsp;
using Newtonsoft.Json;
using System.Text.Json.Serialization;
using Amazon.S3.Model;
using Amazon.S3;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNet.SignalR.Hubs;
using Amazon.DynamoDBv2.Model;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Numerics;
using System;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using File = MessengerServer.Models.File;

namespace MessengerServer.Controllers
{
    [ApiController] // тут получаем данные из базы данных 
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IAmazonS3 _s3;
        private readonly ILogger<UsersController> _logger;
        private readonly string _bucket;
        private readonly DefaultDbContext _db;
        private readonly string _publicUrl;
        private const long MAX_FILE_SIZE = 10_000_000;
        private readonly IWebHostEnvironment _env;
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


        public UsersController(DefaultDbContext context, IServiceProvider serviceProvider, IWebHostEnvironment env, IAmazonS3 s3,
        IConfiguration cfg,
        ILogger<UsersController> logger,
        DefaultDbContext db)
        {
            _s3 = s3;
            _logger = logger;
            _db = db;
            _bucket = cfg["SwiftConfig:BucketName"];
            _publicUrl = cfg["SwiftConfig:PublicUrl"];
            _env = env;
            _context = context;
            _serviceProvider = serviceProvider;
            _cleanupTimer = new Timer(CleanupExpiredData, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        }

        private async void CleanupExpiredData(object state)
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
                    CreatedAt = (DateTime)c.CreatedAt
                })
                .ToListAsync();

            return Ok(chats);
        }

        [HttpGet("chats/{chatId}/{userId}/messages")]
        public async Task<IActionResult> GetMessages(int userId, int chatId)
        {
            // Получаем ID второго участника чата
            var otherUserId = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .FirstOrDefaultAsync();

            var messages = await _context.Messages
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    Message = m,
                    StatusForCurrentUser = m.MessageStatuses
                        .Where(ms => ms.UserId == userId)
                        .Select(ms => ms.Status)
                        .FirstOrDefault(),
                    StatusForRecipient = m.MessageStatuses
                        .Where(ms => ms.UserId == otherUserId)
                        .Select(ms => ms.Status)
                        .FirstOrDefault()
                })
                .Select(x => new MessageDto
                {
                    MessageId = x.Message.MessageId,
                    Content = x.Message.Content,
                    UserID = (int)x.Message.SenderId,
                    CreatedAt = (DateTime)x.Message.CreatedAt,
                    FileId = x.Message.FileId,
                    FileType = x.Message.File != null ? x.Message.File.FileType : null,
                    FileUrl = x.Message.File != null ? x.Message.File.FileUrl : null,
                    Status = x.Message.SenderId == userId
                        ? x.StatusForRecipient // Для своих сообщений берем статус получателя
                        : x.StatusForCurrentUser // Для чужих сообщений берем свой статус
                })
                .ToListAsync();

            return Ok(messages);
        }


        [HttpDelete("chats/{chatId}")]
        public async Task<IActionResult> DeleteChat(int chatId)
        {
            // Начинаем транзакцию
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var chat = await _context.Chats
                    .Include(c => c.ChatMembers)
                    .Include(c => c.Messages)
                        .ThenInclude(m => m.MessageStatuses)
                    .FirstOrDefaultAsync(c => c.ChatId == chatId);

                if (chat == null)
                    return NotFound(new { Message = "Чат не найден" });

                var messages = chat.Messages;
                var statuses = messages.SelectMany(m => m.MessageStatuses).ToList();

                _context.MessageStatuses.RemoveRange(statuses);
                _context.Messages.RemoveRange(messages);
                _context.ChatMembers.RemoveRange(chat.ChatMembers);
                _context.Chats.Remove(chat);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync(); // Подтверждаем все изменения

                // После успешного удаления — уведомляем пользователей
                var hubContext = _serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<ChatHub>>();
                await hubContext.Clients.Users(chat.ChatMembers.Select(cm => cm.UserId.ToString()).ToList())
                    .SendAsync("NotifyUpdateChatList");

                return Ok(new { Message = "Чат успешно удалён" });
            }
            catch (Exception ex)
            {
                // Если что-то пошло не так — откатываем все изменения
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
            await hubContext.Clients.Users(request.UserIds.Select(u => u.ToString()).ToList())
                .SendAsync("NotifyUpdateChatList");

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
        public async Task<IActionResult> UploadFile([FromRoute] int chatId, [FromRoute] int userId, IFormFile file)
        {
            if (file.Length == 0 || file.Length > MAX_FILE_SIZE)
                return BadRequest("Неверный размер файла"); 

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".png", ".pdf", ".docx" };
            if (string.IsNullOrEmpty(ext) || !allowed.Contains(ext))
                return BadRequest("Неподдерживаемое расширение"); 

            // 2) Генерируем ключ и загружаем в S3
            var key = $"{Guid.NewGuid()}{ext}";
            try
            {
                using var ms = file.OpenReadStream();
                var putReq = new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = key,
                    InputStream = ms,
                    ContentType = file.ContentType
                };
                await _s3.PutObjectAsync(putReq);

                // 3) Сохраняем метаданные в БД
                var fileRecord = new File
                {
                    FileUrl = $"{_publicUrl}/{_bucket}/{key}",
                    FileType = file.ContentType,
                    Size = file.Length,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Files.Add(fileRecord);
                await _db.SaveChangesAsync();

                return Ok(new { fileId = fileRecord.FileId, url = fileRecord.FileUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки файла");
                // при необходимости: удалить объект из S3
                return StatusCode(500, "Внутренняя ошибка сервера");
            }
        }


        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(int fileId)
        {
            var fileRec = await _db.Files.FindAsync(fileId);
            if (fileRec == null) return NotFound();

            try
            {
                var getReq = new GetObjectRequest
                {
                    BucketName = _bucket,
                    Key = new Uri(fileRec.FileUrl).Segments.Last()
                };
                using var response = await _s3.GetObjectAsync(getReq);
                return File(response.ResponseStream, response.Headers.ContentType, fileRec.FileUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения файла");
                return StatusCode(500, "Не удалось получить файл");
            }
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

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
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
            public string Phone { get; set; }
            public string Code { get; set; }
            public string NewPassword { get; set; }
        }

        // В UsersController.cs
        public class MessageDto
        {
            [JsonProperty("messageId")]
            public int MessageId { get; set; }

            [JsonProperty("content")]
            public string Content { get; set; }

            [JsonProperty("userID")]
            public int UserID { get; set; }

            [JsonProperty("createdAt")]
            public DateTime CreatedAt { get; set; }

            // Новые поля для файлов
            [JsonProperty("fileId")]
            public int? FileId { get; set; }

            [JsonProperty("fileType")]
            public string? FileType { get; set; }

            [JsonProperty("fileUrl")]
            public string? FileUrl { get; set; }
            [JsonProperty("status")]
            public int Status { get; set; }
        }

        public class ChatDto
        {
            public int ChatId { get; set; }
            public List<UserDto> Members { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class UserDto
        {
            public int UserId { get; set; }
            public string Username { get; set; }
        }

        public class ChatCreationRequest
        {
            public string ChatName { get; set; }
            public List<int> UserIds { get; set; }
        }

        public enum MessageStatusType
        {
            Sent = 0,       // Отправлено
            Delivered = 1,  // Доставлено
            Read = 2        // Прочитано
        }
    }
}