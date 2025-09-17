using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

try
{
    var config = JsonSerializer.Deserialize<Config>(File.ReadAllText("config.json"))!;
    var templates = JsonSerializer.Deserialize<List<UserStory>>(File.ReadAllText("templates.json"))!;
    var createdWorkItemIds = new List<int>();

    using var client = new HttpClient();
    client.BaseAddress = new Uri($"https://dev.azure.com/{config.Organization}/{config.Project}/");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
        Convert.ToBase64String(Encoding.ASCII.GetBytes($"x:{config.PersonalAccessToken}")));

    foreach (var story in templates)
    {
        var ops = new[]
        {
            new { op = "add", path = "/fields/System.Title", value = (object)story.Title },
            new { op = "add", path = "/fields/System.Description", value = (object)story.Description },
            new
            {
                op = "add", path = "/fields/Microsoft.VSTS.Common.AcceptanceCriteria",
                value = (object)story.AcceptanceCriteria
            },
            new
            {
                op = "add", path = "/fields/Microsoft.VSTS.Scheduling.StoryPoints", value = (object)story.StoryPoints
            },
            new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = (object)story.Priority },
            new { op = "add", path = "/fields/System.Tags", value = (object)story.Tags },
            new { op = "add", path = "/fields/System.State", value = (object)story.State }
        };

        var content = new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json");
        var response = await client.PostAsync($"_apis/wit/workitems/${config.WorkItemType}?api-version=7.1", content);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadAsStringAsync();
            var id = JsonDocument.Parse(result).RootElement.GetProperty("id").GetInt32();
            createdWorkItemIds.Add(id);
            Console.WriteLine($"✓ Created work item #{id}: {story.Title}");
        }
        else
        {
            Console.WriteLine($"✗ Failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        }
    }

    if (createdWorkItemIds.Count is not 0)
    {
        var prBody = new
        {
            sourceRefName = "refs/heads/feature",
            targetRefName = "refs/heads/main",
            title = "Add user stories from template",
            description = $"Automated PR with {createdWorkItemIds.Count} linked work items",
            workItemRefs = createdWorkItemIds.Select(id => new { id = id.ToString() }).ToArray()
        };

        var prContent = new StringContent(JsonSerializer.Serialize(prBody), Encoding.UTF8, "application/json");
        var prResponse = await client.PostAsync($"_apis/git/repositories/{config.Project}/pullrequests?api-version=7.1",
            prContent);

        if (prResponse.IsSuccessStatusCode)
        {
            var prResult = await prResponse.Content.ReadAsStringAsync();
            var prId = JsonDocument.Parse(prResult).RootElement.GetProperty("pullRequestId").GetInt32();
            Console.WriteLine($"✓ Created Pull Request #{prId} with {createdWorkItemIds.Count} linked work items");
        }
        else
        {
            Console.WriteLine($"✗ PR failed: {prResponse.StatusCode} - {await prResponse.Content.ReadAsStringAsync()}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Fatal: {ex.Message}");
}

public class Config
{
    public string Organization { get; set; } = "";
    public string Project { get; set; } = "";
    public string PersonalAccessToken { get; set; } = "";
    public string WorkItemType { get; set; } = "";
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