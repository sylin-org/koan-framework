using Koan.Jobs.Core.Tests.Support;
using Koan.Jobs.Recipes;

namespace Koan.Jobs.Core.Tests.Specs.Recipes;

public class JobRecipeSpec
{
    [Fact(DisplayName = "Recipe: builder produces immutable recipe")]
    public void Recipe_builder_produces_immutable_recipe()
    {
        // Arrange & Act
        var recipe = Jobs.Recipe()
            .Persist("my-source", "partition-a")
            .Audit()
            .Build<StubJob, string, string>();

        // Assert
        recipe.Source.Should().Be("my-source");
        recipe.Partition.Should().Be("partition-a");
        recipe.Persist.Should().BeTrue();
        recipe.Audit.Should().BeTrue();
        recipe.Defaults.Should().BeEmpty();
    }

    [Fact(DisplayName = "Recipe: .Persist captures source and partition")]
    public void Recipe_with_persist_captures_source_and_partition()
    {
        // Act
        var recipe = Jobs.Recipe()
            .Persist("src", "part")
            .Build<StubJob, string, string>();

        // Assert
        recipe.Persist.Should().BeTrue();
        recipe.Source.Should().Be("src");
        recipe.Partition.Should().Be("part");
    }

    [Fact(DisplayName = "Recipe: .WithDefaults captures configuration action")]
    public void Recipe_with_defaults_applies_to_job()
    {
        // Arrange
        var recipe = Jobs.Recipe()
            .WithDefaults<StubJob>(j => j.Name = "configured-name")
            .Build<StubJob, string, string>();

        // Assert
        recipe.Defaults.Should().HaveCount(1);

        // Verify the captured action applies correctly
        var job = new StubJob();
        recipe.Defaults[0](job);
        job.Name.Should().Be("configured-name");
    }

    [Fact(DisplayName = "Recipe: without .Persist does not enable persistence")]
    public void Recipe_without_persist_does_not_enable_persistence()
    {
        // Act
        var recipe = Jobs.Recipe()
            .Build<StubJob, string, string>();

        // Assert
        recipe.Persist.Should().BeFalse();
        recipe.Source.Should().BeNull();
        recipe.Partition.Should().BeNull();
    }

    [Fact(DisplayName = "Recipe: Jobs.Recipe() returns new builder")]
    public void Jobs_Recipe_returns_new_builder()
    {
        // Act
        var builder1 = Jobs.Recipe();
        var builder2 = Jobs.Recipe();

        // Assert — each call returns a fresh builder instance
        builder1.Should().NotBeSameAs(builder2);
        builder1.Should().BeOfType<JobRecipeBuilder>();
    }
}
