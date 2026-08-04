namespace BeverageLabelAgent.Server.Models;

/// <summary>
/// Represents all data fields for a beverage label.
/// Required fields are marked; the agent will ask follow-up questions for missing ones.
/// </summary>
public class BeverageLabel
{
    // === Required Fields ===
    public string? ProductName { get; set; }
    public string? BrandName { get; set; }
    public string? BeverageType { get; set; }       // Beer, Wine, Juice, Soda, Water, Spirit, etc.
    public string? Volume { get; set; }              // e.g., "330 ml", "0.5 L", "750 ml"
    
    // === Barcode ===
    public string? BarcodeData { get; set; }         // EAN-13 number (13 digits)
    public string BarcodeType { get; set; } = "EAN13"; // EAN13, QRCode, Code128, etc.
    
    // === Alcohol (if applicable) ===
    public decimal? AlcoholContent { get; set; }     // e.g., 5.0 means 5.0% vol
    public bool IsAlcoholic { get; set; } = false;
    
    // === Ingredients & Allergens ===
    public string? Ingredients { get; set; }         // Comma-separated list
    public string? Allergens { get; set; }           // e.g., "Gluten, Sulphites"
    
    // === Nutritional Information ===
    public string? NutritionalInfo { get; set; }     // Per 100ml values
    public string? EnergyKj { get; set; }
    public string? EnergyKcal { get; set; }
    
    // === Production & Traceability ===
    public string? BatchNumber { get; set; }         // Lot/Batch
    public string? BestBeforeDate { get; set; }      // e.g., "12/2025"
    public string? ProductionDate { get; set; }
    
    // === Manufacturer ===
    public string? ManufacturerName { get; set; }
    public string? ManufacturerAddress { get; set; }
    public string? CountryOfOrigin { get; set; }
    
    // === Optional ===
    public string? Description { get; set; }         // Marketing text / tagline
    public string? StorageInstructions { get; set; } // e.g., "Store in a cool, dry place"
    public string? CertificationMarks { get; set; }  // e.g., "Bio", "Fairtrade"

    /// <summary>
    /// Returns a list of missing required fields for a valid label.
    /// </summary>
    public List<string> GetMissingRequiredFields()
    {
        var missing = new List<string>();
        
        if (string.IsNullOrWhiteSpace(ProductName)) missing.Add("Product Name");
        if (string.IsNullOrWhiteSpace(BrandName)) missing.Add("Brand Name");
        if (string.IsNullOrWhiteSpace(BeverageType)) missing.Add("Beverage Type");
        if (string.IsNullOrWhiteSpace(Volume)) missing.Add("Volume");
        if (string.IsNullOrWhiteSpace(Ingredients)) missing.Add("Ingredients");
        if (string.IsNullOrWhiteSpace(BarcodeData)) missing.Add("Barcode (EAN) Number");
        
        return missing;
    }

    /// <summary>
    /// Returns a list of contradictions found in the data.
    /// </summary>
    public List<string> GetContradictions()
    {
        var contradictions = new List<string>();

        // Alcohol contradiction: marked as non-alcoholic but has alcohol content
        if (!IsAlcoholic && AlcoholContent.HasValue && AlcoholContent.Value > 0.5m)
        {
            contradictions.Add($"The product is marked as non-alcoholic, but has an alcohol content of {AlcoholContent.Value}% vol. Products with more than 0.5% ABV are typically classified as alcoholic.");
        }

        // Alcohol contradiction: marked as alcoholic but no/zero alcohol
        if (IsAlcoholic && (!AlcoholContent.HasValue || AlcoholContent.Value <= 0))
        {
            contradictions.Add("The product is marked as alcoholic, but no alcohol content has been specified.");
        }

        // Volume sanity check
        if (!string.IsNullOrWhiteSpace(Volume))
        {
            var volumeLower = Volume.ToLowerInvariant();
            if (volumeLower.Contains("ml"))
            {
                var numStr = new string(volumeLower.Replace("ml", "").Trim().Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
                if (decimal.TryParse(numStr.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var mlValue))
                {
                    if (mlValue > 10000) contradictions.Add($"Volume of {mlValue}ml seems unusually large for a beverage.");
                    if (mlValue < 10) contradictions.Add($"Volume of {mlValue}ml seems unusually small for a beverage.");
                }
            }
        }

        // EAN-13 validation
        if (!string.IsNullOrWhiteSpace(BarcodeData) && BarcodeType == "EAN13")
        {
            var digits = new string(BarcodeData.Where(char.IsDigit).ToArray());
            if (digits.Length != 13)
            {
                contradictions.Add($"EAN-13 barcode requires exactly 13 digits, but '{BarcodeData}' has {digits.Length} digits.");
            }
        }

        return contradictions;
    }

    /// <summary>
    /// Calculates a completeness percentage (0-100).
    /// </summary>
    public int GetCompletenessPercentage()
    {
        int total = 12; // Total meaningful fields we track
        int filled = 0;

        if (!string.IsNullOrWhiteSpace(ProductName)) filled++;
        if (!string.IsNullOrWhiteSpace(BrandName)) filled++;
        if (!string.IsNullOrWhiteSpace(BeverageType)) filled++;
        if (!string.IsNullOrWhiteSpace(Volume)) filled++;
        if (!string.IsNullOrWhiteSpace(Ingredients)) filled++;
        if (!string.IsNullOrWhiteSpace(BarcodeData)) filled++;
        if (!string.IsNullOrWhiteSpace(ManufacturerName)) filled++;
        if (!string.IsNullOrWhiteSpace(CountryOfOrigin)) filled++;
        if (!string.IsNullOrWhiteSpace(BatchNumber)) filled++;
        if (!string.IsNullOrWhiteSpace(BestBeforeDate)) filled++;
        if (!string.IsNullOrWhiteSpace(Allergens)) filled++;
        if (IsAlcoholic && AlcoholContent.HasValue) filled++;
        else if (!IsAlcoholic) filled++; // Non-alcoholic doesn't need ABV

        return (int)Math.Round((double)filled / total * 100);
    }
}
