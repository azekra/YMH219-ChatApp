using System.Text.Json;
using System.Text.Json.Serialization;

namespace YMH219_ChatApp.Networking;

internal static class ChatProtocol
{
    public const string Join = "join";
    public const string Joined = "joined";
    public const string Message = "message";
    public const string Chat = "chat";
    public const string System = "system";
    public const string Error = "error";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(ChatWireMessage message) => JsonSerializer.Serialize(message, SerializerOptions);

    public static ChatWireMessage? Deserialize(string json) => JsonSerializer.Deserialize<ChatWireMessage>(json, SerializerOptions);
}

internal sealed record ChatWireMessage(
    string Type,
    string? UserName = null,
    string? Text = null,
    DateTimeOffset? Timestamp = null);
