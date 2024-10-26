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

        [HttpGet("authorization")]
        public async Task<ActionResult<User>> GetUserByLoginAndPassword(string login, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == login && u.PasswordHash == password);
            if (user == null)
            {
                return NotFound(new { Message = "Пользователь не найден" });
            }
            return Ok(user);
        }

        // Другие методы для работы с пользователями
    }
}