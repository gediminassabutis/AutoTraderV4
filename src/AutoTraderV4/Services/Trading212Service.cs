namespace AutoTraderV4.Services;

public sealed class Trading212Service
{
    private readonly ITrading212Client _client;

    public Trading212Service(ITrading212Client client)
    {
        _client = client;
    }

    public Task<Trading212AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken = default)
    {
        return _client.GetAccountSummaryAsync(cancellationToken);
    }

    public Task<Trading212OrderResult> PlaceOrderAsync(Trading212OrderRequest order, CancellationToken cancellationToken = default)
    {
        return _client.PlaceOrderAsync(order, cancellationToken);
    }
}
