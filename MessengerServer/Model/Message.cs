using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MessengerServer.Model;

public partial class Message
{
    [JsonPropertyName("messageId")]
    public int MessageId { get; set; }
    [JsonPropertyName("chatId")]
    public int? ChatId { get; set; }
    [JsonPropertyName("senderId")]
    public int? SenderId { get; set; }
    [JsonPropertyName("content")]
    public string Content { get; set; } = null!;
    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    public virtual Chat? Chat { get; set; }

    public virtual ICollection<MessageStatus> MessageStatuses { get; set; } = new List<MessageStatus>();

    public virtual User? Sender { get; set; }
}
