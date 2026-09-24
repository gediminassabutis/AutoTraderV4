using System;
using System.IO;
using System.Linq;

namespace AutoTraderV4.Tests;

public class AgentDefinitionTests
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
    public void Repository_ContainsMultipleSpecializedAgents()
    {
        var root = GetRepositoryRoot();
        var agentDirectory = Path.Combine(root, ".github", "agents");

        Assert.True(Directory.Exists(agentDirectory));

        var files = Directory.GetFiles(agentDirectory, "*.agent.md")
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(files.Count >= 5);
        Assert.Contains("algotrader.agent.md", files);
        Assert.Contains("market-data.agent.md", files);
        Assert.Contains("strategy-engine.agent.md", files);
        Assert.Contains("risk-governance.agent.md", files);
        Assert.Contains("dashboard-analytics.agent.md", files);
        Assert.Contains("qa-validation.agent.md", files);
    }

    [Fact]
    public void SpecializedAgents_DescribeTheirDomainAndRequirements()
    {
        var root = GetRepositoryRoot();

        var marketDataContent = File.ReadAllText(Path.Combine(root, ".github", "agents", "market-data.agent.md"));
        Assert.Contains("sentiment", marketDataContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stale", marketDataContent, StringComparison.OrdinalIgnoreCase);

        var strategyContent = File.ReadAllText(Path.Combine(root, ".github", "agents", "strategy-engine.agent.md"));
        Assert.Contains("weighted", strategyContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("75", strategyContent, StringComparison.OrdinalIgnoreCase);

        var riskContent = File.ReadAllText(Path.Combine(root, ".github", "agents", "risk-governance.agent.md"));
        Assert.Contains("95%", riskContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("defensive", riskContent, StringComparison.OrdinalIgnoreCase);

        var dashboardContent = File.ReadAllText(Path.Combine(root, ".github", "agents", "dashboard-analytics.agent.md"));
        Assert.Contains("recharts", dashboardContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("watchlist", dashboardContent, StringComparison.OrdinalIgnoreCase);

        var qaContent = File.ReadAllText(Path.Combine(root, ".github", "agents", "qa-validation.agent.md"));
        Assert.Contains("regression", qaContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audit", qaContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanAndReadme_AreLinkedAndDescribeTheSameImplementationPath()
    {
        var root = GetRepositoryRoot();

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains("plan.md", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AGENTS.md", readme, StringComparison.OrdinalIgnoreCase);

        var plan = File.ReadAllText(Path.Combine(root, "plan.md"));
        Assert.Contains("Implementation Plan", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AGENTS.md", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("risk management", plan, StringComparison.OrdinalIgnoreCase);
    }
}
