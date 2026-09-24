using System.IO;

namespace AutoTraderV4.Tests;

public class DashboardSpecificationTests
{
    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing AGENTS.md.");
    }

    [Fact]
    public void DashboardRequirements_AreFullySpecifiedInAgentsDocument()
    {
        var agents = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "AGENTS.md"));

        Assert.Contains("Portfolio Summary", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open Positions Table", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Watchlist", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Market Overview", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AI Insights Panel", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alerts Panel", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recharts", agents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardAnalyticsAgent_CoversAllRequiredViewsAndCharts()
    {
        var dashboardAgent = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "agents", "dashboard-analytics.agent.md"));

        Assert.Contains("portfolio summary", dashboardAgent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("open positions", dashboardAgent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("watchlist", dashboardAgent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("market overview", dashboardAgent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AI insight", dashboardAgent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alerts", dashboardAgent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drawdown", dashboardAgent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forecast projections", dashboardAgent, StringComparison.OrdinalIgnoreCase);
    }
}
