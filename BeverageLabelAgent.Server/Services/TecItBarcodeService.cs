using System.Net.Http;
using System.Web;

namespace BeverageLabelAgent.Server.Services;

/// <summary>
/// Client for the TEC-IT Barcode REST API.
/// Generates barcode images by constructing URLs with the proper parameters.
/// </summary>
public class TecItBarcodeService
{
    private readonly string _baseUrl;
    private readonly string _accessId;
    private readonly ILogger<TecItBarcodeService> _logger;
    private readonly HttpClient _httpClient;

    public TecItBarcodeService(IConfiguration configuration, ILogger<TecItBarcodeService> logger, HttpClient httpClient)
    {
        _baseUrl = configuration["TecIt:BaseUrl"] ?? "https://barcode.tec-it.com/barcode.ashx";
        _accessId = configuration["TecIt:AccessId"] ?? "";
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Constructs the full URL for generating a barcode image.
    /// </summary>
    public string GetBarcodeUrl(string data, string codeType = "EAN13", int dpi = 300, 
        string imageType = "png", bool showHrt = true, string? moduleWidth = null)
    {
        // Auto-fix EAN13 data if 12 or 13 digits provided
        if (codeType.Equals("EAN13", StringComparison.OrdinalIgnoreCase))
        {
            data = FixOrNormalizeEan13(data);
        }

        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        
        if (!string.IsNullOrWhiteSpace(_accessId))
            queryParams["accessid"] = _accessId;
        
        queryParams["code"] = codeType;
        queryParams["data"] = data;
        queryParams["dpi"] = dpi.ToString();
        queryParams["imagetype"] = imageType;
        queryParams["showhrt"] = showHrt ? "yes" : "no";
        
        if (!string.IsNullOrWhiteSpace(moduleWidth))
            queryParams["modulewidth"] = moduleWidth;

        var url = $"{_baseUrl}?{queryParams}";
        _logger.LogInformation("Generated barcode URL: {Url}", url);
        return url;
    }

    /// <summary>
    /// Downloads the barcode image bytes from TEC-IT API.
    /// Used for embedding in generated label HTML.
    /// </summary>
    public async Task<byte[]?> GetBarcodeImageBytesAsync(string data, string codeType = "EAN13", 
        int dpi = 300, string imageType = "png")
    {
        try
        {
            var url = GetBarcodeUrl(data, codeType, dpi, imageType, showHrt: true);
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("TEC-IT API returned {StatusCode} for barcode generation", response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch barcode image from TEC-IT API");
            return null;
        }
    }

    /// <summary>
    /// Gets a barcode image as a base64-encoded data URI for inline HTML embedding.
    /// </summary>
    public async Task<string?> GetBarcodeAsDataUriAsync(string data, string codeType = "EAN13", 
        int dpi = 300)
    {
        var imageBytes = await GetBarcodeImageBytesAsync(data, codeType, dpi, "png");
        if (imageBytes == null || imageBytes.Length == 0) return null;
        
        var base64 = Convert.ToBase64String(imageBytes);
        return $"data:image/png;base64,{base64}";
    }

    /// <summary>
    /// Auto-fixes 12 or 13 digit strings into valid EAN-13 barcodes by calculating the correct check digit.
    /// </summary>
    public static string FixOrNormalizeEan13(string data)
    {
        var digits = new string(data.Where(char.IsDigit).ToArray());
        if (digits.Length == 12)
        {
            return digits + CalculateCheckDigit(digits);
        }
        else if (digits.Length == 13)
        {
            var prefix = digits.Substring(0, 12);
            return prefix + CalculateCheckDigit(prefix);
        }
        return data;
    }

    /// <summary>
    /// Calculates the EAN-13 check digit for the first 12 digits.
    /// </summary>
    public static char CalculateCheckDigit(string first12Digits)
    {
        if (first12Digits.Length < 12) return '0';
        
        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int digit = first12Digits[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;
        }
        int checkDigit = (10 - (sum % 10)) % 10;
        return (char)('0' + checkDigit);
    }

    /// <summary>
    /// Validates whether the given data is compatible with the barcode type.
    /// </summary>
    public (bool IsValid, string? ErrorMessage) ValidateBarcodeData(string data, string codeType)
    {
        switch (codeType.ToUpperInvariant())
        {
            case "EAN13":
                var digits = new string(data.Where(char.IsDigit).ToArray());
                if (digits.Length != 12 && digits.Length != 13)
                    return (false, $"EAN-13 requires 12 or 13 digits. Got {digits.Length} digits.");
                return (true, null);

            case "EAN8":
                var digits8 = new string(data.Where(char.IsDigit).ToArray());
                if (digits8.Length != 8)
                    return (false, $"EAN-8 requires exactly 8 digits. Got {digits8.Length} digits.");
                return (true, null);

            case "QRCODE":
                if (string.IsNullOrWhiteSpace(data))
                    return (false, "QR Code data cannot be empty.");
                if (data.Length > 4296)
                    return (false, "QR Code data exceeds maximum length of 4296 characters.");
                return (true, null);

            case "CODE128":
                if (string.IsNullOrWhiteSpace(data))
                    return (false, "Code 128 data cannot be empty.");
                return (true, null);

            default:
                return (true, null);
        }
    }

    private bool ValidateEan13CheckDigit(string digits)
    {
        if (digits.Length != 13) return false;
        
        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int digit = digits[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;
        }
        
        int checkDigit = (10 - (sum % 10)) % 10;
        return checkDigit == (digits[12] - '0');
    }
}
