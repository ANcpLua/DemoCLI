namespace DemoCLI;

public class AzureDevOpsSettings
{
    public const string SectionName = "AzureDevOps";
    
    public string Organization { get; init; } = null!;
    public string Project { get; init; } = null!;
    public string PersonalAccessToken { get; init; } = null!;
    public RepositorySettings Repository { get; init; } = null!;
    public PipelineSettings Pipeline { get; init; } = null!;
    public WorkItemSettings WorkItems { get; init; } = null!;
    public PullRequestSettings PullRequest { get; init; } = null!;
}

public class RepositorySettings
{
    public string Name { get; init; } = null!;
    public string MainBranch { get; init; } = "main";
    public string FeatureBranch { get; init; } = "feature";
}

public class PipelineSettings
{
    public string Name { get; init; } = null!;
    public string? Folder { get; init; }
    public string YamlPath { get; init; } = "azure-pipelines.yml";
}

public class WorkItemSettings
{
    public string Type { get; init; } = "User Story";
    public string TemplatesPath { get; init; } = "templates.json";
}

public class PullRequestSettings
{
    public string Title { get; init; } = null!;
    public string DescriptionTemplate { get; init; } = "Automated PR with {0} linked work items";
}
