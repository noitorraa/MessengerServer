using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using MessengerServer.Model;
using MessengerServer.Services;

namespace MessengerServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IVerificationService _verificationService;
        private readonly IChatService _chatService;
        private readonly IMessageService _messageService;
        private readonly IFileService _fileService;
        private readonly ICleanupService _cleanupService;
        private readonly Timer _cleanupTimer;

        public UsersController(
            IUserService userService,
            IVerificationService verificationService,
            IChatService chatService,
            IMessageService messageService,
            IFileService fileService,
            ICleanupService cleanupService)
        {
            _userService = userService;
            _verificationService = verificationService;
            _chatService = chatService;
            _messageService = messageService;
            _fileService = fileService;
            _cleanupService = cleanupService;
            _cleanupTimer = new Timer(CleanupExpiredData, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        private void CleanupExpiredData(object? state)
        {
            _cleanupService.CleanupExpiredData();
        }

        [HttpGet("authorization")]
        public async Task<ActionResult<User>> GetUserByLoginAndPassword(string login, string password)
        {
            return await _userService.GetUserByLoginAndPassword(login, password);
        }

        [HttpPost("send-verification-code")]
        public async Task<IActionResult> SendVerificationCode([FromBody] PhoneRequest request)
        {
            return await _verificationService.SendVerificationCode(request.Phone);
        }

        [HttpPost("verify-code")]
        public IActionResult VerifyCode(string phone, string code)
        {
            return _verificationService.VerifyCode(phone, code);
        }

        [HttpGet("chats/{userId}")]
        public async Task<ActionResult<List<ChatDto>>> GetUserChats(int userId)
        {
            return await _chatService.GetUserChats(userId);
        }

        [HttpGet("chats/{chatId}/{userId}/messages")]
        public async Task<IActionResult> GetMessages(int userId, int chatId)
        {
            return await _messageService.GetMessages(userId, chatId);
        }

        [HttpDelete("chats/{chatId}")]
        public async Task<IActionResult> DeleteChat(int chatId)
        {
            return await _chatService.DeleteChat(chatId);
        }

        [HttpPost("registration")]
        public async Task<IActionResult> Registration([FromBody] User user)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            }
            
            return await _userService.Registration(user);
        }

        [HttpGet("search")]
        public async Task<ActionResult<List<User>>> SearchUsersByLogin(string login)
        {
            return await _userService.SearchUsersByLogin(login);
        }

        [HttpPost("chats")]
        public async Task<ActionResult<Chat>> CreateChat([FromBody] ChatCreationRequest request)
        {
            return await _chatService.CreateChat(request);
        }

        [HttpPost("send-reset-code")]
        public Task<IActionResult> SendResetCode(string phone)
        {
            return _userService.SendResetCode(phone);
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetModel model)
        {
            return await _userService.ResetPassword(model);
        }

        [HttpGet("chats/existing")]
        public async Task<ActionResult<Chat>> GetExistingChat(int user1Id, int user2Id)
        {
            return await _userService.GetExistingChat(user1Id, user2Id);
        }

        [HttpPost("upload/{chatId}/{userId}")]
        [RequestSizeLimit(FileService.MAX_FILE_SIZE)]
        [RequestFormLimits(MultipartBodyLengthLimit = FileService.MAX_FILE_SIZE)]
        public async Task<IActionResult> UploadFile(
            [FromRoute] int chatId,
            [FromRoute] int userId,
            IFormFile file)
        {
            return await _fileService.UploadFile(chatId, userId, file);
        }

        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(int fileId)
        {
            return await _fileService.GetFile(fileId);
        }
    }
}
