using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemoCLI;

public record AzureDevOpsListResponse<T>([property: JsonPropertyName("value")] T[] Value);
public record Repository([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("id")] string Id);
public record GitRef([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("objectId")] string ObjectId);

public class GitHelper
{
    private readonly HttpClient _client;
    private readonly Config _config;

    public GitHelper(HttpClient client, Config config)
    {
        _client = client;
        _config = config;
    }

    public async Task<string> EnsureRepositoryExists()
    {
        var response = await _client.GetAsync("_apis/git/repositories?api-version=7.1");
        response.EnsureSuccessStatusCode();
        
        var repos = await response.Content.ReadFromJsonAsync<AzureDevOpsListResponse<Repository>>();
        
        if (repos!.Value.Length > 0)
        {
            Console.WriteLine($"Using existing repository: {repos.Value[0].Name}");
            return repos.Value[0].Name;
        }

        Console.WriteLine("Creating new repository...");
        var createResponse = await _client.PostAsJsonAsync("_apis/git/repositories?api-version=7.1", new { name = "DemoCLI" });
        createResponse.EnsureSuccessStatusCode();

        var repo = await createResponse.Content.ReadFromJsonAsync<Repository>();
        Console.WriteLine($"Created repository: {repo!.Name}");
        return repo.Name;
    }

    public async Task PushCodeViaApi(string repoName)
    {
        var files = GetProjectFiles();
        
        var pushBody = new
        {
            refUpdates = new[] { new { name = "refs/heads/main", oldObjectId = "0000000000000000000000000000000000000000" } },
            commits = new[]
            {
                new
                {
                    comment = "Initial commit with DemoCLI project",
                    changes = files.Select(f => new
                    {
                        changeType = "add",
                        item = new { path = f.Path },
                        newContent = new { content = f.Content, contentType = "rawtext" }
                    }).ToArray()
                }
            }
        };

        var response = await _client.PostAsJsonAsync($"_apis/git/repositories/{repoName}/pushes?api-version=7.1", pushBody);
        response.EnsureSuccessStatusCode();
        
        Console.WriteLine("Code pushed to Azure DevOps via API");
    }

    public async Task CreateFeatureBranchViaApi(string repoName)
    {
        var response = await _client.GetAsync($"_apis/git/repositories/{repoName}/refs?filter=heads/main&api-version=7.1");
        response.EnsureSuccessStatusCode();

        var refs = await response.Content.ReadFromJsonAsync<AzureDevOpsListResponse<GitRef>>();
        var mainBranch = refs!.Value[0];

        var branchBody = new[]
        {
            new
            {
                name = "refs/heads/feature",
                oldObjectId = "0000000000000000000000000000000000000000",
                newObjectId = mainBranch.ObjectId
            }
        };

        var branchResponse = await _client.PostAsJsonAsync($"_apis/git/repositories/{repoName}/refs?api-version=7.1", branchBody);
        branchResponse.EnsureSuccessStatusCode();
        
        Console.WriteLine("Feature branch created via API");
    }

    private List<(string Path, string Content)> GetProjectFiles()
    {
        var files = new List<(string Path, string Content)>();
        var projectDir = Path.GetFullPath(".");
        
        var filesToInclude = new[]
        {
            "Program.cs",
            "DemoCLI.csproj",
            "azure-pipelines.yml",
            "config.example.json",
            "templates.json",
            "../DemoCLI.Tests/UnitTest1.cs",
            "../DemoCLI.Tests/DemoCLI.Tests.csproj",
            "../DemoCLI.sln"
        };

        foreach (var file in filesToInclude)
        {
            var fullPath = Path.Combine(projectDir, file);
            if (File.Exists(fullPath))
            {
                var content = File.ReadAllText(fullPath);
                var relativePath = file.Replace("../", "/");
                files.Add((relativePath, content));
            }
        }

        return files;
    }
}
