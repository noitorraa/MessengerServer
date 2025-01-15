using MessengerServer.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly MessengerDataBaseContext _context;
        public ChatHub(MessengerDataBaseContext context)
        {
            _context = context;
        }

        public async Task SendMessage(int userId, string message, int chatid)
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                var newMessage = new Message // ХУЙНЯ, чета напутал с senderId и ChatId
                {
                    Content = message,
                    CreatedAt = DateTime.Now,
                    SenderId = userId,
                    ChatId = chatid// правильный ChatId нада указывать
                };

                _context.Messages.Add(newMessage);
                await _context.SaveChangesAsync();

                // Отправляем сообщение всем клиентам
                await Clients.All.SendAsync("ReceiveMessage", userId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка в хабе при отправке сообщения " + ex.ToString());
                throw new HubException("Ошибка при отправке сообщения", ex);
            }
        }
    }
}