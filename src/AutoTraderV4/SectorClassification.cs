namespace AutoTraderV4;

public static class SectorClassification
{
    public const string Unclassified = "Unclassified";

    public static string? Resolve(
        string symbol,
        string? providerSector = null,
        string? persistedSector = null)
    {
        if (IsClassified(providerSector))
        {
            return providerSector!.Trim();
        }

        if (IsClassified(persistedSector))
        {
            return persistedSector!.Trim();
        }

        var normalized = symbol.Split('_')[0].Split('.')[0].TrimStart('^').ToUpperInvariant();
        return normalized switch
        {
            "NVDA" or "MSFT" or "AAPL" or "AMD" or "META" or "GOOGL" => "Technology",
            "V" or "MA" or "JPM" or "GS" => "Financials",
            "LLY" or "JNJ" or "PFE" => "Healthcare",
            "XOM" or "CVX" => "Energy",
            "PG" or "KO" or "PEP" => "Consumer",
            "SPX" or "GSPC" or "IXIC" or "DJI" or "FTSE" or "VIX" => "Market",
            _ => null
        };
    }

    public static bool CouldBelongTo(PortfolioPosition position, string sector)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentException.ThrowIfNullOrWhiteSpace(sector);

        var positionSector = Resolve(position.Ticker, persistedSector: position.Sector);
        return positionSector is null
            || string.Equals(positionSector, sector, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClassified(string? sector)
    {
        return !string.IsNullOrWhiteSpace(sector)
            && !string.Equals(sector.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sector.Trim(), Unclassified, StringComparison.OrdinalIgnoreCase);
    }
}
