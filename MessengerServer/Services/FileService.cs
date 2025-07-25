using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;

namespace MessengerServer.Services
{
    public class FileService : IFileService
    {
        public const long MAX_FILE_SIZE = 16 * 1024 * 1024;
        private readonly DefaultDbContext _context;
        public FileService(DefaultDbContext context)
        {
            _context = context;
        }

        public string GetFileUrl(int fileId)
        {
            return $"/api/users/{fileId}";
        }

        public async Task<IActionResult> UploadFile(int chatId, int userId, IFormFile file)
        {
            if (!AllowedTypes.Contains(file.ContentType))
                return new BadRequestObjectResult("Недопустимый тип файла");
            if (file == null || file.Length == 0)
                return new BadRequestObjectResult("Файл не загружен");
            if (file.Length > MAX_FILE_SIZE)
                return new BadRequestObjectResult("Максимальный размер файла: 16 МБ");

            var chatExists = await _context.Chats.AnyAsync(c => c.ChatId == chatId);
            if (!chatExists) return new BadRequestResult();

            var ext = Path.GetExtension(file.FileName);
            if (!ExtensionToMime.TryGetValue(ext, out var expectedMime) 
                || !file.ContentType.Equals(expectedMime, StringComparison.OrdinalIgnoreCase))
            {
                return new BadRequestObjectResult("Несоответствие расширения и MIME‑типа файла");
            }
            if (!AllowedTypes.Contains(file.ContentType))
            {
                return new BadRequestObjectResult("Недопустимый тип файла");
            }

            // Решение: добавить валидацию перед сохранением
            var userExists = await _context.Users.AnyAsync(u => u.UserId == userId);
            if (!userExists) return new BadRequestResult();

            byte[] fileBytes;
            await using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1) Сохраняем только файл, чтобы БД сгенерировала FileId
                var fileEntity = new Model.File
                {
                    FileName = file.FileName,
                    FileType = file.ContentType,
                    FileData = fileBytes,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Files.Add(fileEntity);
                await _context.SaveChangesAsync();

                await tx.CommitAsync();
                return new OkObjectResult(new
                {
                    FileId = fileEntity.FileId,
                    FileName = fileEntity.FileName,
                    FileType = fileEntity.FileType
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> GetFile(int fileId)
        {
            var file = await _context.Files.FindAsync(fileId);
            if (file == null)
                return new NotFoundResult();

            return new FileContentResult(file.FileData, file.FileType)
            {
                FileDownloadName = file.FileName
            };
        }
        private static readonly HashSet<string> AllowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Изображения (загрузка из галереи или камеры)
            "image/jpeg",   // .jpg, .jpeg
            "image/png",    // .png
            "image/gif",    // .gif
            "image/bmp",    // .bmp
            "image/heic",   // .heic (iOS-камера)

            // Документы
            "application/pdf",  // .pdf

            // Аудио
            "audio/mpeg",   // .mp3
            "audio/wav",    // .wav
            "audio/ogg"     // .ogg
        };

        // И для проверки расширения:
        private static readonly Dictionary<string, string> ExtensionToMime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".jpg",  "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".png",  "image/png"  },
            { ".gif",  "image/gif"  },
            { ".bmp",  "image/bmp"  },
            { ".heic", "image/heic" },

            { ".pdf",  "application/pdf" },

            { ".mp3",  "audio/mpeg" },
            { ".wav",  "audio/wav"  },
            { ".ogg",  "audio/ogg"  },
        };

    }

    public interface IFileService
    {
        Task<IActionResult> UploadFile(int chatId, int userId, IFormFile file);
        Task<IActionResult> GetFile(int fileId);
    }
}
