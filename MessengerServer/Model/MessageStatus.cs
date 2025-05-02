using System;
using System.Collections.Generic;

namespace MessengerServer.Model;

public partial class MessageStatus
{
    public int StatusId { get; set; }

    public int MessageId { get; set; }

    public int? UserId { get; set; }

    public int Status { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Message Message { get; set; } = null!;

    public virtual User? User { get; set; }
}
