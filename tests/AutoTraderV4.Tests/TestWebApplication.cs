using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AutoTraderV4.Tests;

public sealed class TestWebApplication : IAsyncDisposable
{
    private TestWebApplication(WebApplication app)
    {
        App = app;
        Client = app.GetTestClient();
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public static async Task<TestWebApplication> CreateAsync(
        Action<ApplicationDbContext>? seed = null,
        ITrading212Client? tradingClient = null)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=autotrader_test;Username=postgres;Password=postgres",
            ["Trading212:BaseUrl"] = "https://demo.trading212.com",
            ["Trading212:ApiKey"] = "demo-key",
            ["Trading212:ApiSecret"] = "demo-secret"
        });

        Program.ConfigureServices(builder, includeDatabase: false);

        builder.Services.RemoveAll<ITrading212Client>();
        builder.Services.AddSingleton(tradingClient ?? new FakeTrading212Client());
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));

        var app = builder.Build();
        Program.ConfigureApp(app);

        await app.StartAsync();

        if (seed is not null)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seed(context);
            await context.SaveChangesAsync();
        }

        return new TestWebApplication(app);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }

    private sealed class FakeTrading212Client : ITrading212Client
    {
        public Task<Trading212AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Trading212AccountSummary
            {
                Id = 42,
                Currency = "USD",
                Cash = 12500.50m,
                Equity = 48750.25m
            });
        }

        public Task<Trading212OrderResult> PlaceOrderAsync(Trading212OrderRequest order, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Trading212OrderResult
            {
                Id = 99,
                Ticker = order.Ticker,
                Status = "accepted"
            });
        }
    }
}
