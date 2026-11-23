namespace Sistemas_para_pericias.Models.Responses;

public enum FeedbackMessageType
{
    Success,
    Error,
    Warning,
    Information
}

public sealed record FeedbackMessageViewModel(string Message, FeedbackMessageType Type, string? Title = null);
