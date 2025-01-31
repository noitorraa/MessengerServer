using MessengerServer.Controllers;
using MessengerServer.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        public async Task SendToGroup(string groupName, string message)
        {
            await Clients.Group(groupName).SendAsync("ReceiveTest", message);
        }

        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }
    }
}