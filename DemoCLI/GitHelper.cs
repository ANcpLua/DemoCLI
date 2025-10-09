using System.Net.Http.Json;
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
    private readonly AzureDevOpsSettings _settings;

    public GitHelper(HttpClient client, AzureDevOpsSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<string> EnsureRepositoryExists()
    {
        var response = await _client.GetAsync("_apis/git/repositories?api-version=7.1");
        response.EnsureSuccessStatusCode();
        
        var repos = await response.Content.ReadFromJsonAsync<AzureDevOpsListResponse<Repository>>();
        
        if (repos!.Value.Length > 0)
        {
            var existingRepo = repos.Value.FirstOrDefault(r => r.Name.Equals(_settings.Repository.Name, StringComparison.OrdinalIgnoreCase)) 
                ?? repos.Value[0];
            Console.WriteLine($"Using existing repository: {existingRepo.Name}");
            return existingRepo.Name;
        }

        Console.WriteLine("No repositories found. Creating new repository...");
        var createResponse = await _client.PostAsJsonAsync("_apis/git/repositories?api-version=7.1", new { name = _settings.Repository.Name });
        
        if (!createResponse.IsSuccessStatusCode)
        {
            var error = await createResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to create repository. Status: {createResponse.StatusCode}. Error: {error}. Please create a repository manually in Azure DevOps.");
        }

        var repo = await createResponse.Content.ReadFromJsonAsync<Repository>();
        Console.WriteLine($"Created repository: {repo!.Name}");
        return repo.Name;
    }

    public async Task PushCodeViaApi(string repoName)
    {
        var files = GetProjectFiles();
        
        var pushBody = new
        {
            refUpdates = new[] { new { name = $"refs/heads/{_settings.Repository.MainBranch}", oldObjectId = "0000000000000000000000000000000000000000" } },
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
        var response = await _client.GetAsync($"_apis/git/repositories/{repoName}/refs?filter=heads/{_settings.Repository.MainBranch}&api-version=7.1");
        response.EnsureSuccessStatusCode();

        var refs = await response.Content.ReadFromJsonAsync<AzureDevOpsListResponse<GitRef>>();
        var mainBranch = refs!.Value[0];

        var branchBody = new[]
        {
            new
            {
                name = $"refs/heads/{_settings.Repository.FeatureBranch}",
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
        var solutionDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
        var gitignorePath = Path.Combine(solutionDir, ".gitignore");
        
        var excludePatterns = File.Exists(gitignorePath)
            ? File.ReadAllLines(gitignorePath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
                .Select(line => line.Trim().TrimEnd('/').Replace('/', Path.DirectorySeparatorChar))
                .ToHashSet()
            : new HashSet<string> { "bin", "obj" };

        var includePatterns = new[] { "*.cs", "*.csproj", "*.sln", "azure-pipelines.yml", "templates.json", "appsettings.json" };

        return Directory.EnumerateFiles(solutionDir, "*.*", SearchOption.AllDirectories)
            .Where(file =>
            {
                var fileName = Path.GetFileName(file);
                var relativePath = Path.GetRelativePath(solutionDir, file);
                
                var isIncluded = includePatterns.Any(pattern =>
                    pattern.Contains('*')
                        ? fileName.EndsWith(pattern.TrimStart('*'))
                        : fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase));
                
                var isExcluded = excludePatterns.Any(pattern =>
                    relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase));

                return isIncluded && !isExcluded;
            })
            .Select(file => (
                Path: Path.GetRelativePath(solutionDir, file).Replace(Path.DirectorySeparatorChar, '/'),
                Content: File.ReadAllText(file)
            ))
            .ToList();
    }
}
