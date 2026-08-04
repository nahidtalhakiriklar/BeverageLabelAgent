using BeverageLabelAgent.Server.Models;

namespace BeverageLabelAgent.Tests;

public class BeverageLabelTests
{
    [Fact]
    public void GetMissingRequiredFields_AllEmpty_ReturnsAllRequired()
    {
        var label = new BeverageLabel();
        var missing = label.GetMissingRequiredFields();
        
        Assert.Equal(6, missing.Count);
        Assert.Contains("Product Name", missing);
        Assert.Contains("Brand Name", missing);
        Assert.Contains("Beverage Type", missing);
        Assert.Contains("Volume", missing);
        Assert.Contains("Ingredients", missing);
        Assert.Contains("Barcode (EAN) Number", missing);
    }

    [Fact]
    public void GetMissingRequiredFields_AllFilled_ReturnsEmpty()
    {
        var label = new BeverageLabel
        {
            ProductName = "Alpine Gold",
            BrandName = "Mountain Brew",
            BeverageType = "Beer",
            Volume = "330 ml",
            Ingredients = "Water, Barley, Hops, Yeast",
            BarcodeData = "4006381333931"
        };

        var missing = label.GetMissingRequiredFields();
        Assert.Empty(missing);
    }

    [Fact]
    public void GetContradictions_NonAlcoholicWithHighAbv_ReturnsContradiction()
    {
        var label = new BeverageLabel
        {
            IsAlcoholic = false,
            AlcoholContent = 5.2m
        };

        var contradictions = label.GetContradictions();
        Assert.Single(contradictions);
        Assert.Contains("non-alcoholic", contradictions[0]);
    }

    [Fact]
    public void GetContradictions_AlcoholicWithNoAbv_ReturnsContradiction()
    {
        var label = new BeverageLabel
        {
            IsAlcoholic = true,
            AlcoholContent = null
        };

        var contradictions = label.GetContradictions();
        Assert.Single(contradictions);
        Assert.Contains("alcoholic", contradictions[0]);
    }

    [Fact]
    public void GetContradictions_InvalidEan13Length_ReturnsContradiction()
    {
        var label = new BeverageLabel
        {
            BarcodeData = "12345",
            BarcodeType = "EAN13"
        };

        var contradictions = label.GetContradictions();
        Assert.Contains(contradictions, c => c.Contains("13 digits"));
    }

    [Fact]
    public void GetContradictions_ValidData_ReturnsNoContradictions()
    {
        var label = new BeverageLabel
        {
            IsAlcoholic = true,
            AlcoholContent = 5.0m,
            Volume = "500 ml",
            BarcodeData = "4006381333931",
            BarcodeType = "EAN13"
        };

        var contradictions = label.GetContradictions();
        Assert.Empty(contradictions);
    }

    [Fact]
    public void GetContradictions_UnreasonablyLargeVolume_ReturnsContradiction()
    {
        var label = new BeverageLabel
        {
            Volume = "50000 ml"
        };

        var contradictions = label.GetContradictions();
        Assert.Contains(contradictions, c => c.Contains("unusually large"));
    }

    [Fact]
    public void GetCompletenessPercentage_Empty_ReturnsLow()
    {
        var label = new BeverageLabel();
        var pct = label.GetCompletenessPercentage();
        
        // Only isAlcoholic defaults to false, which counts as filled for non-alcoholic
        Assert.True(pct < 20);
    }

    [Fact]
    public void GetCompletenessPercentage_FullyFilled_ReturnsHigh()
    {
        var label = new BeverageLabel
        {
            ProductName = "Alpine Gold",
            BrandName = "Mountain Brew",
            BeverageType = "Beer",
            Volume = "330 ml",
            Ingredients = "Water, Barley, Hops, Yeast",
            BarcodeData = "4006381333931",
            ManufacturerName = "Mountain Brew GmbH",
            CountryOfOrigin = "Austria",
            BatchNumber = "L2024-0815",
            BestBeforeDate = "12/2025",
            Allergens = "Gluten",
            IsAlcoholic = true,
            AlcoholContent = 5.2m
        };

        var pct = label.GetCompletenessPercentage();
        Assert.Equal(100, pct);
    }
}
