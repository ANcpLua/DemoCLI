using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DemoCLI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection(AzureDevOpsSettings.SectionName).Get<AzureDevOpsSettings>()!;
var templates = JsonSerializer.Deserialize<List<UserStory>>(File.ReadAllText(settings.WorkItems.TemplatesPath))!;
var createdWorkItemIds = new List<int>();

using var client = new HttpClient();
client.BaseAddress = new Uri($"https://dev.azure.com/{settings.Organization}/{settings.Project}/");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
    Convert.ToBase64String(Encoding.ASCII.GetBytes($"x:{settings.PersonalAccessToken}")));

Console.WriteLine("Setting up Git Repository...");
var gitHelper = new DemoCLI.GitHelper(client, settings);
string repoName = await gitHelper.EnsureRepositoryExists();

Console.WriteLine("Pushing code to repository...");
await gitHelper.PushCodeViaApi(repoName);

Console.WriteLine("Creating feature branch...");
await gitHelper.CreateFeatureBranchViaApi(repoName);

Console.WriteLine("Setting up pipeline...");
await CreateOrUpdatePipeline(client, settings, repoName);

Console.WriteLine("Creating Work Items...");

foreach (var story in templates)
{
    var ops = new[]
    {
        new { op = "add", path = "/fields/System.Title", value = (object)story.Title },
        new { op = "add", path = "/fields/System.Description", value = (object)story.Description },
        new { op = "add", path = "/fields/Microsoft.VSTS.Common.AcceptanceCriteria", value = (object)story.AcceptanceCriteria },
        new { op = "add", path = "/fields/Microsoft.VSTS.Scheduling.StoryPoints", value = (object)story.StoryPoints },
        new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = (object)story.Priority },
        new { op = "add", path = "/fields/System.State", value = (object)story.State }
    };

    var content = new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json");
    var response = await client.PostAsync($"_apis/wit/workitems/${settings.WorkItems.Type}?api-version=7.1", content);
    response.EnsureSuccessStatusCode();

    var result = await response.Content.ReadAsStringAsync();
    var id = JsonDocument.Parse(result).RootElement.GetProperty("id").GetInt32();
    createdWorkItemIds.Add(id);
    Console.WriteLine($"Created work item #{id}: {story.Title}");
}

Console.WriteLine("Creating Pull Request...");

var prBody = new
{
    sourceRefName = $"refs/heads/{settings.Repository.FeatureBranch}",
    targetRefName = $"refs/heads/{settings.Repository.MainBranch}",
    title = settings.PullRequest.Title,
    description = string.Format(settings.PullRequest.DescriptionTemplate, createdWorkItemIds.Count),
    workItemRefs = createdWorkItemIds.Select(id => new { id = id.ToString() }).ToArray()
};

var prContent = new StringContent(JsonSerializer.Serialize(prBody), Encoding.UTF8, "application/json");
var prResponse = await client.PostAsync($"_apis/git/repositories/{repoName}/pullrequests?api-version=7.1", prContent);
prResponse.EnsureSuccessStatusCode();

var prResult = await prResponse.Content.ReadAsStringAsync();
var prId = JsonDocument.Parse(prResult).RootElement.GetProperty("pullRequestId").GetInt32();
Console.WriteLine($"Created Pull Request #{prId} with {createdWorkItemIds.Count} linked work items");

static async Task CreateOrUpdatePipeline(HttpClient client, AzureDevOpsSettings settings, string repoName)
{
    var repoResponse = await client.GetAsync($"_apis/git/repositories/{repoName}?api-version=7.1");
    repoResponse.EnsureSuccessStatusCode();
    
    var repoJson = await repoResponse.Content.ReadAsStringAsync();
    var repoId = JsonDocument.Parse(repoJson).RootElement.GetProperty("id").GetString();

    var pipelinesResponse = await client.GetAsync("_apis/pipelines?api-version=7.1");
    if (pipelinesResponse.IsSuccessStatusCode)
    {
        var pipelinesJson = await pipelinesResponse.Content.ReadAsStringAsync();
        var pipelines = JsonDocument.Parse(pipelinesJson);
        
        foreach (var pipeline in pipelines.RootElement.GetProperty("value").EnumerateArray())
        {
            var pipelineName = pipeline.GetProperty("name").GetString();
            if (pipelineName?.Contains("DemoCLI") == true)
            {
                Console.WriteLine($"Pipeline already exists: {pipelineName}");
                return;
            }
        }
    }

    var pipelineJson = JsonSerializer.Serialize(new
    {
        name = settings.Pipeline.Name,
        folder = settings.Pipeline.Folder ?? "\\",
        configuration = new
        {
            type = "yaml",
            path = $"/{settings.Pipeline.YamlPath}",
            repository = new { id = repoId, type = "azureReposGit" }
        }
    });

    var pipelineContent = new StringContent(pipelineJson, Encoding.UTF8, "application/json");
    var createResponse = await client.PostAsync("_apis/pipelines?api-version=7.1", pipelineContent);
    createResponse.EnsureSuccessStatusCode();

    var result = await createResponse.Content.ReadAsStringAsync();
    var pipelineId = JsonDocument.Parse(result).RootElement.GetProperty("id").GetInt32();
    Console.WriteLine($"Created pipeline #{pipelineId}: {settings.Pipeline.Name}");
}

public class UserStory
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    public int StoryPoints { get; set; }
    public int Priority { get; set; }
    public string Tags { get; set; } = "";
    public string State { get; set; } = "";
}