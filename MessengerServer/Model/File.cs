using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MessengerServer.Model;

public partial class File
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int FileId { get; set; }

    public string FileName { get; set; } = null!;

    public string FileType { get; set; } = null!;

    public byte[] FileData { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
