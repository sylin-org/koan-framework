using FluentAssertions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Relational.Schema;
using Xunit;

namespace Sora.Data.Relational.Tests;

public class ModelBuilderTests
{
    public class Todo : Sora.Data.Abstractions.IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        [Index]
        public string Title { get; set; } = string.Empty;
        public MetaData Meta { get; set; } = new();
    }

    public class MetaData { public int Priority { get; set; } }

    [Fact]
    public void Builds_Table_Columns_Indexes()
    {
        var model = RelationalModelBuilder.FromEntity(typeof(Todo));
        var table = model.Table;
        table.Name.Should().Be(nameof(Todo));
        table.Columns.Select(c => c.Name).Should().Contain(new[] { nameof(Todo.Title), nameof(Todo.Meta) });
        table.PrimaryKey.Name.Should().Be(nameof(Todo.Id));
        table.PrimaryKey.IsJson.Should().BeFalse();
        table.Columns.Single(c => c.Name == nameof(Todo.Meta)).IsJson.Should().BeTrue();
        table.Indexes.Should().ContainSingle(i => !i.IsPrimaryKey && i.Columns.Single().Name == nameof(Todo.Title));
    }

    [StorageNaming(StorageNamingStyle.FullNamespace)]
    public class Named : Sora.Data.Abstractions.IEntity<string>
    {
        [Identifier] public string Id { get; set; } = default!;
    }

    [Fact]
    public void Uses_FullNamespace_When_Requested()
    {
        var model = RelationalModelBuilder.FromEntity(typeof(Named));
        model.Table.Name.Should().Be(typeof(Named).FullName!.Replace('.', '_'));
    }
}
