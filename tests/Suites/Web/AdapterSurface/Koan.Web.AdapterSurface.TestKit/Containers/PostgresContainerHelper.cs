using Npgsql;
using Koan.Testing.Containers;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class PostgresContainerHelper : KoanWebContainerHelper<PostgresFixture>
{
    public async Task ResetAsync()
    {
        if (ConnectionString is null) return;
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "DO $$ DECLARE r RECORD; BEGIN FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public') LOOP EXECUTE 'DROP TABLE IF EXISTS public.' || quote_ident(r.tablename) || ' CASCADE'; END LOOP; END $$;",
            conn);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
