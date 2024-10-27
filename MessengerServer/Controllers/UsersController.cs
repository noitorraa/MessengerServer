using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerServer.Controllers
{
    [ApiController]
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
            if (chats == null || !chats.Any())
            {
                return NotFound(new { Message = "Чатов нет" });
            }
            return Ok(chats);
        }
    }
}