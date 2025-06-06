using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;

namespace MessengerServer.Services
{
    public class FileService : IFileService
    {
        public const long MAX_FILE_SIZE = 10_000_000;
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
            if (file == null || file.Length == 0)
                return new BadRequestObjectResult("Файл не загружен");
            if (file.Length > MAX_FILE_SIZE)
                return new BadRequestObjectResult("Максимальный размер файла: 16 МБ");

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
                await _context.SaveChangesAsync();     // <-- здесь fileEntity.FileId != 0

                // 2) Теперь создаём сообщение, явно используя сгенерированный FileId
                var message = new Message
                {
                    ChatId = chatId,
                    SenderId = userId,
                    Content = "[Файл]",
                    FileId = fileEntity.FileId,       // теперь правильный ключ
                    CreatedAt = DateTime.UtcNow
                };
                _context.Messages.Add(message);
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
    }

    public interface IFileService
    {
        Task<IActionResult> UploadFile(int chatId, int userId, IFormFile file);
        Task<IActionResult> GetFile(int fileId);
    }
}
