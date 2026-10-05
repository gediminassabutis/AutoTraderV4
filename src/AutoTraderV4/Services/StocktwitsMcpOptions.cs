namespace AutoTraderV4.Services;

public sealed class StocktwitsMcpOptions
{
    private const string PinnedServerPackage =
        "github:stocktwits/stocktwits-mcp#3c3f6de9192eb910612f5e3d701872adc8ee524b";

    public const string SectionName = "StocktwitsMcp";

    public bool Enabled { get; set; }

    public string Command { get; set; } = "npx";

    public string[] Arguments { get; set; } = [];

    public string? NpmScriptShell { get; set; } = FindGitBash();

    public int RefreshIntervalMinutes { get; set; } = 15;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(RefreshIntervalMinutes);

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);

    public IReadOnlyList<string> GetEffectiveArguments()
    {
        return Arguments.Length > 0
            ? Arguments
            : ["-y", $"--package={PinnedServerPackage}", "--", "stocktwits-mcp"];
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Command))
        {
            throw new ArgumentException("The Stocktwits MCP command is required.", nameof(Command));
        }

        if (Arguments is null || Arguments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Stocktwits MCP command arguments cannot be null or empty.", nameof(Arguments));
        }

        if (!string.IsNullOrWhiteSpace(NpmScriptShell) && !File.Exists(NpmScriptShell))
        {
            throw new FileNotFoundException("The configured npm script shell was not found.", NpmScriptShell);
        }

        if (RefreshIntervalMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RefreshIntervalMinutes), "The refresh interval must be positive.");
        }

        if (RequestTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeoutSeconds), "The request timeout must be positive.");
        }
    }

    private static string? FindGitBash()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}

public sealed record StocktwitsMcpSettings(bool Enabled);
