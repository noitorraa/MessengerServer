using System;
using System.Collections.Generic;

namespace MessengerServer.Model;

public partial class MediaFile
{
    public int FileId { get; set; }

    public int? SenderId { get; set; }

    public int? ChatId { get; set; }

    public string FileUrl { get; set; } = null!;

    public string FileType { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual Chat? Chat { get; set; }

    public virtual User? Sender { get; set; }
}
