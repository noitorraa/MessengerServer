using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerServer.Controllers
{
    [ApiController] // тут получаем данные из базы данных 
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly MessengerDataBaseContext _context;

        public UsersController(MessengerDataBaseContext context)
        {
            _context = context;
        }

        [HttpGet("authorization")] // получение пользователя для авторизации
        public async Task<ActionResult<User>> GetUserByLoginAndPassword(string login, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == login && u.PasswordHash == password);
            if (user == null)
            {
                return NotFound(new { Message = "Пользователь не найден" });
            }
            return Ok(user);
        }

        [HttpGet("chats/{userId}")] // тут получение списка чатов пользователя для отображения их в клиентской части
        public async Task<ActionResult<List<Chat>>> GetUserChats(int userId)
        {
            var chats = await _context.Chats.Where(c => c.ChatMembers.First().UserId == userId).ToListAsync();
            Console.WriteLine(chats);
            if (chats == null || !chats.Any())
            {
                return NotFound(new { Message = "Чатов нет" });
            }
            return Ok(chats);
        }

        [HttpGet("chats/{chatId}/messages")] // получение сообщений в чате
        public async Task<ActionResult<List<Message>>> GetChatMessages(int chatId)
        {
            var chatMembers = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            var messages = await _context.Messages
                .Where(m => m.ChatId == chatId && chatMembers.Contains((int)m.SenderId))
                .ToListAsync();
            Console.WriteLine(messages);
            if (messages == null || !messages.Any())
            {
                return NotFound(new { Message = "Сообщения не найдены" });
            }

            return Ok(messages);
        }
    }
}