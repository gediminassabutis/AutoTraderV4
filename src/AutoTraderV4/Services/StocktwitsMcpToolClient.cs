using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AutoTraderV4.Services;

public interface IStocktwitsMcpToolClient
{
    Task<JsonElement> CallAsync(string toolName, string symbol, CancellationToken cancellationToken = default);
}

public sealed class StocktwitsMcpToolClient : IStocktwitsMcpToolClient, IAsyncDisposable
{
    private readonly StocktwitsMcpOptions _options;
    private readonly ILogger<StocktwitsMcpToolClient> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private McpClient? _client;
    private Dictionary<string, McpClientTool>? _tools;

    public StocktwitsMcpToolClient(
        StocktwitsMcpOptions options,
        ILogger<StocktwitsMcpToolClient> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<JsonElement> CallAsync(
        string toolName,
        string symbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        try
        {
            var tool = await GetToolAsync(toolName, cancellationToken);
            var result = await tool.CallAsync(
                new Dictionary<string, object?> { ["symbol"] = symbol },
                cancellationToken: cancellationToken);

            var textContent = result.Content
                .OfType<TextContentBlock>()
                .Select(content => content.Text)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

            if (result.IsError == true)
            {
                throw new InvalidOperationException(
                    $"Stocktwits MCP tool '{toolName}' failed: {textContent ?? "The server returned an unspecified error."}");
            }

            if (textContent is null)
            {
                throw new InvalidDataException($"Stocktwits MCP tool '{toolName}' returned no text content.");
            }

            try
            {
                using var document = JsonDocument.Parse(textContent);
                return document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Stocktwits MCP tool '{toolName}' returned invalid JSON.",
                    exception);
            }
        }
        catch (ClientTransportClosedException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var details = exception.Details as StdioClientCompletionDetails;
            var standardError = details?.StandardErrorTail is { Count: > 0 } lines
                ? string.Join(" | ", lines)
                : "no standard error output";
            var processDetails = details is null
                ? exception.Message
                : $"exit code {details.ExitCode?.ToString() ?? "unknown"}; stderr: {standardError}";
            throw new InvalidOperationException(
                $"Stocktwits MCP server exited unexpectedly while calling '{toolName}' ({processDetails}).",
                exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
                _tools = null;
            }
        }
        finally
        {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }
    }

    private async Task<McpClientTool> GetToolAsync(string toolName, CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_client is null || _client.Completion.IsCompleted)
            {
                if (_client is not null)
                {
                    await _client.DisposeAsync();
                }

                var environmentVariables = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
                environmentVariables["NPM_CONFIG_LOGLEVEL"] = "error";
                if (!string.IsNullOrWhiteSpace(_options.NpmScriptShell))
                {
                    environmentVariables["npm_config_script_shell"] = _options.NpmScriptShell;
                }

                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "stocktwits",
                    Command = _options.Command,
                    Arguments = _options.GetEffectiveArguments().ToArray(),
                    ShutdownTimeout = TimeSpan.FromSeconds(10),
                    InheritEnvironmentVariables = false,
                    EnvironmentVariables = environmentVariables,
                    StandardErrorLines = line => _logger.LogDebug("Stocktwits MCP: {Message}", line)
                });
                var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
                try
                {
                    var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                    _tools = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
                    _client = client;
                }
                catch
                {
                    await client.DisposeAsync();
                    throw;
                }
            }

            if (_tools is null || !_tools.TryGetValue(toolName, out var tool))
            {
                throw new InvalidOperationException(
                    $"The Stocktwits MCP server does not expose the required '{toolName}' tool.");
            }

            return tool;
        }
        finally
        {
            _connectionLock.Release();
        }
    }
}
