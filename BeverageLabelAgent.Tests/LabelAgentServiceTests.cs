using BeverageLabelAgent.Server.Models;
using BeverageLabelAgent.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeverageLabelAgent.Tests;

public class LabelAgentServiceTests
{
    private LabelAgentService CreateAgentService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TecIt:BaseUrl"] = "https://barcode.tec-it.com/barcode.ashx",
                ["TecIt:AccessId"] = "JOBEVALH3C6A1E77C228",
                ["Gemini:ApiKey"] = "" // Empty API key to test fallback agent directly
            })
            .Build();

        var http = new HttpClient();
        var tecItLogger = new Mock<ILogger<TecItBarcodeService>>().Object;
        var tecItService = new TecItBarcodeService(config, tecItLogger, http);

        var geminiLogger = new Mock<ILogger<GeminiLLMService>>().Object;
        var geminiService = new GeminiLLMService(config, geminiLogger, http);

        var rendererLogger = new Mock<ILogger<LabelRendererService>>().Object;
        var renderer = new LabelRendererService(tecItService, rendererLogger);

        var agentLogger = new Mock<ILogger<LabelAgentService>>().Object;
        return new LabelAgentService(geminiService, tecItService, renderer, agentLogger);
    }

    [Fact]
    public void ExtractDataFromMessage_GermanNaturalLanguage_ExtractsAllFields()
    {
        var agent = CreateAgentService();
        var label = new BeverageLabel();

        var input = "Brauhaus Alpenblick markası için Goldweizen adında yeni bir buğday birası üretiyoruz. 500ml şişe, %5.4 alkol oranına sahip. EAN numarası: 4006381333931. Ingredients: Wasser, Weizenmalz, Gerstenmalz, Hefe.";

        agent.ExtractDataFromMessage(label, input);

        Assert.Equal("Goldweizen", label.ProductName);
        Assert.Equal("Brauhaus Alpenblick", label.BrandName);
        Assert.Equal("500ml", label.Volume);
        Assert.Equal(5.4m, label.AlcoholContent);
        Assert.Equal("4006381333931", label.BarcodeData);
        Assert.Equal("Wasser, Weizenmalz, Gerstenmalz, Hefe.", label.Ingredients);
    }

    [Fact]
    public void ExtractDataFromMessage_KeyValueFormat_ExtractsAllFields()
    {
        var agent = CreateAgentService();
        var label = new BeverageLabel();

        var input = "Product name: \"Goldweizen\" Brand name: \"Brauhaus Alpenblick\" Ingredients: \"su, buğday, arpa maltı, maya\" Volume: 500 ml EAN: 4006381333931 Beer %5.4";

        agent.ExtractDataFromMessage(label, input);

        Assert.Equal("Goldweizen", label.ProductName);
        Assert.Equal("Brauhaus Alpenblick", label.BrandName);
        Assert.Equal("su, buğday, arpa maltı, maya", label.Ingredients);
        Assert.Equal("500 ml", label.Volume);
        Assert.Equal("4006381333931", label.BarcodeData);
    }

    [Fact]
    public void ExtractDataFromMessage_ComplexWineryInput_ExtractsEntities()
    {
        var agent = CreateAgentService();
        var label = new BeverageLabel();

        var input = "Chardonnay Davraz Winery 14 apple 4006381333931";

        agent.ExtractDataFromMessage(label, input);

        Assert.Equal("Chardonnay", label.ProductName);
        Assert.Equal("Davraz Winery", label.BrandName);
        Assert.Equal("Wein (Wine)", label.BeverageType);
        Assert.Equal(14m, label.AlcoholContent);
        Assert.Equal("Apple", label.Ingredients);
        Assert.Equal("4006381333931", label.BarcodeData);
    }
}
