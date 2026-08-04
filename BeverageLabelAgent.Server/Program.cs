using BeverageLabelAgent.Server.Hubs;
using BeverageLabelAgent.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add SignalR
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.PayloadSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5000", "http://localhost:5001", "https://localhost:5001")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Register HttpClient
builder.Services.AddHttpClient<TecItBarcodeService>();
builder.Services.AddHttpClient<GeminiLLMService>();

// Register application services
builder.Services.AddSingleton<LabelAgentService>();
builder.Services.AddSingleton<TecItBarcodeService>();
builder.Services.AddSingleton<GeminiLLMService>();
builder.Services.AddSingleton<LabelRendererService>();

var app = builder.Build();

// Enable CORS
app.UseCors();

// Serve static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Map SignalR hub
app.MapHub<ChatHub>("/chathub");

// Health check endpoint
app.MapGet("/api/health", () => new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0"
});

// Configuration status endpoint (for frontend to check)
app.MapGet("/api/config/status", (GeminiLLMService llm) => new
{
    llmConfigured = llm.IsConfigured,
    llmProvider = llm.IsConfigured ? "Google Gemini" : "Fallback (Rule-based)",
    barcodeApi = "TEC-IT"
});

Console.WriteLine("===========================================");
Console.WriteLine("  Beverage Label Agent");
Console.WriteLine("  Chat-Agent für druckfertige Getränkeetiketten");
Console.WriteLine("===========================================");
Console.WriteLine($"  Server: http://localhost:5000");
Console.WriteLine($"  LLM: {(app.Services.GetRequiredService<GeminiLLMService>().IsConfigured ? "Google Gemini ✓" : "Fallback Agent (configure Gemini API key for full features)")}");
Console.WriteLine("===========================================");

app.Run("http://localhost:5000");
