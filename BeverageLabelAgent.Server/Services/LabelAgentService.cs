using BeverageLabelAgent.Server.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BeverageLabelAgent.Server.Services;

/// <summary>
/// Main agent orchestrator. Manages conversation state, uses LLM to understand user intent,
/// extracts label data, detects issues, and triggers label generation.
/// </summary>
public class LabelAgentService
{
    private readonly GeminiLLMService _llmService;
    private readonly TecItBarcodeService _barcodeService;
    private readonly LabelRendererService _labelRenderer;
    private readonly ILogger<LabelAgentService> _logger;
    
    // In-memory conversation states (per SignalR connection)
    private static readonly ConcurrentDictionary<string, ConversationState> _conversations = new();

    private const string SYSTEM_PROMPT = @"You are a specialized AI assistant for creating beverage product labels. You help employees of a beverage manufacturer create print-ready labels by collecting product information through conversation.

YOUR ROLE:
- Collect all necessary information for a beverage label through natural conversation
- Detect missing, incomplete, or contradictory information
- Ask targeted follow-up questions when information is unclear or missing
- Be knowledgeable about EU food labeling regulations
- Guide users step by step through the label creation process

REQUIRED LABEL FIELDS (must have before generating):
1. productName - The name of the beverage product
2. brandName - The brand/company name  
3. beverageType - Type: Beer, Wine, Juice, Soda, Water, Spirit, Energy Drink, Tea, Coffee, etc.
4. volume - Volume with unit (e.g., ""330 ml"", ""0.5 L"", ""750 ml"")
5. ingredients - List of ingredients
6. barcodeData - EAN-13 barcode number (exactly 13 digits)

OPTIONAL BUT RECOMMENDED FIELDS:
- alcoholContent - Alcohol percentage (REQUIRED for alcoholic beverages)
- isAlcoholic - Whether the beverage contains alcohol
- allergens - Allergen information (e.g., ""Gluten, Sulphites"")
- nutritionalInfo - Nutritional values per 100ml
- energyKj / energyKcal - Energy values
- batchNumber - Lot/Batch number (e.g., ""L2024-0815"")
- bestBeforeDate - Best before date (e.g., ""12/2025"")
- manufacturerName - Name of the manufacturer
- manufacturerAddress - Address of the manufacturer
- countryOfOrigin - Country of production
- description - Marketing tagline or product description
- storageInstructions - Storage guidance
- certificationMarks - Certifications (Bio, Fairtrade, etc.)
- barcodeType - Barcode type (default: EAN13, also supports: QRCode, Code128, EAN8)

CONTRADICTION DETECTION RULES:
- If a product is described as ""alcohol-free"" or ""non-alcoholic"" but has alcoholContent > 0.5%, flag this
- If a product is described as ""beer"" or ""wine"" but isAlcoholic is false, ask for clarification
- If volume seems unreasonable (< 10ml or > 10000ml), ask for confirmation
- If EAN-13 is provided but doesn't have exactly 13 digits, flag this
- If ingredients mention allergens not listed in the allergens field, suggest adding them

RESPONSE FORMAT:
Always respond in this exact JSON format:
{
  ""message"": ""Your natural language response to the user (in the same language they use)"",
  ""extractedData"": {
    ""productName"": null,
    ""brandName"": null,
    ""beverageType"": null,
    ""volume"": null,
    ""alcoholContent"": null,
    ""isAlcoholic"": null,
    ""ingredients"": null,
    ""allergens"": null,
    ""nutritionalInfo"": null,
    ""energyKj"": null,
    ""energyKcal"": null,
    ""batchNumber"": null,
    ""bestBeforeDate"": null,
    ""manufacturerName"": null,
    ""manufacturerAddress"": null,
    ""countryOfOrigin"": null,
    ""description"": null,
    ""storageInstructions"": null,
    ""certificationMarks"": null,
    ""barcodeData"": null,
    ""barcodeType"": null
  },
  ""contradictions"": [],
  ""readyToGenerate"": false
}

RULES:
- Only include fields in extractedData that were EXPLICITLY mentioned or can be CLEARLY inferred from the current message
- Set readyToGenerate to true ONLY when all 6 required fields are filled
- Keep null for fields not yet provided
- Respond in the SAME LANGUAGE the user writes in (German, English, etc.)
- If the user provides all info at once, extract everything and set readyToGenerate accordingly
- Be friendly but professional
- When asking follow-up questions, explain WHY the information is needed";

    public LabelAgentService(
        GeminiLLMService llmService,
        TecItBarcodeService barcodeService,
        LabelRendererService labelRenderer,
        ILogger<LabelAgentService> logger)
    {
        _llmService = llmService;
        _barcodeService = barcodeService;
        _labelRenderer = labelRenderer;
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new conversation for a connection.
    /// </summary>
    public ConversationState InitializeConversation(string connectionId)
    {
        var state = new ConversationState { ConnectionId = connectionId };
        _conversations[connectionId] = state;
        _logger.LogInformation("Initialized conversation for connection {ConnectionId}", connectionId);
        return state;
    }

    /// <summary>
    /// Processes a user message and returns the agent's response.
    /// </summary>
    public async Task<AgentResponse> ProcessMessageAsync(string connectionId, string userMessage)
    {
        if (!_conversations.TryGetValue(connectionId, out var state))
        {
            state = InitializeConversation(connectionId);
        }

        state.LastActivityAt = DateTime.UtcNow;

        // Add user message to history
        state.History.Add(new ChatMessage
        {
            Role = "user",
            Content = userMessage,
            Timestamp = DateTime.UtcNow
        });

        try
        {
            AgentResponse response;

            if (_llmService.IsConfigured)
            {
                response = await ProcessWithLLMAsync(state, userMessage);
            }
            else
            {
                response = await ProcessWithFallbackAgentAsync(state, userMessage);
            }

            // Add assistant message to history
            state.History.Add(new ChatMessage
            {
                Role = "assistant",
                Content = response.Message,
                Timestamp = DateTime.UtcNow,
                LabelHtml = response.LabelHtml,
                MessageType = response.MessageType
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for connection {ConnectionId}", connectionId);
            
            // Try fallback on LLM error
            try
            {
                var fallbackResponse = await ProcessWithFallbackAgentAsync(state, userMessage);
                state.History.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = fallbackResponse.Message,
                    Timestamp = DateTime.UtcNow,
                    MessageType = fallbackResponse.MessageType
                });
                return fallbackResponse;
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Fallback agent also failed");
                return new AgentResponse
                {
                    Message = "I'm sorry, I encountered an error processing your request. Please try again.",
                    MessageType = "error"
                };
            }
        }
    }

    /// <summary>
    /// Processes the message using Gemini LLM.
    /// </summary>
    private async Task<AgentResponse> ProcessWithLLMAsync(ConversationState state, string userMessage)
    {
        // Build history for LLM
        var llmHistory = state.History
            .SkipLast(1) // Skip the just-added user message
            .Select(m => new GeminiMessage { Role = m.Role, Content = m.Content })
            .ToList();

        // Add context about current label state
        var contextMessage = $"{userMessage}\n\n[SYSTEM CONTEXT - Current label state: {JsonSerializer.Serialize(state.Label, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, WriteIndented = false })}]";

        var llmResponse = await _llmService.ChatAsync(SYSTEM_PROMPT, llmHistory, contextMessage);
        _logger.LogDebug("LLM Response: {Response}", llmResponse);

        // Parse the JSON response
        var agentOutput = ParseLLMResponse(llmResponse);

        // Update label with extracted data
        if (agentOutput.ExtractedData != null)
        {
            MergeLabelData(state.Label, agentOutput.ExtractedData);
        }

        // Check contradictions
        var modelContradictions = agentOutput.Contradictions ?? new List<string>();
        var dataContradictions = state.Label.GetContradictions();
        var allContradictions = modelContradictions.Concat(dataContradictions).Distinct().ToList();

        var response = new AgentResponse
        {
            Message = agentOutput.Message ?? "I'm processing your request...",
            CompletenessPercentage = state.Label.GetCompletenessPercentage(),
            MissingFields = state.Label.GetMissingRequiredFields(),
            Contradictions = allContradictions,
            MessageType = "text"
        };

        // Generate label if ready
        if (agentOutput.ReadyToGenerate && state.Label.GetMissingRequiredFields().Count == 0 && allContradictions.Count == 0)
        {
            await GenerateLabelForResponse(state, response);
        }

        return response;
    }

    /// <summary>
    /// Fallback rule-based agent when LLM is not available.
    /// </summary>
    private async Task<AgentResponse> ProcessWithFallbackAgentAsync(ConversationState state, string userMessage)
    {
        var msgLower = userMessage.ToLowerInvariant();

        // Try to extract data from the message using simple pattern matching
        ExtractDataFromMessage(state.Label, userMessage);

        var missing = state.Label.GetMissingRequiredFields();
        var contradictions = state.Label.GetContradictions();
        var completeness = state.Label.GetCompletenessPercentage();

        string responseMessage;

        // Check for explicit generation requests
        if (msgLower.Contains("generate") || msgLower.Contains("create label") || 
            msgLower.Contains("etikett erstellen") || msgLower.Contains("erstell") ||
            msgLower.Contains("make label") || msgLower.Contains("label erzeugen"))
        {
            if (missing.Count > 0)
            {
                responseMessage = $"I'd love to generate your label, but I still need the following information:\n\n" +
                    string.Join("\n", missing.Select(f => $"• **{f}**")) +
                    "\n\nPlease provide these details so I can create your label.";
            }
            else if (contradictions.Count > 0)
            {
                responseMessage = "Before I can generate the label, please resolve these issues:\n\n" +
                    string.Join("\n", contradictions.Select(c => $"⚠️ {c}"));
            }
            else
            {
                var response = new AgentResponse
                {
                    Message = "Great! Generating your label now...",
                    CompletenessPercentage = completeness,
                    MissingFields = missing,
                    Contradictions = contradictions,
                    MessageType = "text"
                };
                await GenerateLabelForResponse(state, response);
                return response;
            }
        }
        else if (state.Phase == ConversationPhase.Greeting)
        {
            state.Phase = ConversationPhase.CollectingInfo;
            responseMessage = "👋 Welcome! I'm your beverage label assistant. I'll help you create a print-ready label for your product.\n\n" +
                "You can tell me about your beverage in natural language, and I'll extract the relevant information. " +
                "For example, you could say:\n\n" +
                "*\"We're making a new craft beer called Alpine Gold for our brand Mountain Brew. It's 330ml with 5.2% alcohol.\"*\n\n" +
                "Or you can provide details step by step. What would you like to start with?";
        }
        else
        {
            // Build response based on what we have and what's missing
            if (missing.Count == 0 && contradictions.Count == 0)
            {
                var response = new AgentResponse
                {
                    Message = $"✅ **Alle erforderlichen Informationen wurden erfasst!** (Vollständigkeit: **{completeness}%**)\n\nEtikett wird generiert...",
                    CompletenessPercentage = completeness,
                    MissingFields = missing,
                    Contradictions = contradictions,
                    MessageType = "text"
                };
                await GenerateLabelForResponse(state, response);
                return response;
            }
            else
            {
                var parts = new List<string>();
                parts.Add($"Vielen Dank! Etiketten-Vollständigkeit: **{completeness}%**.");

                // Show what has been captured so far
                var captured = new List<string>();
                if (!string.IsNullOrWhiteSpace(state.Label.ProductName)) captured.Add($"• **Produktname:** {state.Label.ProductName}");
                if (!string.IsNullOrWhiteSpace(state.Label.BrandName)) captured.Add($"• **Marke:** {state.Label.BrandName}");
                if (!string.IsNullOrWhiteSpace(state.Label.BeverageType)) captured.Add($"• **Getränkeart:** {state.Label.BeverageType}");
                if (!string.IsNullOrWhiteSpace(state.Label.Volume)) captured.Add($"• **Füllmenge:** {state.Label.Volume}");
                if (state.Label.AlcoholContent.HasValue) captured.Add($"• **Alkoholgehalt:** {state.Label.AlcoholContent.Value}% vol");
                if (!string.IsNullOrWhiteSpace(state.Label.Ingredients)) captured.Add($"• **Zutaten:** {state.Label.Ingredients}");
                if (!string.IsNullOrWhiteSpace(state.Label.BarcodeData)) captured.Add($"• **Barcode (EAN):** {state.Label.BarcodeData}");

                if (captured.Count > 0)
                {
                    parts.Add("\n✨ **Bisher erfasste Daten:**");
                    parts.AddRange(captured);
                }

                if (contradictions.Count > 0)
                {
                    parts.Add("\n⚠️ **Gefundene Probleme / Widersprüche:**");
                    parts.AddRange(contradictions.Select(c => $"• {c}"));
                }

                if (missing.Count > 0)
                {
                    parts.Add("\n📋 **Noch benötigte Angaben:**");
                    parts.AddRange(missing.Select(f => $"• **{f}**"));
                }

                responseMessage = string.Join("\n", parts);
            }
        }

        return new AgentResponse
        {
            Message = responseMessage,
            CompletenessPercentage = completeness,
            MissingFields = missing,
            Contradictions = contradictions,
            MessageType = "text"
        };
    }

    /// <summary>
    /// Generates the label HTML and attaches it to the response.
    /// </summary>
    private async Task GenerateLabelForResponse(ConversationState state, AgentResponse response)
    {
        try
        {
            var labelHtml = await _labelRenderer.GenerateLabelHtmlAsync(state.Label);
            response.LabelHtml = labelHtml;
            response.MessageType = "label";
            
            if (!string.IsNullOrWhiteSpace(state.Label.BarcodeData))
            {
                response.BarcodeUrl = _barcodeService.GetBarcodeUrl(
                    state.Label.BarcodeData,
                    state.Label.BarcodeType ?? "EAN13"
                );
            }

            state.LabelGenerated = true;
            state.Phase = ConversationPhase.ReviewingLabel;
            
            response.Message += "\n\n🏷️ Your label has been generated! You can see the preview on the right panel. Use the **Print** or **Download** button to save it.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate label");
            response.Message += "\n\n❌ Sorry, I had trouble generating the label preview. Please try again.";
        }
    }

    /// <summary>
    /// Parses the structured JSON response from the LLM.
    /// </summary>
    private LLMAgentOutput ParseLLMResponse(string response)
    {
        try
        {
            // Try to extract JSON from the response (it might be wrapped in markdown code blocks)
            var jsonStr = response.Trim();
            if (jsonStr.StartsWith("```"))
            {
                var start = jsonStr.IndexOf('{');
                var end = jsonStr.LastIndexOf('}');
                if (start >= 0 && end > start)
                    jsonStr = jsonStr.Substring(start, end - start + 1);
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Deserialize<LLMAgentOutput>(jsonStr, options) ?? new LLMAgentOutput();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response as JSON, treating as plain text: {Response}", response);
            return new LLMAgentOutput { Message = response };
        }
    }

    /// <summary>
    /// Merges extracted data from LLM response into the label.
    /// Only updates fields that are non-null in the extracted data.
    /// </summary>
    private void MergeLabelData(BeverageLabel label, ExtractedLabelData data)
    {
        if (!string.IsNullOrWhiteSpace(data.ProductName)) label.ProductName = data.ProductName;
        if (!string.IsNullOrWhiteSpace(data.BrandName)) label.BrandName = data.BrandName;
        if (!string.IsNullOrWhiteSpace(data.BeverageType)) label.BeverageType = data.BeverageType;
        if (!string.IsNullOrWhiteSpace(data.Volume)) label.Volume = data.Volume;
        if (data.AlcoholContent.HasValue) label.AlcoholContent = data.AlcoholContent;
        if (data.IsAlcoholic.HasValue) label.IsAlcoholic = data.IsAlcoholic.Value;
        if (!string.IsNullOrWhiteSpace(data.Ingredients)) label.Ingredients = data.Ingredients;
        if (!string.IsNullOrWhiteSpace(data.Allergens)) label.Allergens = data.Allergens;
        if (!string.IsNullOrWhiteSpace(data.NutritionalInfo)) label.NutritionalInfo = data.NutritionalInfo;
        if (!string.IsNullOrWhiteSpace(data.EnergyKj)) label.EnergyKj = data.EnergyKj;
        if (!string.IsNullOrWhiteSpace(data.EnergyKcal)) label.EnergyKcal = data.EnergyKcal;
        if (!string.IsNullOrWhiteSpace(data.BatchNumber)) label.BatchNumber = data.BatchNumber;
        if (!string.IsNullOrWhiteSpace(data.BestBeforeDate)) label.BestBeforeDate = data.BestBeforeDate;
        if (!string.IsNullOrWhiteSpace(data.ManufacturerName)) label.ManufacturerName = data.ManufacturerName;
        if (!string.IsNullOrWhiteSpace(data.ManufacturerAddress)) label.ManufacturerAddress = data.ManufacturerAddress;
        if (!string.IsNullOrWhiteSpace(data.CountryOfOrigin)) label.CountryOfOrigin = data.CountryOfOrigin;
        if (!string.IsNullOrWhiteSpace(data.Description)) label.Description = data.Description;
        if (!string.IsNullOrWhiteSpace(data.StorageInstructions)) label.StorageInstructions = data.StorageInstructions;
        if (!string.IsNullOrWhiteSpace(data.CertificationMarks)) label.CertificationMarks = data.CertificationMarks;
        if (!string.IsNullOrWhiteSpace(data.BarcodeData)) label.BarcodeData = data.BarcodeData;
        if (!string.IsNullOrWhiteSpace(data.BarcodeType)) label.BarcodeType = data.BarcodeType;
    }

    /// <summary>
    /// Advanced multi-pass NLU entity extractor supporting German, English, and Turkish.
    /// Intelligently parses natural language, key-value lines, brand keywords (winery, brewery, etc.),
    /// drink varieties (Chardonnay, IPA, Pils, etc.), and multi-word inputs.
    /// </summary>
    public void ExtractDataFromMessage(BeverageLabel label, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var msg = message.Trim();
        var opts = System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant;

        // ============================================================
        // PASS 1: Explicit Key-Value Pairs (English, German, Turkish)
        // ============================================================

        // Product Name / Produktname / Name / Produkt / Product
        var prodMatch = System.Text.RegularExpressions.Regex.Match(msg,
            @"(?:product\s*name|produktname|bezeichnung|ürün\s*adı|ürün)\s*[:=]\s*(?:""([^""]+)""|'([^']+)'|([^\n,;]+))", opts);
        if (prodMatch.Success)
        {
            var val = prodMatch.Groups[1].Success ? prodMatch.Groups[1].Value :
                      prodMatch.Groups[2].Success ? prodMatch.Groups[2].Value : prodMatch.Groups[3].Value;
            if (!string.IsNullOrWhiteSpace(val)) label.ProductName = val.Trim();
        }

        // Brand Name / Marke / Brand / Hersteller / Brauerei / Winery / Marka
        var brandMatch = System.Text.RegularExpressions.Regex.Match(msg,
            @"(?:brand\s*name|marke|brand|hersteller|brauerei|winzer|winery|brewery|marka|markası)\s*[:=]\s*(?:""([^""]+)""|'([^']+)'|([^\n,;]+))", opts);
        if (brandMatch.Success)
        {
            var val = brandMatch.Groups[1].Success ? brandMatch.Groups[1].Value :
                      brandMatch.Groups[2].Success ? brandMatch.Groups[2].Value : brandMatch.Groups[3].Value;
            if (!string.IsNullOrWhiteSpace(val)) label.BrandName = val.Trim();
        }

        // Ingredients / Zutaten / Inhaltsstoffe / İçindekiler
        var ingrMatch = System.Text.RegularExpressions.Regex.Match(msg,
            @"(?:ingredients|zutaten|inhaltsstoffe|icindekiler|içindekiler)\s*[:=]\s*(.+)", opts);
        if (ingrMatch.Success)
        {
            var raw = ingrMatch.Groups[1].Value.Trim().Trim('"', '\'');
            var keyMatch = System.Text.RegularExpressions.Regex.Match(raw, @"(?<=\s)[""']?\s*(?:volume|ean|brand|product|marke|produkt|alkohol)\s*[:=]", opts);
            if (keyMatch.Success)
            {
                raw = raw.Substring(0, keyMatch.Index).Trim();
            }
            if (!string.IsNullOrWhiteSpace(raw)) label.Ingredients = raw.Trim('"', '\'', ',', ';', ' ');
        }

        // ============================================================
        // PASS 2: Brand / Company Pattern Recognition
        // ============================================================
        if (string.IsNullOrWhiteSpace(label.BrandName))
        {
            // Keyword-based brand matching: "Davraz Winery", "Mountain Brewery", "Schloss Kellerei", "Brauhaus Alpenblick"
            var brandKeywordMatch = System.Text.RegularExpressions.Regex.Match(msg,
                @"\b([A-Z0-9äöüßÄÖÜ][\w\säöüßÄÖÜ-]{1,25}\s+(?:winery|brewery|brauerei|brauhaus|kellerei|winzer|distillery|kelterei|gmbh|ag|co|ltd))\b", opts);
            if (brandKeywordMatch.Success)
            {
                var val = brandKeywordMatch.Groups[1].Value.Trim();
                // If val starts with a variety (e.g. "Chardonnay Davraz Winery"), strip "Chardonnay"
                var varietyPrefixMatch = System.Text.RegularExpressions.Regex.Match(val, @"^\b(chardonnay|sauvignon|merlot|pinot|riesling|cabernet|beer|bier|wein|wine)\s+", opts);
                if (varietyPrefixMatch.Success)
                {
                    val = val.Substring(varietyPrefixMatch.Length).Trim();
                }
                label.BrandName = val;
            }
            else
            {
                var brandNL1 = System.Text.RegularExpressions.Regex.Match(msg,
                    @"([A-Z0-9äöüßÄÖÜ][\w\säöüßÄÖÜ-]{2,30}?)\s+(?:markası|marke|brauerei|winery)\s+(?:için|für|von)", opts);
                if (brandNL1.Success)
                {
                    var val = brandNL1.Groups[1].Value.Trim();
                    var words = val.Split(' ');
                    if (words.Length > 3) val = string.Join(" ", words.TakeLast(3));
                    label.BrandName = val;
                }
                else
                {
                    var brandNL2 = System.Text.RegularExpressions.Regex.Match(msg,
                        @"(?:für die marke|marke|brauerei|winery|von)\s+([A-Z0-9äöüßÄÖÜ][\w\säöüßÄÖÜ-]{2,30}?)(?:\s+|$|\.)", opts);
                    if (brandNL2.Success) label.BrandName = brandNL2.Groups[1].Value.Trim();
                }
            }
        }

        // ============================================================
        // PASS 3: Product Name & Drink Variety Recognition
        // ============================================================
        if (string.IsNullOrWhiteSpace(label.ProductName))
        {
            var prodNL1 = System.Text.RegularExpressions.Regex.Match(msg,
                @"([A-Z0-9äöüßÄÖÜ][\wäöüßÄÖÜ-]{2,30})\s+(?:adında|namens|called|named)", opts);
            if (prodNL1.Success)
            {
                label.ProductName = prodNL1.Groups[1].Value.Trim();
            }
            else
            {
                var prodNL2 = System.Text.RegularExpressions.Regex.Match(msg,
                    @"(?:adında|namens|called|named|produkt)\s+([A-Z0-9äöüßÄÖÜ][\w\säöüßÄÖÜ-]{2,30}?)(?:\s+|$|\.)", opts);
                if (prodNL2.Success) label.ProductName = prodNL2.Groups[1].Value.Trim();
            }
        }

        // ============================================================
        // PASS 4: Beverage Type & Wine/Beer Varieties
        // ============================================================
        var msgLower = msg.ToLowerInvariant();
        
        // Specific Wine Varieties
        if (msgLower.Contains("chardonnay") || msgLower.Contains("sauvignon") || msgLower.Contains("merlot") || 
            msgLower.Contains("pinot") || msgLower.Contains("riesling") || msgLower.Contains("cabernet") || 
            msgLower.Contains("shiraz") || msgLower.Contains("syrah") || msgLower.Contains("chianti"))
        {
            if (string.IsNullOrWhiteSpace(label.BeverageType)) label.BeverageType = "Wein (Wine)";
            label.IsAlcoholic = true;
            
            // If Chardonnay etc is in message and Product Name is empty, use variety as product name
            if (string.IsNullOrWhiteSpace(label.ProductName))
            {
                var varietyMatch = System.Text.RegularExpressions.Regex.Match(msg, @"\b(chardonnay|sauvignon blanc|merlot|pinot noir|riesling|cabernet sauvignon|shiraz)\b", opts);
                if (varietyMatch.Success)
                {
                    label.ProductName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(varietyMatch.Value);
                }
            }
        }

        // General Beverage Types
        if (string.IsNullOrWhiteSpace(label.BeverageType))
        {
            var typeMap = new Dictionary<string, string>
            {
                { "buğday birası", "Weizenbier (Wheat Beer)" }, { "weizenbier", "Weizenbier" }, { "weissbier", "Weißbier" },
                { "beer", "Beer" }, { "bier", "Bier" }, { "bira", "Bier" }, { "lager", "Lager" }, { "ale", "Ale" }, { "pilsner", "Pilsner" }, { "pils", "Pils" }, { "ipa", "IPA (Craft Beer)" },
                { "wine", "Wein (Wine)" }, { "wein", "Wein" }, { "şarap", "Wein" }, { "rosé", "Rosé" }, { "sekt", "Sekt" }, { "champagne", "Champagner" },
                { "juice", "Fruchtsaft (Juice)" }, { "saft", "Saft" }, { "meyve suyu", "Saft" }, { "nektar", "Nektar" }, { "fruchtsaft", "Fruchtsaft" },
                { "soda", "Soda" }, { "cola", "Cola" }, { "lemonade", "Limonade" }, { "limonade", "Limonade" },
                { "water", "Wasser (Water)" }, { "wasser", "Wasser" }, { "mineralwasser", "Mineralwasser" }, { "su", "Wasser" },
                { "spirit", "Spirituose" }, { "vodka", "Wodka" }, { "whisky", "Whisky" }, { "rum", "Rum" }, { "gin", "Gin" }, { "likör", "Likör" },
                { "energy", "Energy Drink" }, { "energy drink", "Energy Drink" },
                { "tea", "Tee" }, { "tee", "Tee" }, { "eistee", "Eistee" }, { "çay", "Tee" },
                { "coffee", "Kaffee" }, { "kaffee", "Kaffee" }, { "kahve", "Kaffee" }
            };

            foreach (var (keyword, type) in typeMap)
            {
                if (msgLower.Contains(keyword))
                {
                    label.BeverageType = type;
                    if (type.Contains("Bier") || type.Contains("Beer") || type.Contains("Wein") || type.Contains("Wine") || type.Contains("Spirituose") || type.Contains("Likör") || type.Contains("Pils") || type.Contains("Lager") || type.Contains("IPA")) 
                        label.IsAlcoholic = true;
                    break;
                }
            }
        }

        // ============================================================
        // PASS 5: Volume & Quantities
        // ============================================================
        var volumeMatch = System.Text.RegularExpressions.Regex.Match(msg, 
            @"(\d+(?:[.,]\d+)?)\s*(ml|l|liter|litre|cl)\b", opts);
        if (volumeMatch.Success && string.IsNullOrWhiteSpace(label.Volume))
        {
            label.Volume = volumeMatch.Value.Trim();
        }

        // ============================================================
        // PASS 6: Alcohol Percentage (ABV)
        // ============================================================
        // Match explicit "%5.4", "5.4%", "14% vol", "14 alc" or standalone numbers like 14 in context
        var alcMatch = System.Text.RegularExpressions.Regex.Match(msg,
            @"(?:%\s*(\d+(?:[.,]\d+)?)|(\d+(?:[.,]\d+)?)\s*%\s*(?:vol|alcohol|alc|abv)?)", opts);
        if (alcMatch.Success && (alcMatch.Groups[1].Success || alcMatch.Groups[2].Success))
        {
            var alcStr = (!string.IsNullOrEmpty(alcMatch.Groups[1].Value) ? alcMatch.Groups[1].Value : alcMatch.Groups[2].Value).Replace(',', '.');
            if (decimal.TryParse(alcStr, System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out var alcValue) && alcValue <= 100)
            {
                label.AlcoholContent = alcValue;
                label.IsAlcoholic = alcValue > 0.5m;
            }
        }
        else if (!label.AlcoholContent.HasValue && label.IsAlcoholic)
        {
            // Match standalone numbers like "14" when beverage is alcoholic (e.g. "Chardonnay Davraz Winery 14 apple 3212321313")
            var standaloneNumMatch = System.Text.RegularExpressions.Regex.Match(msg, @"\b([5-9]|[1-6][0-9])\b");
            if (standaloneNumMatch.Success)
            {
                if (decimal.TryParse(standaloneNumMatch.Value, out var num) && num >= 4 && num <= 70)
                {
                    label.AlcoholContent = num;
                }
            }
        }

        // Non-alcoholic explicit check
        if (msgLower.Contains("alkoholfrei") || msgLower.Contains("non-alcoholic") || 
            msgLower.Contains("alcohol-free") || msgLower.Contains("alcohol free") || msgLower.Contains("alkolsüz"))
        {
            label.IsAlcoholic = false;
            label.AlcoholContent = 0;
        }

        // ============================================================
        // PASS 7: Barcode (EAN-13, EAN-8, QR Code)
        // ============================================================
        var ean13Match = System.Text.RegularExpressions.Regex.Match(msg, @"\b(\d{13})\b");
        if (ean13Match.Success && string.IsNullOrWhiteSpace(label.BarcodeData))
        {
            label.BarcodeData = ean13Match.Groups[1].Value;
            label.BarcodeType = "EAN13";
        }
        else if (string.IsNullOrWhiteSpace(label.BarcodeData))
        {
            // Any sequence of 8 to 13 digits provided as barcode
            var anyDigitsMatch = System.Text.RegularExpressions.Regex.Match(msg, @"\b(\d{8,13})\b");
            if (anyDigitsMatch.Success)
            {
                label.BarcodeData = anyDigitsMatch.Groups[1].Value;
                label.BarcodeType = anyDigitsMatch.Groups[1].Value.Length == 8 ? "EAN8" : "EAN13";
            }
        }

        // ============================================================
        // PASS 8: Natural Ingredients & Flavors
        // ============================================================
        if (string.IsNullOrWhiteSpace(label.Ingredients))
        {
            var ingrNL = System.Text.RegularExpressions.Regex.Match(msg,
                @"(?:zutaten sind|inhaltsstoffe sind|ingredients are|içindekiler|zutaten:|inhaltsstoffe:|ingredients:)\s*([^.\n]+)", opts);
            if (ingrNL.Success)
            {
                label.Ingredients = ingrNL.Groups[1].Value.Trim();
            }
            else
            {
                // Common flavor/ingredient keywords: "apple", "elma", "apfel", "water", "trauben", "grape"
                var flavorMatch = System.Text.RegularExpressions.Regex.Match(msg, @"\b(apple|elma|apfel|grape|trauben|birne|pear|pfirsich|peach|zitrone|lemon)\b", opts);
                if (flavorMatch.Success)
                {
                    label.Ingredients = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(flavorMatch.Value);
                }
            }
        }

        // ============================================================
        // PASS 9: Direct Field Assignment for Short Answers
        // ============================================================
        var wordsClean = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wordsClean.Length <= 4 && !msg.Contains(":") && !msg.Contains("="))
        {
            // If user just sends a single entity response
            if (string.IsNullOrWhiteSpace(label.ProductName))
            {
                label.ProductName = msg;
            }
            else if (string.IsNullOrWhiteSpace(label.BrandName))
            {
                label.BrandName = msg;
            }
            else if (string.IsNullOrWhiteSpace(label.Ingredients))
            {
                label.Ingredients = msg;
            }
        }
    }

    /// <summary>
    /// Gets the current state of a conversation.
    /// </summary>
    public ConversationState? GetConversation(string connectionId)
    {
        _conversations.TryGetValue(connectionId, out var state);
        return state;
    }

    /// <summary>
    /// Removes a conversation state.
    /// </summary>
    public void RemoveConversation(string connectionId)
    {
        _conversations.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// Gets the greeting message for a new conversation.
    /// </summary>
    public string GetGreetingMessage()
    {
        return "👋 Willkommen! Ich bin Ihr **Getränkeetiketten-KI-Agent**.\n\n" +
               "Ich helfe Ihnen, ein druckfertiges Etikett für Ihr Getränkeprodukt zu erstellen. " +
               "Beschreiben Sie mir Ihr Produkt einfach in natürlicher Sprache (Deutsch oder Englisch).\n\n" +
               "**Beispieleingabe:**\n" +
               "> *\"Wir produzieren ein neues Weizenbier namens Goldweizen für die Marke Brauhaus Alpenblick. " +
               "500ml Flasche mit 5.4% Alkohol. EAN: 4006381333931. Zutaten: Wasser, Weizenmalz, Arpamalz, Hefe.\"*\n\n" +
               "Benötigte Pflichtangaben:\n" +
               "• Produktname & Marke\n" +
               "• Getränkeart & Füllmenge\n" +
               "• Zutatenliste\n" +
               "• Barcode (EAN-13)\n\n" +
               "Wie lauten die Details zu Ihrem Getränk? 🍺🍷🧃";
    }
}

// === Internal DTOs for LLM response parsing ===

public class LLMAgentOutput
{
    public string? Message { get; set; }
    public ExtractedLabelData? ExtractedData { get; set; }
    public List<string>? Contradictions { get; set; }
    public bool ReadyToGenerate { get; set; }
}

public class ExtractedLabelData
{
    public string? ProductName { get; set; }
    public string? BrandName { get; set; }
    public string? BeverageType { get; set; }
    public string? Volume { get; set; }
    public decimal? AlcoholContent { get; set; }
    public bool? IsAlcoholic { get; set; }
    public string? Ingredients { get; set; }
    public string? Allergens { get; set; }
    public string? NutritionalInfo { get; set; }
    public string? EnergyKj { get; set; }
    public string? EnergyKcal { get; set; }
    public string? BatchNumber { get; set; }
    public string? BestBeforeDate { get; set; }
    public string? ManufacturerName { get; set; }
    public string? ManufacturerAddress { get; set; }
    public string? CountryOfOrigin { get; set; }
    public string? Description { get; set; }
    public string? StorageInstructions { get; set; }
    public string? CertificationMarks { get; set; }
    public string? BarcodeData { get; set; }
    public string? BarcodeType { get; set; }
}
