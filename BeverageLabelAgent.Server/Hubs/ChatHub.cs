using Microsoft.AspNetCore.SignalR;
using BeverageLabelAgent.Server.Models;
using BeverageLabelAgent.Server.Services;

namespace BeverageLabelAgent.Server.Hubs;

/// <summary>
/// SignalR hub for real-time chat communication between the browser and the agent.
/// </summary>
public class ChatHub : Hub
{
    private readonly LabelAgentService _agentService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(LabelAgentService agentService, ILogger<ChatHub> logger)
    {
        _agentService = agentService;
        _logger = logger;
    }

    /// <summary>
    /// Called when a new client connects. Sends a greeting message.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("Client connected: {ConnectionId}", connectionId);

        _agentService.InitializeConversation(connectionId);

        var greeting = _agentService.GetGreetingMessage();
        
        await Clients.Caller.SendAsync("ReceiveMessage", new AgentResponse
        {
            Message = greeting,
            CompletenessPercentage = 0,
            MissingFields = new List<string> 
            { 
                "Product Name", "Brand Name", "Beverage Type", 
                "Volume", "Ingredients", "Barcode (EAN) Number" 
            },
            MessageType = "text"
        });

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects. Cleans up conversation state.
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("Client disconnected: {ConnectionId}", connectionId);
        _agentService.RemoveConversation(connectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Receives a chat message from the client and processes it through the agent.
    /// </summary>
    public async Task SendMessage(string message)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("Received message from {ConnectionId}: {Message}", connectionId, message);

        if (string.IsNullOrWhiteSpace(message))
        {
            await Clients.Caller.SendAsync("ReceiveMessage", new AgentResponse
            {
                Message = "Please enter a message.",
                MessageType = "error"
            });
            return;
        }

        // Send typing indicator
        await Clients.Caller.SendAsync("AgentTyping", true);

        try
        {
            var response = await _agentService.ProcessMessageAsync(connectionId, message);

            // Stop typing indicator
            await Clients.Caller.SendAsync("AgentTyping", false);

            // Send the response
            await Clients.Caller.SendAsync("ReceiveMessage", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {ConnectionId}", connectionId);

            await Clients.Caller.SendAsync("AgentTyping", false);
            await Clients.Caller.SendAsync("ReceiveMessage", new AgentResponse
            {
                Message = "Sorry, an error occurred while processing your message. Please try again.",
                MessageType = "error"
            });
        }
    }

    /// <summary>
    /// Requests the current label state for a conversation.
    /// </summary>
    public async Task RequestLabelState()
    {
        var connectionId = Context.ConnectionId;
        var state = _agentService.GetConversation(connectionId);

        if (state != null)
        {
            await Clients.Caller.SendAsync("LabelStateUpdate", new
            {
                label = state.Label,
                completeness = state.Label.GetCompletenessPercentage(),
                missingFields = state.Label.GetMissingRequiredFields(),
                contradictions = state.Label.GetContradictions(),
                phase = state.Phase.ToString()
            });
        }
    }
}
