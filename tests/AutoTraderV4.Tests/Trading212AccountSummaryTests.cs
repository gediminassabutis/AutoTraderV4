using System.Text.Json;

namespace AutoTraderV4.Tests;

public class Trading212AccountSummaryTests
{
    [Fact]
    public void Deserialize_WhenCashAndEquityAreWrappedObjects_ParsesDecimalValues()
    {
        const string json = """
        {
          "id": 12345,
          "currency": "USD",
          "cash": { "amount": 1500.75 },
          "equity": { "amount": 25000.50 }
        }
        """;

        var result = JsonSerializer.Deserialize<Trading212AccountSummary>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal(12345, result!.Id);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(1500.75m, result.Cash);
        Assert.Equal(25000.50m, result.Equity);
    }

    [Fact]
    public void Deserialize_WhenCashAndEquityArePlainNumbers_ParsesDecimalValues()
    {
        const string json = """
        {
          "id": 12345,
          "currency": "USD",
          "cash": 1500.75,
          "equity": 25000.50
        }
        """;

        var result = JsonSerializer.Deserialize<Trading212AccountSummary>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal(1500.75m, result!.Cash);
        Assert.Equal(25000.50m, result.Equity);
    }
}
