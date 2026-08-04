using BeverageLabelAgent.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeverageLabelAgent.Tests;

public class TecItBarcodeServiceTests
{
    private TecItBarcodeService CreateService(string? accessId = "TESTKEY123")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TecIt:BaseUrl"] = "https://barcode.tec-it.com/barcode.ashx",
                ["TecIt:AccessId"] = accessId
            })
            .Build();

        var logger = new Mock<ILogger<TecItBarcodeService>>();
        var httpClient = new HttpClient();

        return new TecItBarcodeService(config, logger.Object, httpClient);
    }

    [Fact]
    public void GetBarcodeUrl_EAN13_ReturnsCorrectUrl()
    {
        var service = CreateService();
        var url = service.GetBarcodeUrl("4006381333931", "EAN13");

        Assert.Contains("barcode.tec-it.com", url);
        Assert.Contains("code=EAN13", url);
        Assert.Contains("data=4006381333931", url);
        Assert.Contains("accessid=TESTKEY123", url);
    }

    [Fact]
    public void GetBarcodeUrl_QRCode_ReturnsCorrectUrl()
    {
        var service = CreateService();
        var url = service.GetBarcodeUrl("https://example.com", "QRCode");

        Assert.Contains("code=QRCode", url);
        Assert.Contains("data=", url);
    }

    [Fact]
    public void GetBarcodeUrl_WithoutAccessId_OmitsParameter()
    {
        var service = CreateService(accessId: "");
        var url = service.GetBarcodeUrl("12345", "Code128");

        Assert.DoesNotContain("accessid", url);
    }

    [Fact]
    public void ValidateBarcodeData_ValidEAN13_ReturnsValid()
    {
        var service = CreateService();
        var (isValid, error) = service.ValidateBarcodeData("4006381333931", "EAN13");

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateBarcodeData_InvalidEAN13Length_ReturnsInvalid()
    {
        var service = CreateService();
        var (isValid, error) = service.ValidateBarcodeData("12345", "EAN13");

        Assert.False(isValid);
        Assert.Contains("13 digits", error);
    }

    [Fact]
    public void ValidateBarcodeData_EmptyQRCode_ReturnsInvalid()
    {
        var service = CreateService();
        var (isValid, error) = service.ValidateBarcodeData("", "QRCode");

        Assert.False(isValid);
        Assert.Contains("empty", error);
    }

    [Fact]
    public void ValidateBarcodeData_ValidCode128_ReturnsValid()
    {
        var service = CreateService();
        var (isValid, error) = service.ValidateBarcodeData("HELLO-123", "Code128");

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void GetBarcodeUrl_HighDpi_IncludesDpiParameter()
    {
        var service = CreateService();
        var url = service.GetBarcodeUrl("4006381333931", "EAN13", dpi: 300);

        Assert.Contains("dpi=300", url);
    }

    [Fact]
    public void GetBarcodeUrl_PngFormat_IncludesFormatParameter()
    {
        var service = CreateService();
        var url = service.GetBarcodeUrl("4006381333931", "EAN13", imageType: "png");

        Assert.Contains("imagetype=png", url);
    }
}
