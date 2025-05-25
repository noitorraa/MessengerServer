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

        public async Task<IActionResult> UploadFile(int chatId, int userId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return new BadRequestObjectResult("Файл не загружен");
            if (file.Length > 16 * 1024 * 1024)
                return new BadRequestObjectResult("Максимальный размер файла: 16 МБ");

            await using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);

            // Начнём транзакцию
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var fileEntity = new Model.File
                {
                    FileName = file.FileName,
                    FileType = file.ContentType,
                    FileData = memoryStream.ToArray(),
                    CreatedAt = DateTime.UtcNow
                };

                _context.Files.Add(fileEntity);

                var message = new Message
                {
                    ChatId = chatId,
                    SenderId = userId,
                    Content = "Файл",
                    FileId = fileEntity.FileId, // Use the saved FileId
                    CreatedAt = DateTime.UtcNow
                };

                _context.Messages.Add(message);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new OkObjectResult(new { FileId = fileEntity.FileId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = 500
                };
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
