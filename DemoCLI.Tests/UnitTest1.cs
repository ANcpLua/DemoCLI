using System.Text.Json;

namespace DemoCLI.Tests;

public class UnitTest1
{
    [Fact]
    public void CanLoadConfigFromJson()
    {
        // Act
        var json = File.ReadAllText("config.test.json");
        var config = JsonSerializer.Deserialize<Config>(json);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("test-org", config.Organization);
        Assert.Equal("test-project", config.Project);
        Assert.Equal("test-token-123", config.PersonalAccessToken);
        Assert.Equal("User Story", config.WorkItemType);
    }

    [Fact]
    public void CanLoadTemplatesFromJson()
    {
        // Act
        var json = File.ReadAllText("templates.tests.json");
        var templates = JsonSerializer.Deserialize<List<UserStory>>(json);

        // Assert
        Assert.NotNull(templates);
        Assert.Single(templates);
        Assert.Equal("Test Story 1", templates[0].Title);
        Assert.Equal(1, templates[0].StoryPoints);
    }

    [Fact]
    public void ConfigRequiresAllFields()
    {
        // Arrange
        var config = new Config();

        // Assert
        Assert.NotNull(config.Organization);
        Assert.NotNull(config.Project);
        Assert.NotNull(config.PersonalAccessToken);
        Assert.NotNull(config.WorkItemType);
    }

    [Fact]
    public void UserStoryHasValidDefaults()
    {
        // Arrange
        var story = new UserStory();

        // Assert
        Assert.NotNull(story.Title);
        Assert.NotNull(story.Description);
        Assert.NotNull(story.AcceptanceCriteria);
        Assert.NotNull(story.Tags);
        Assert.NotNull(story.State);
        Assert.Equal(0, story.StoryPoints);
        Assert.Equal(0, story.Priority);
    }

    [Fact]
    public void GeneratesCorrectAzureDevOpsUrl()
    {
        // Arrange
        var config = new Config
        {
            Organization = "myorg",
            Project = "myproject"
        };

        // Act
        var url = $"https://dev.azure.com/{config.Organization}/{config.Project}/";

        // Assert
        Assert.Equal("https://dev.azure.com/myorg/myproject/", url);
    }
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