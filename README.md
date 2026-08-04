# Beverage Label Agent 

**Chat-Agent für druckfertige Getränkeetiketten**

An AI-powered full-stack web application that helps beverage manufacturers create print-ready product labels through a conversational chat interface. The agent collects product information via natural language, detects incomplete or contradictory data, and generates professional labels with barcodes via the TEC-IT API.

## Features

-  **AI-powered chat agent** — Understands natural language input in English and German
-  **Smart data extraction** — Automatically extracts product details from conversation
-  **Contradiction detection** — Identifies inconsistencies (e.g., "non-alcoholic beer with 5% ABV")
-  **Follow-up questions** — Asks targeted questions for missing information
-  **Label generation** — Creates professional, print-ready label previews
-  **Barcode integration** — Generates EAN-13, QR codes, Code 128 via TEC-IT API
-  **Print & download** — Print labels or download as HTML
-  **Responsive design** — Works on desktop and mobile devices
-  **Real-time** — WebSocket communication via SignalR

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Backend | ASP.NET Core 9, C# |
| Frontend | Vanilla HTML, CSS, JavaScript |
| Real-time | SignalR (WebSocket) |
| AI/LLM | Google Gemini API (with rule-based fallback) |
| Barcode | TEC-IT REST API |
| Tests | xUnit, Moq |

## Quick Start

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- (Optional) [Google Gemini API Key](https://aistudio.google.com/apikey) for LLM features

### Setup

1. **Clone / Extract the project**

2. **Configure (optional — Gemini API key)**
   Edit `BeverageLabelAgent.Server/appsettings.json`:
   ```json
   {
     "Gemini": {
       "ApiKey": "YOUR_GEMINI_API_KEY_HERE",
       "Model": "gemini-2.0-flash"
     }
   }
   ```

3. **Run**
   ```bash
   cd BeverageLabelAgent.Server
   dotnet run
   ```

4. **Open** http://localhost:5000 in your browser

### Run Tests
```bash
cd BeverageLabelAgent.Tests
dotnet test
```

## Architecture

```
                    ┌─────────────────────────────┐
                    │     Browser (Frontend)      │
                     HTML/CSS/JS + SignalR Client │
                    └──────────┬──────────────────┘
                               │ WebSocket
                    ┌──────────▼──────────────────┐
                    │    ASP.NET Core Server      │
                    │  ┌─────────────────────┐    │
                    │  │   SignalR ChatHub   │    │
                    │  └────────┬────────────┘    │
                    │           │                 │
                    │  ┌────────▼────────────┐    │
                    │  │  LabelAgentService  │    │
                    │  │  (Orchestrator)     │    │
                    │  └──┬───────────┬──────┘    │
                    │     │           │           │
                    │  ┌──▼──┐  ┌─────▼─────────┐ │
                    │  │ LLM │  │ LabelRenderer │ │
                    │  │     │  │ + TecIT Client│ │
                    │  └──┬──┘  └────┬──────────┘ │
                    └─────┼──────────┼────────────┘
                          │          │
               ┌───────────▼┐  ┌─────▼────────────┐
               │ Gemini API │  │barcode.tec-it.com│
               └────────────┘  └──────────────────┘
```

## Example Chat Flow

```
User: "We have a new wheat beer called Goldweizen from Brauhaus Alpenblick. 
       It's 500ml with 5.4% alcohol."

Agent: "Great! I've captured the following:
        • Product: Goldweizen
        • Brand: Brauhaus Alpenblick  
        • Type: Beer (Wheat Beer)
        • Volume: 500 ml
        • Alcohol: 5.4% vol
        
        I still need:
        • Ingredients list
        • EAN-13 barcode number
        
        Could you provide these?"

User: "Ingredients are water, wheat malt, barley malt, hops, yeast. 
       EAN is 4012345678901. Allergens: gluten."

Agent: "Perfect! All required fields are complete. Generating your label..."
       [Label Preview with embedded barcode appears]
```

## Known Limitations

- Label download as PNG may not work in all browsers (HTML download fallback provided)
- Without Gemini API key, uses rule-based fallback with limited NLU
- In-memory conversation state (not persistent across server restarts)
- Single-user per connection (no authentication)

## License

This project was created as a technical evaluation task for TEC-IT.
