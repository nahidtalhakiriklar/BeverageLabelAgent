namespace BeverageLabelAgent.Server.Models;

/// <summary>
/// Tracks the state of a multi-turn conversation with the agent.
/// </summary>
public class ConversationState
{
    public string ConnectionId { get; set; } = string.Empty;
    public BeverageLabel Label { get; set; } = new();
    public List<ChatMessage> History { get; set; } = new();
    public ConversationPhase Phase { get; set; } = ConversationPhase.Greeting;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public bool LabelGenerated { get; set; } = false;
}

public enum ConversationPhase
{
    Greeting,           // Initial state
    CollectingInfo,     // Gathering label information
    ClarifyingInfo,     // Asking follow-up questions / resolving contradictions
    ReviewingLabel,     // User is reviewing the generated label
    Complete            // Label finalized
}

/// <summary>
/// DTO for chat messages between client and server.
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = "user";  // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? LabelHtml { get; set; }       // If this message includes a label preview
    public string? MessageType { get; set; } = "text"; // "text", "label", "status"
}

/// <summary>
/// Response sent back to the client via SignalR.
/// </summary>
public class AgentResponse
{
    public string Message { get; set; } = string.Empty;
    public string? LabelHtml { get; set; }
    public int CompletenessPercentage { get; set; }
    public List<string> MissingFields { get; set; } = new();
    public List<string> Contradictions { get; set; } = new();
    public string MessageType { get; set; } = "text"; // "text", "label", "error"
    public string? BarcodeUrl { get; set; }
}
