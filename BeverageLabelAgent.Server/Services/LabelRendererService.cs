using BeverageLabelAgent.Server.Models;
using System.Text.Encodings.Web;

namespace BeverageLabelAgent.Server.Services;

/// <summary>
/// Generates print-ready HTML label layouts from BeverageLabel data.
/// Labels are designed to be rendered in-browser and printed at proper physical dimensions.
/// </summary>
public class LabelRendererService
{
    private readonly TecItBarcodeService _barcodeService;
    private readonly ILogger<LabelRendererService> _logger;

    public LabelRendererService(TecItBarcodeService barcodeService, ILogger<LabelRendererService> logger)
    {
        _barcodeService = barcodeService;
        _logger = logger;
    }

    /// <summary>
    /// Generates a complete HTML label for a beverage product.
    /// Includes embedded barcode from TEC-IT API.
    /// </summary>
    public async Task<string> GenerateLabelHtmlAsync(BeverageLabel label)
    {
        // Get barcode image
        string? barcodeDataUri = null;
        string? barcodeUrl = null;
        
        if (!string.IsNullOrWhiteSpace(label.BarcodeData))
        {
            barcodeUrl = _barcodeService.GetBarcodeUrl(
                label.BarcodeData, 
                label.BarcodeType ?? "EAN13",
                dpi: 300,
                imageType: "png",
                showHrt: true
            );
            
            barcodeDataUri = await _barcodeService.GetBarcodeAsDataUriAsync(
                label.BarcodeData,
                label.BarcodeType ?? "EAN13",
                dpi: 300
            );
        }

        var html = $@"
<div class=""label-container"">
    <div class=""label-inner"">
        {GenerateHeaderSection(label)}
        {GenerateProductInfoSection(label)}
        {GenerateIngredientsSection(label)}
        {GenerateNutritionSection(label)}
        {GenerateBarcodeSection(label, barcodeDataUri, barcodeUrl)}
        {GenerateManufacturerSection(label)}
        {GenerateFooterSection(label)}
    </div>
</div>";

        return html;
    }

    private string GenerateHeaderSection(BeverageLabel label)
    {
        var alcoholBadge = label.IsAlcoholic && label.AlcoholContent.HasValue
            ? $@"<span class=""label-alcohol-badge"">{label.AlcoholContent.Value:F1}% vol</span>"
            : "";

        var typeBadge = !string.IsNullOrWhiteSpace(label.BeverageType)
            ? $@"<span class=""label-type-badge"">{Encode(label.BeverageType)}</span>"
            : "";

        return $@"
        <div class=""label-header"">
            <div class=""label-brand"">{Encode(label.BrandName ?? "Brand")}</div>
            <div class=""label-product-name"">{Encode(label.ProductName ?? "Product")}</div>
            <div class=""label-badges"">
                {typeBadge}
                {alcoholBadge}
            </div>
            {(!string.IsNullOrWhiteSpace(label.Description) ? $@"<div class=""label-description"">{Encode(label.Description)}</div>" : "")}
        </div>";
    }

    private string GenerateProductInfoSection(BeverageLabel label)
    {
        var items = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(label.Volume))
            items.Add($@"<div class=""label-info-item""><span class=""label-info-icon"">📦</span> <strong>{Encode(label.Volume)}</strong></div>");
        
        if (!string.IsNullOrWhiteSpace(label.CountryOfOrigin))
            items.Add($@"<div class=""label-info-item""><span class=""label-info-icon"">🌍</span> {Encode(label.CountryOfOrigin)}</div>");

        if (!string.IsNullOrWhiteSpace(label.CertificationMarks))
            items.Add($@"<div class=""label-info-item""><span class=""label-info-icon"">✅</span> {Encode(label.CertificationMarks)}</div>");

        if (items.Count == 0) return "";

        return $@"
        <div class=""label-product-info"">
            {string.Join("\n            ", items)}
        </div>";
    }

    private string GenerateIngredientsSection(BeverageLabel label)
    {
        if (string.IsNullOrWhiteSpace(label.Ingredients)) return "";

        var allergensHtml = !string.IsNullOrWhiteSpace(label.Allergens)
            ? $@"<div class=""label-allergens""><strong>Allergens:</strong> {Encode(label.Allergens)}</div>"
            : "";

        return $@"
        <div class=""label-ingredients"">
            <div class=""label-section-title"">Ingredients</div>
            <div class=""label-ingredients-text"">{Encode(label.Ingredients)}</div>
            {allergensHtml}
        </div>";
    }

    private string GenerateNutritionSection(BeverageLabel label)
    {
        if (string.IsNullOrWhiteSpace(label.NutritionalInfo) && 
            string.IsNullOrWhiteSpace(label.EnergyKj) && 
            string.IsNullOrWhiteSpace(label.EnergyKcal))
            return "";

        var rows = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(label.EnergyKj) || !string.IsNullOrWhiteSpace(label.EnergyKcal))
        {
            var energy = "";
            if (!string.IsNullOrWhiteSpace(label.EnergyKj)) energy += $"{Encode(label.EnergyKj)} kJ";
            if (!string.IsNullOrWhiteSpace(label.EnergyKcal)) 
            {
                if (energy.Length > 0) energy += " / ";
                energy += $"{Encode(label.EnergyKcal)} kcal";
            }
            rows.Add($"<tr><td>Energy</td><td>{energy}</td></tr>");
        }

        if (!string.IsNullOrWhiteSpace(label.NutritionalInfo))
        {
            rows.Add($"<tr><td colspan=\"2\">{Encode(label.NutritionalInfo)}</td></tr>");
        }

        return $@"
        <div class=""label-nutrition"">
            <div class=""label-section-title"">Nutritional Information (per 100 ml)</div>
            <table class=""label-nutrition-table"">
                {string.Join("\n                ", rows)}
            </table>
        </div>";
    }

    private string GenerateBarcodeSection(BeverageLabel label, string? barcodeDataUri, string? barcodeUrl)
    {
        if (string.IsNullOrWhiteSpace(label.BarcodeData)) return "";

        var directUrl = barcodeUrl ?? _barcodeService.GetBarcodeUrl(label.BarcodeData, label.BarcodeType ?? "EAN13");
        var primarySrc = !string.IsNullOrWhiteSpace(barcodeDataUri) ? barcodeDataUri : directUrl;
        
        return $@"
        <div class=""label-barcode"">
            <img src=""{primarySrc}"" onerror=""this.onerror=null; this.src='{directUrl}';"" alt=""Barcode {Encode(label.BarcodeData)}"" class=""label-barcode-img"" />
        </div>";
    }

    private string GenerateManufacturerSection(BeverageLabel label)
    {
        if (string.IsNullOrWhiteSpace(label.ManufacturerName) && 
            string.IsNullOrWhiteSpace(label.ManufacturerAddress))
            return "";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(label.ManufacturerName))
            parts.Add(Encode(label.ManufacturerName));
        if (!string.IsNullOrWhiteSpace(label.ManufacturerAddress))
            parts.Add(Encode(label.ManufacturerAddress));

        return $@"
        <div class=""label-manufacturer"">
            <div class=""label-section-title"">Produced by</div>
            <div>{string.Join("<br/>", parts)}</div>
        </div>";
    }

    private string GenerateFooterSection(BeverageLabel label)
    {
        var items = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(label.BatchNumber))
            items.Add($"L: {Encode(label.BatchNumber)}");
        
        if (!string.IsNullOrWhiteSpace(label.BestBeforeDate))
            items.Add($"BBD: {Encode(label.BestBeforeDate)}");

        if (!string.IsNullOrWhiteSpace(label.StorageInstructions))
            items.Add(Encode(label.StorageInstructions));

        if (items.Count == 0) return "";

        return $@"
        <div class=""label-footer"">
            {string.Join(" &nbsp;|&nbsp; ", items)}
        </div>";
    }

    private string Encode(string? text)
    {
        return HtmlEncoder.Default.Encode(text ?? "");
    }
}
