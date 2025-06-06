using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace MessengerServer.Services
{
    public class ChatDto
    {
        [Key]
        public int ChatId { get; set; }

        public List<UserDto>? Members { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PhoneRequest
    {
        public string Phone { get; set; }
    }

    public class VerifyCodeRequest
    {
        public string Phone { get; set; }
        public string Code  { get; set; }
    }

    public class UserDto
    {
        [Key]
        public int UserId { get; set; }
        public string? Username { get; set; }
    }

    public class MessageDto
    {
        [JsonProperty("messageId")]
        public int MessageId { get; set; }

        [JsonProperty("content")]
        public string? Content { get; set; }

        [JsonProperty("userID")]
        public int UserID { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("fileId")]
        public int? FileId { get; set; }

        [JsonProperty("fileName")]
        public string? FileName { get; set; }

        [JsonProperty("fileType")]
        public string? FileType { get; set; }
        [JsonProperty("fileUrl")]
        public string? FileUrl { get; set; }

        [JsonProperty("status")]
        public int Status { get; set; }
    }

    public class StatusDto
    {
        public int MessageId { get; set; }
        public int Status { get; set; }
    }

    public class ChatCreationRequest
    {
        public string? ChatName { get; set; }
        public List<int>? UserIds { get; set; }
    }
    
    public class ResetModel
    {
        public required string Phone { get; set; }
        public string? Code { get; set; }
        public string? NewPassword { get; set; }
    }
}