using System.CommandLine;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.Exceptions;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Command = System.CommandLine.Command;

namespace DemoCLI;

public sealed record AzureConfig(string Organization, string Project, string Pat)
{
    private static readonly string SecretPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azdo", "secret");

    public string OrgUrl => $"https://dev.azure.com/{Organization}";

    public static async Task<string> LoadPatAsync(CancellationToken cancellationToken = default)
    {
        return (await File.ReadAllTextAsync(SecretPath, cancellationToken)).Trim();
    }

    public static async Task SavePatAsync(string pat, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SecretPath)!);
        await File.WriteAllTextAsync(SecretPath, pat.Trim(), cancellationToken);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(SecretPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static Task DeletePatAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(SecretPath);
        return Task.CompletedTask;
    }
}

public static class Program
{
    private static IConfiguration? _configuration;

    public static async Task<int> Main(string[] args)
    {
        _configuration = BuildConfiguration();
        return await BuildRoot().Parse(args).InvokeAsync();
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static RootCommand BuildRoot()
    {
        var root = new RootCommand("Azure DevOps CLI");

        // Auth commands
        var authCommand = new Command("auth", "Manage authentication");

        var loginCommand = new Command("login", "Save PAT");
        var forceOption = new Option<bool>("--force") { Description = "Overwrite existing PAT" };
        loginCommand.Add(forceOption);
        loginCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            if (File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azdo", "secret"))
                && !parseResult.GetValue(forceOption))
                throw new InvalidOperationException("PAT exists. Use --force to overwrite");

            var prompt = new TextPrompt<string>("[cyan]PAT:[/]").Secret();
            var pat = await prompt.ShowAsync(AnsiConsole.Console, cancellationToken);

            await AzureConfig.SavePatAsync(pat, cancellationToken);
            AnsiConsole.MarkupLine("[green]✓ PAT saved[/]");
            return 0;
        });

        var logoutCommand = new Command("logout", "Remove PAT");
        logoutCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            await AzureConfig.DeletePatAsync(cancellationToken);
            AnsiConsole.MarkupLine("[green]✓ PAT removed[/]");
            return 0;
        });

        authCommand.Add(loginCommand);
        authCommand.Add(logoutCommand);
        root.Add(authCommand);

        // Repos
        var reposCommand = new Command("repos", "Manage repositories");
        var repoCreateCommand = new Command("create", "Create repository");
        var repoNameArg = new Argument<string>("name") { Description = "Repository name" };
        repoCreateCommand.Add(repoNameArg);
        repoCreateCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var config = await GetConfigAsync(cancellationToken);
            var name = parseResult.GetValue(repoNameArg)!;
            await ExecuteAzureCliAsync(config,
                $"repos create --name {name} --org {config.OrgUrl} --project {config.Project}", cancellationToken);
            AnsiConsole.MarkupLine("[green]✓ Repository created[/]");
            return 0;
        });
        reposCommand.Add(repoCreateCommand);
        root.Add(reposCommand);

        // Pipelines
        var pipelinesCommand = new Command("pipelines", "Manage pipelines");
        var pipelineCreateCommand = new Command("create", "Create pipeline");
        var pipelineNameArg = new Argument<string>("name") { Description = "Pipeline name" };
        var pipelineRepoArg = new Argument<string>("repo") { Description = "Repository" };
        var yamlPathOption = new Option<string>("--yaml-path")
        {
            Description = "YAML file path",
            DefaultValueFactory = _ => "azure-pipelines.yml"
        };
        pipelineCreateCommand.Add(pipelineNameArg);
        pipelineCreateCommand.Add(pipelineRepoArg);
        pipelineCreateCommand.Add(yamlPathOption);
        pipelineCreateCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var config = await GetConfigAsync(cancellationToken);
            var name = parseResult.GetValue(pipelineNameArg)!;
            var repo = parseResult.GetValue(pipelineRepoArg)!;
            var yamlPath = parseResult.GetValue(yamlPathOption)!;

            await ExecuteAzureCliAsync(config,
                $"pipelines create --name {name} --repository {repo} --branch main --yml-path {yamlPath} --org {config.OrgUrl} --project {config.Project}",
                cancellationToken);
            AnsiConsole.MarkupLine("[green]✓ Pipeline created[/]");
            return 0;
        });
        pipelinesCommand.Add(pipelineCreateCommand);
        root.Add(pipelinesCommand);

        // Work Items
        var workItemsCommand = new Command("work-items", "Manage work items");
        var workItemCreateCommand = new Command("create", "Create user story");
        var workItemTitleArg = new Argument<string>("title") { Description = "Title" };
        var descriptionOption = new Option<string?>("--description") { Description = "Description" };
        workItemCreateCommand.Add(workItemTitleArg);
        workItemCreateCommand.Add(descriptionOption);
        workItemCreateCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var config = await GetConfigAsync(cancellationToken);
            var title = parseResult.GetValue(workItemTitleArg)!;
            var description = parseResult.GetValue(descriptionOption);

            var command =
                $"boards work-item create --type \"User Story\" --title \"{title}\" --org {config.OrgUrl} --project {config.Project}";
            if (!string.IsNullOrEmpty(description))
                command += $" --description \"{description}\"";

            await ExecuteAzureCliAsync(config, command, cancellationToken);
            AnsiConsole.MarkupLine("[green]✓ Work item created[/]");
            return 0;
        });
        workItemsCommand.Add(workItemCreateCommand);
        root.Add(workItemsCommand);

        // Pull Requests
        var pullRequestsCommand = new Command("pull-requests", "Manage pull requests");
        var prCreateCommand = new Command("create", "Create PR");
        var prRepoArg = new Argument<string>("repo") { Description = "Repository" };
        var prSourceArg = new Argument<string>("source") { Description = "Source branch" };
        var prTargetArg = new Argument<string>("target") { Description = "Target branch" };
        var prTitleArg = new Argument<string>("title") { Description = "Title" };
        var prDescOption = new Option<string?>("--description") { Description = "Description" };
        prCreateCommand.Add(prRepoArg);
        prCreateCommand.Add(prSourceArg);
        prCreateCommand.Add(prTargetArg);
        prCreateCommand.Add(prTitleArg);
        prCreateCommand.Add(prDescOption);
        prCreateCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var config = await GetConfigAsync(cancellationToken);
            var repo = parseResult.GetValue(prRepoArg)!;
            var source = parseResult.GetValue(prSourceArg)!;
            var target = parseResult.GetValue(prTargetArg)!;
            var title = parseResult.GetValue(prTitleArg)!;
            var description = parseResult.GetValue(prDescOption);

            var command =
                $"repos pr create --repository {repo} --source-branch {source} --target-branch {target} --title \"{title}\" --org {config.OrgUrl} --project {config.Project}";
            if (!string.IsNullOrEmpty(description))
                command += $" --description \"{description}\"";

            await ExecuteAzureCliAsync(config, command, cancellationToken);
            AnsiConsole.MarkupLine("[green]✓ Pull request created[/]");
            return 0;
        });
        pullRequestsCommand.Add(prCreateCommand);
        root.Add(pullRequestsCommand);

        return root;
    }

    private static async Task<AzureConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var org = _configuration?["AZURE_DEVOPS_ORG"] ?? _configuration?["AzureDevOps:Organization"]
            ?? throw new InvalidOperationException("AZURE_DEVOPS_ORG not set");
        var project = _configuration?["AZURE_DEVOPS_PROJECT"] ?? _configuration?["AzureDevOps:Project"]
            ?? throw new InvalidOperationException("AZURE_DEVOPS_PROJECT not set");

        var pat = await AzureConfig.LoadPatAsync(cancellationToken);
        return new AzureConfig(org, project, pat);
    }

    private static async Task ExecuteAzureCliAsync(AzureConfig config, string arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Cli.Wrap("az")
                .WithArguments(arguments)
                .WithEnvironmentVariables(env => env.Set("AZURE_DEVOPS_EXT_PAT", config.Pat))
                .ExecuteBufferedAsync(cancellationToken);
        }
        catch (CommandExecutionException ex)
        {
            throw new InvalidOperationException($"Azure CLI failed: {ex.Message}", ex);
        }
    }
}
