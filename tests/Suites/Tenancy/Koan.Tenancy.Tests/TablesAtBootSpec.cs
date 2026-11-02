using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Lexical;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>ARCH-0142: a tenant is a column of one table, so under a closed posture a tenant-scoped entity's table and its index exist at start, with no tenant in scope, and its
/// rows stay fail-closed.</summary>
public sealed class TablesAtBootSpec
{
    public sealed class TenantBootNote : Entity<TenantBootNote>
    {
        [Lexical] public string Title { get; set; } = "";
    }

    [Fact]
    public async Task A_tenant_scoped_entity_has_its_table_at_start_and_needs_a_tenant_for_its_rows()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = "Closed" });
        var file = runtime.DatabasePath;

        Objects(file, "%TenantBootNote").Should().BeGreaterThan(0);
        Objects(file, "%TenantBootNote__koan_lexical").Should().BeGreaterThan(0);

        Func<Task> unscoped = () => new TenantBootNote { Title = "whose" }.Save();
        await unscoped.Should().ThrowAsync<Koan.Core.Semantics.Segmentation.SegmentationRequiredException>("a table with no tenant is not a row with no tenant");
    }

    private static long Objects(string file, string like)
    {
        using var connection = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE name LIKE @like";
        command.Parameters.AddWithValue("@like", like);
        return (long)command.ExecuteScalar()!;
    }
}
