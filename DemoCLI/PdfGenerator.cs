using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemoCLI;

public record RepoInfo([property: JsonPropertyName("webUrl")] string WebUrl);
public record Pipeline([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("url")] string Url);
public record PipelineRun([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("state")] string State, [property: JsonPropertyName("result")] string? Result);

public class PdfGenerator
{
    private readonly HttpClient _client;
    private readonly Config _config;

    public PdfGenerator(HttpClient client, Config config)
    {
        _client = client;
        _config = config;
    }

    public async Task GenerateReportData(string outputPath)
    {
        var report = new StringBuilder();
        report.AppendLine("MSE1 APM1 - Übung 3");
        report.AppendLine("===================");
        report.AppendLine();
        report.AppendLine($"Organization: {_config.Organization}");
        report.AppendLine($"Project: {_config.Project}");
        report.AppendLine();

        await AddRepositoryInfo(report);
        await AddPipelineInfo(report);
        await AddWorkItemsInfo(report);

        File.WriteAllText(outputPath, report.ToString());
        Console.WriteLine($"Report generated: {outputPath}");
    }

    private async Task AddRepositoryInfo(StringBuilder report)
    {
        report.AppendLine("Teil 1: Repository");
        report.AppendLine("------------------");
        
        var response = await _client.GetAsync($"_apis/git/repositories/{_config.Project}?api-version=7.1");
        response.EnsureSuccessStatusCode();
        
        var repo = await response.Content.ReadFromJsonAsync<RepoInfo>();
        report.AppendLine($"Repository URL: {repo!.WebUrl}");
        report.AppendLine();
    }

    private async Task AddPipelineInfo(StringBuilder report)
    {
        report.AppendLine("Teil 1: Build Pipeline");
        report.AppendLine("----------------------");
        
        var response = await _client.GetAsync("_apis/pipelines?api-version=7.1");
        response.EnsureSuccessStatusCode();
        
        var pipelines = await response.Content.ReadFromJsonAsync<AzureDevOpsListResponse<Pipeline>>();
        var demoPipeline = pipelines!.Value.FirstOrDefault(p => p.Name.Contains("DemoCLI"));
        
        if (demoPipeline != null)
        {
            report.AppendLine($"Pipeline: {demoPipeline.Name}");
            report.AppendLine($"Pipeline ID: {demoPipeline.Id}");
            report.AppendLine($"Pipeline URL: {demoPipeline.Url}");
            
            await AddPipelineRunInfo(report, demoPipeline.Id);
        }
        
        report.AppendLine();
    }

    private async Task AddPipelineRunInfo(StringBuilder report, int pipelineId)
    {
        var response = await _client.GetAsync($"_apis/pipelines/{pipelineId}/runs?api-version=7.1");
        response.EnsureSuccessStatusCode();
        
        var runs = await response.Content.ReadFromJsonAsync<AzureDevOpsListResponse<PipelineRun>>();
        
        if (runs!.Value.Length > 0)
        {
            var latestRun = runs.Value[0];
            report.AppendLine($"Latest Run ID: {latestRun.Id}");
            report.AppendLine($"State: {latestRun.State}");
            report.AppendLine($"Result: {latestRun.Result ?? "Running"}");
        }
    }

    private async Task AddWorkItemsInfo(StringBuilder report)
    {
        report.AppendLine("Teil 2 & 3: Work Items (User Stories)");
        report.AppendLine("-------------------------------------");
        
        var wiqlQuery = new
        {
            query = $"SELECT [System.Id], [System.Title], [System.State] FROM WorkItems WHERE [System.WorkItemType] = 'Issue' AND [System.TeamProject] = '{_config.Project}' ORDER BY [System.CreatedDate] DESC"
        };

        var wiqlContent = new StringContent(JsonSerializer.Serialize(wiqlQuery), Encoding.UTF8, "application/json");
        var wiqlResponse = await _client.PostAsync("_apis/wit/wiql?api-version=7.1", wiqlContent);

        if (wiqlResponse.IsSuccessStatusCode)
        {
            var wiqlJson = await wiqlResponse.Content.ReadAsStringAsync();
            var wiqlResult = JsonDocument.Parse(wiqlJson);
            
            if (wiqlResult.RootElement.TryGetProperty("workItems", out var workItems))
            {
                foreach (var wi in workItems.EnumerateArray())
                {
                    var id = wi.GetProperty("id").GetInt32();
                    var wiResponse = await _client.GetAsync($"_apis/wit/workitems/{id}?api-version=7.1");
                    
                    if (wiResponse.IsSuccessStatusCode)
                    {
                        var wiJson = await wiResponse.Content.ReadAsStringAsync();
                        var workItem = JsonDocument.Parse(wiJson);
                        var fields = workItem.RootElement.GetProperty("fields");
                        
                        var title = fields.GetProperty("System.Title").GetString();
                        var state = fields.GetProperty("System.State").GetString();
                        
                        report.AppendLine($"Work Item #{id}: {title}");
                        report.AppendLine($"  State: {state}");
                        
                        if (fields.TryGetProperty("System.Description", out var desc))
                        {
                            report.AppendLine($"  Description: {desc.GetString()}");
                        }
                        report.AppendLine();
                    }
                }
            }
        }
    }
}
