using System;
using System.Collections.Generic;

namespace MessengerServer.Models;

public partial class File
{
    public int FileId { get; set; }

    public string FileUrl { get; set; } = null!;

    public string FileType { get; set; } = null!;

    public long Size { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
