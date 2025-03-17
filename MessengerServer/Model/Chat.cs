using System;
using System.Collections.Generic;

namespace MessengerServer.Models;

public partial class Chat
{
    public int ChatId { get; set; }

    public string? ChatName { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
