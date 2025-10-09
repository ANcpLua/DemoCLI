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
        Console.WriteLine($"Calling: {_client.BaseAddress}_apis/git/repositories?api-version=7.1");
        Console.WriteLine($"Auth header: {_client.DefaultRequestHeaders.Authorization}");
        var response = await _client.GetAsync("_apis/git/repositories?api-version=7.1");
        Console.WriteLine($"Response status: {response.StatusCode}");
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

    public async Task UpdateFeatureBranch(string repoName)
    {
        var mainRefResponse = await _client.GetAsync($"_apis/git/repositories/{repoName}/refs?filter=heads/{_settings.Repository.MainBranch}&api-version=7.1");
        mainRefResponse.EnsureSuccessStatusCode();
        var mainRefs = await mainRefResponse.Content.ReadFromJsonAsync<AzureDevOpsListResponse<GitRef>>();
        var mainCommit = mainRefs!.Value[0].ObjectId;

        var featureRefResponse = await _client.GetAsync($"_apis/git/repositories/{repoName}/refs?filter=heads/{_settings.Repository.FeatureBranch}&api-version=7.1");
        
        if (featureRefResponse.IsSuccessStatusCode)
        {
            var featureRefs = await featureRefResponse.Content.ReadFromJsonAsync<AzureDevOpsListResponse<GitRef>>();
            if (featureRefs!.Value.Length > 0)
            {
                var oldCommit = featureRefs.Value[0].ObjectId;
                var updateBody = new[]
                {
                    new
                    {
                        name = $"refs/heads/{_settings.Repository.FeatureBranch}",
                        oldObjectId = oldCommit,
                        newObjectId = mainCommit
                    }
                };
                
                var updateContent = new StringContent(JsonSerializer.Serialize(updateBody), Encoding.UTF8, "application/json");
                var updateResponse = await _client.PostAsync($"_apis/git/repositories/{repoName}/refs?api-version=7.1", updateContent);
                updateResponse.EnsureSuccessStatusCode();
                Console.WriteLine($"Updated feature branch to match {_settings.Repository.MainBranch}");
                return;
            }
        }

        var branchBody = new[]
        {
            new
            {
                name = $"refs/heads/{_settings.Repository.FeatureBranch}",
                oldObjectId = "0000000000000000000000000000000000000000",
                newObjectId = mainCommit
            }
        };

        var branchContent = new StringContent(JsonSerializer.Serialize(branchBody), Encoding.UTF8, "application/json");
        var branchResponse = await _client.PostAsync($"_apis/git/repositories/{repoName}/refs?api-version=7.1", branchContent);
        branchResponse.EnsureSuccessStatusCode();
        Console.WriteLine($"Created feature branch from {_settings.Repository.MainBranch}");
    }

    public async Task PushYamlFile(string repoName, string branchName)
    {
        var refResponse = await _client.GetAsync($"_apis/git/repositories/{repoName}/refs?filter=heads/{branchName}&api-version=7.1");
        refResponse.EnsureSuccessStatusCode();
        var refs = await refResponse.Content.ReadFromJsonAsync<AzureDevOpsListResponse<GitRef>>();
        var currentCommit = refs!.Value[0].ObjectId;

        var yamlPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../azure-pipelines.yml"));
        if (!File.Exists(yamlPath))
        {
            Console.WriteLine($"YAML file not found at: {yamlPath}");
            return;
        }
        
        var yamlContent = File.ReadAllText(yamlPath);

        var itemResponse = await _client.GetAsync($"_apis/git/repositories/{repoName}/items?path=/azure-pipelines.yml&api-version=7.1");
        var changeType = itemResponse.IsSuccessStatusCode ? "edit" : "add";

        var pushBody = new
        {
            refUpdates = new[] { new { name = $"refs/heads/{branchName}", oldObjectId = currentCommit } },
            commits = new[]
            {
                new
                {
                    comment = changeType == "add" ? "Add azure-pipelines.yml" : "Update azure-pipelines.yml",
                    changes = new[]
                    {
                        new
                        {
                            changeType = changeType,
                            item = new { path = "/azure-pipelines.yml" },
                            newContent = new { content = yamlContent, contentType = "rawtext" }
                        }
                    }
                }
            }
        };

        var pushContent = new StringContent(JsonSerializer.Serialize(pushBody), Encoding.UTF8, "application/json");
        var pushResponse = await _client.PostAsync($"_apis/git/repositories/{repoName}/pushes?api-version=7.1", pushContent);
        
        if (pushResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Pushed azure-pipelines.yml to {branchName} branch");
        }
        else
        {
            var error = await pushResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Failed to push YAML: {pushResponse.StatusCode}");
            Console.WriteLine($"Error: {error}");
        }
    }

    public async Task PushProjectFiles(string repoName, string branchName)
    {
        var refResponse = await _client.GetAsync($"_apis/git/repositories/{repoName}/refs?filter=heads/{branchName}&api-version=7.1");
        refResponse.EnsureSuccessStatusCode();
        var refs = await refResponse.Content.ReadFromJsonAsync<AzureDevOpsListResponse<GitRef>>();
        var currentCommit = refs!.Value[0].ObjectId;

        var projectRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
        var files = GetProjectFiles(projectRoot);

        if (files.Count == 0)
        {
            Console.WriteLine("No project files found to push");
            return;
        }

        var changes = new List<object>();
        foreach (var (path, content) in files)
        {
            var itemResponse = await _client.GetAsync($"_apis/git/repositories/{repoName}/items?path=/{path}&api-version=7.1");
            var changeType = itemResponse.IsSuccessStatusCode ? "edit" : "add";

            changes.Add(new
            {
                changeType = changeType,
                item = new { path = $"/{path}" },
                newContent = new { content = content, contentType = "rawtext" }
            });
        }

        var pushBody = new
        {
            refUpdates = new[] { new { name = $"refs/heads/{branchName}", oldObjectId = currentCommit } },
            commits = new[]
            {
                new
                {
                    comment = "Update project files and pipeline configuration",
                    changes = changes.ToArray()
                }
            }
        };

        var pushContent = new StringContent(JsonSerializer.Serialize(pushBody), Encoding.UTF8, "application/json");
        var pushResponse = await _client.PostAsync($"_apis/git/repositories/{repoName}/pushes?api-version=7.1", pushContent);
        
        if (pushResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Pushed {files.Count} files to {branchName} branch");
        }
        else
        {
            var error = await pushResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Failed to push files: {pushResponse.StatusCode}");
            Console.WriteLine($"Error: {error}");
        }
    }

    private List<(string Path, string Content)> GetProjectFiles(string projectRoot)
    {
        var files = new List<(string, string)>();
        var includePatterns = new[] { "*.cs", "*.csproj", "*.sln", "azure-pipelines.yml", "*.json", ".gitignore" };
        var excludePatterns = new HashSet<string> { "bin", "obj", ".git", ".vs", "appsettings.json" };

        foreach (var file in Directory.EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            var relativePath = Path.GetRelativePath(projectRoot, file);
            
            var isIncluded = includePatterns.Any(pattern =>
                pattern.Contains('*')
                    ? fileName.EndsWith(pattern.TrimStart('*'))
                    : fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase));
            
            var isExcluded = excludePatterns.Any(pattern =>
                relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase));

            if (isIncluded && !isExcluded)
            {
                files.Add((relativePath.Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(file)));
            }
        }

        return files;
    }
}
