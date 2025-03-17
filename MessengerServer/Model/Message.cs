using System;
using System.Collections.Generic;

namespace MessengerServer.Models;

public partial class Message
{
    public int MessageId { get; set; }

    public int? ChatId { get; set; }

    public int? SenderId { get; set; }

    public string Content { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public int? FileId { get; set; }

    public virtual Chat? Chat { get; set; }

    public virtual File? File { get; set; }

    public virtual ICollection<MessageStatus> MessageStatuses { get; set; } = new List<MessageStatus>();

    public virtual User? Sender { get; set; }
}
