using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AutoTraderV4.Tests;

public class Trading212ClientTests
{
    [Fact]
    public async Task GetAccountSummaryAsync_SendsCorrectRequestAndParsesNestedCashAndInvestments()
    {
        const string responseJson = """
        {
          "id": 12345,
          "currency": "USD",
          "cash": {
            "availableToTrade": 1500.75,
            "inPies": 200.25,
            "reservedForOrders": 75.5
          },
          "investments": {
            "currentValue": 25000.50,
            "realizedProfitLoss": 1200.25,
            "totalCost": 22000.00,
            "unrealizedProfitLoss": 3000.50
          },
          "totalValue": 26500.50
        }
        """;

        using var client = new HttpClient(new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://demo.trading212.com/api/v0/equity/account/summary", request.RequestUri!.ToString());
            Assert.Equal("Basic ZGVtby1rZXk6ZGVtby1zZWNyZXQ=", request.Headers.Authorization!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }));

        var sut = new Trading212Client(client, new Trading212Options
        {
            BaseUrl = "https://demo.trading212.com",
            ApiKey = "demo-key",
            ApiSecret = "demo-secret"
        });

        var result = await sut.GetAccountSummaryAsync();

        Assert.Equal(12345, result.Id);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(1500.75m, result.Cash);
        Assert.Equal(1500.75m, result.CashDetails.AvailableToTrade);
        Assert.Equal(200.25m, result.CashDetails.InPies);
        Assert.Equal(75.5m, result.CashDetails.ReservedForOrders);
        Assert.Equal(25000.50m, result.Investments.CurrentValue);
        Assert.Equal(22000.00m, result.Investments.TotalCost);
        Assert.Equal(26500.50m, result.TotalValue);
    }

    [Fact]
    public async Task GetPositionsAsync_ParsesAccountCurrencyWalletValues()
    {
        const string responseJson = """
        [
          {
            "instrument": {
              "currency": "USD",
              "ticker": "AAPL_US_EQ"
            },
            "quantity": 5,
            "averagePricePaid": 100,
            "currentPrice": 120,
            "walletImpact": {
              "currency": "USD",
              "currentValue": 600,
              "totalCost": 500,
              "unrealizedProfitLoss": 100
            }
          }
        ]
        """;

        using var client = new HttpClient(new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://demo.trading212.com/api/v0/equity/positions", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }));

        var sut = new Trading212Client(client, new Trading212Options
        {
            BaseUrl = "https://demo.trading212.com",
            ApiKey = "demo-key",
            ApiSecret = "demo-secret"
        });

        var positions = await sut.GetPositionsAsync();

        var position = Assert.Single(positions);
        Assert.Equal("AAPL_US_EQ", position.Instrument.Ticker);
        Assert.Equal(5m, position.Quantity);
        Assert.Equal(100m, position.AveragePricePaid);
        Assert.Equal(120m, position.CurrentPrice);
        Assert.Equal("USD", position.WalletImpact!.Currency);
        Assert.Equal(600m, position.WalletImpact.CurrentValue);
        Assert.Equal(500m, position.WalletImpact.TotalCost);
    }

    [Fact]
    public async Task PlaceOrderAsync_Market_SendsExpectedPayloadAndParsesOrderResult()
    {
        const string responseJson = """
        {
          "id": 6001,
          "ticker": "AAPL_US_EQ",
          "status": "CONFIRMED",
          "type": "MARKET",
          "side": "BUY",
          "quantity": 2.5,
          "filledQuantity": 0,
          "filledValue": 0,
          "limitPrice": 0,
          "stopPrice": 0,
          "value": 0,
          "currency": "USD",
          "timeInForce": "DAY",
          "extendedHours": true,
          "createdAt": "2024-01-01T12:00:00Z",
          "instrument": {
            "currency": "USD",
            "isin": "US0378331005",
            "name": "Apple Inc.",
            "ticker": "AAPL_US_EQ"
          }
        }
        """;

        using var client = new HttpClient(new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://demo.trading212.com/api/v0/equity/orders/market", request.RequestUri!.ToString());
            Assert.Equal("Basic ZGVtby1rZXk6ZGVtby1zZWNyZXQ=", request.Headers.Authorization!.ToString());
            Assert.Equal("application/json; charset=utf-8", request.Content!.Headers.ContentType!.ToString());

            var payload = JsonDocument.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("AAPL_US_EQ", payload.RootElement.GetProperty("ticker").GetString());
            Assert.Equal(2.5m, payload.RootElement.GetProperty("quantity").GetDecimal());
            Assert.True(payload.RootElement.GetProperty("extendedHours").GetBoolean());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }));

        var sut = new Trading212Client(client, new Trading212Options
        {
            BaseUrl = "https://demo.trading212.com",
            ApiKey = "demo-key",
            ApiSecret = "demo-secret"
        });

        var result = await sut.PlaceOrderAsync(new Trading212OrderRequest
        {
            Ticker = "AAPL_US_EQ",
            Quantity = 2.5m,
            Type = Trading212OrderType.Market,
            ExtendedHours = true
        });

        Assert.Equal(6001, result.Id);
        Assert.Equal("AAPL_US_EQ", result.Ticker);
        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal("MARKET", result.Type);
        Assert.Equal("BUY", result.Side);
        Assert.Equal(2.5m, result.Quantity);
        Assert.Equal(0m, result.FilledQuantity);
        Assert.True(result.ExtendedHours);
        Assert.Equal("AAPL_US_EQ", result.Instrument.Ticker);
        Assert.Equal("Apple Inc.", result.Instrument.Name);
    }

    [Fact]
    public async Task PlaceOrderAsync_Limit_SendsExpectedPayloadAndParsesOrderResult()
    {
        const string responseJson = """
        {
          "id": 6002,
          "ticker": "MSFT_US_EQ",
          "status": "NEW",
          "type": "LIMIT",
          "side": "BUY",
          "quantity": 1.5,
          "filledQuantity": 0,
          "filledValue": 0,
          "limitPrice": 420.25,
          "stopPrice": 0,
          "value": 0,
          "currency": "USD",
          "timeInForce": "DAY",
          "extendedHours": false,
          "createdAt": "2024-01-02T08:00:00Z",
          "instrument": {
            "currency": "USD",
            "isin": "US5949181045",
            "name": "Microsoft Corporation",
            "ticker": "MSFT_US_EQ"
          }
        }
        """;

        using var client = new HttpClient(new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://demo.trading212.com/api/v0/equity/orders/limit", request.RequestUri!.ToString());

            var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("MSFT_US_EQ", payload.RootElement.GetProperty("ticker").GetString());
            Assert.Equal(1.5m, payload.RootElement.GetProperty("quantity").GetDecimal());
            Assert.Equal(420.25m, payload.RootElement.GetProperty("limitPrice").GetDecimal());
            Assert.Equal("DAY", payload.RootElement.GetProperty("timeValidity").GetString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }));

        var sut = new Trading212Client(client, new Trading212Options
        {
            BaseUrl = "https://demo.trading212.com",
            ApiKey = "demo-key",
            ApiSecret = "demo-secret"
        });

        var result = await sut.PlaceOrderAsync(new Trading212OrderRequest
        {
            Ticker = "MSFT_US_EQ",
            Quantity = 1.5m,
            Type = Trading212OrderType.Limit,
            LimitPrice = 420.25m,
            TimeValidity = "DAY"
        });

        Assert.Equal(6002, result.Id);
        Assert.Equal("MSFT_US_EQ", result.Ticker);
        Assert.Equal("LIMIT", result.Type);
        Assert.Equal(420.25m, result.LimitPrice);
        Assert.Equal("DAY", result.TimeInForce);
    }

    [Fact]
    public async Task PlaceOrderAsync_Stop_SendsExpectedPayloadAndParsesOrderResult()
    {
        const string responseJson = """
        {
          "id": 6003,
          "ticker": "NVDA_US_EQ",
          "status": "CONFIRMED",
          "type": "STOP",
          "side": "SELL",
          "quantity": 3,
          "filledQuantity": 0,
          "filledValue": 0,
          "limitPrice": 0,
          "stopPrice": 955.00,
          "value": 0,
          "currency": "USD",
          "timeInForce": "GOOD_TILL_CANCEL",
          "extendedHours": false,
          "createdAt": "2024-01-03T09:15:00Z",
          "instrument": {
            "currency": "USD",
            "isin": "US67066G1040",
            "name": "NVIDIA Corporation",
            "ticker": "NVDA_US_EQ"
          }
        }
        """;

        using var client = new HttpClient(new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://demo.trading212.com/api/v0/equity/orders/stop", request.RequestUri!.ToString());

            var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("NVDA_US_EQ", payload.RootElement.GetProperty("ticker").GetString());
            Assert.Equal(3m, payload.RootElement.GetProperty("quantity").GetDecimal());
            Assert.Equal(955.00m, payload.RootElement.GetProperty("stopPrice").GetDecimal());
            Assert.Equal("GOOD_TILL_CANCEL", payload.RootElement.GetProperty("timeValidity").GetString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }));

        var sut = new Trading212Client(client, new Trading212Options
        {
            BaseUrl = "https://demo.trading212.com",
            ApiKey = "demo-key",
            ApiSecret = "demo-secret"
        });

        var result = await sut.PlaceOrderAsync(new Trading212OrderRequest
        {
            Ticker = "NVDA_US_EQ",
            Quantity = 3m,
            Type = Trading212OrderType.Stop,
            StopPrice = 955.00m,
            TimeValidity = "GOOD_TILL_CANCEL"
        });

        Assert.Equal(6003, result.Id);
        Assert.Equal("STOP", result.Type);
        Assert.Equal("SELL", result.Side);
        Assert.Equal(955.00m, result.StopPrice);
        Assert.Equal("GOOD_TILL_CANCEL", result.TimeInForce);
    }

    [Fact]
    public async Task PlaceOrderAsync_StopLimit_SendsExpectedPayloadAndParsesOrderResult()
    {
        const string responseJson = """
        {
          "id": 6004,
          "ticker": "AMZN_US_EQ",
          "status": "PARTIALLY_FILLED",
          "type": "STOP_LIMIT",
          "side": "BUY",
          "quantity": 2,
          "filledQuantity": 0.5,
          "filledValue": 150.00,
          "limitPrice": 170.00,
          "stopPrice": 165.00,
          "value": 340.00,
          "currency": "USD",
          "timeInForce": "DAY",
          "extendedHours": false,
          "createdAt": "2024-01-04T10:30:00Z",
          "instrument": {
            "currency": "USD",
            "isin": "US0231351067",
            "name": "Amazon.com, Inc.",
            "ticker": "AMZN_US_EQ"
          }
        }
        """;

        using var client = new HttpClient(new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://demo.trading212.com/api/v0/equity/orders/stop_limit", request.RequestUri!.ToString());

            var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("AMZN_US_EQ", payload.RootElement.GetProperty("ticker").GetString());
            Assert.Equal(2m, payload.RootElement.GetProperty("quantity").GetDecimal());
            Assert.Equal(170.00m, payload.RootElement.GetProperty("limitPrice").GetDecimal());
            Assert.Equal(165.00m, payload.RootElement.GetProperty("stopPrice").GetDecimal());
            Assert.Equal("DAY", payload.RootElement.GetProperty("timeValidity").GetString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }));

        var sut = new Trading212Client(client, new Trading212Options
        {
            BaseUrl = "https://demo.trading212.com",
            ApiKey = "demo-key",
            ApiSecret = "demo-secret"
        });

        var result = await sut.PlaceOrderAsync(new Trading212OrderRequest
        {
            Ticker = "AMZN_US_EQ",
            Quantity = 2m,
            Type = Trading212OrderType.StopLimit,
            LimitPrice = 170.00m,
            StopPrice = 165.00m,
            TimeValidity = "DAY"
        });

        Assert.Equal(6004, result.Id);
        Assert.Equal("STOP_LIMIT", result.Type);
        Assert.Equal(170.00m, result.LimitPrice);
        Assert.Equal(165.00m, result.StopPrice);
        Assert.Equal(0.5m, result.FilledQuantity);
        Assert.Equal("PARTIALLY_FILLED", result.Status);
    }

    [Theory]
    [InlineData(Trading212OrderType.Limit)]
    [InlineData(Trading212OrderType.Stop)]
    [InlineData(Trading212OrderType.StopLimit)]
    public async Task PlaceOrderAsync_RequiresRequiredPriceForConditionalOrderTypes(Trading212OrderType type)
    {
        var sut = new Trading212Client(new HttpClient(new RecordingHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK))),
            new Trading212Options
            {
                BaseUrl = "https://demo.trading212.com",
                ApiKey = "demo-key",
                ApiSecret = "demo-secret"
            });

        var order = new Trading212OrderRequest
        {
            Ticker = "AAPL_US_EQ",
            Quantity = 1m,
            Type = type
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => sut.PlaceOrderAsync(order));
        Assert.Equal("order", exception.ParamName);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request, cancellationToken));
        }
    }
}
