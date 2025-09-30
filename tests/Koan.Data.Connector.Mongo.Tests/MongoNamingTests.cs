using FluentAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Naming;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests;

public class MongoNamingTests
{
    private sealed class Outer
    {
        public sealed class Inner { }
    }

    [StorageName("ExplicitName")] private sealed class WithStorageName { }

    [Storage(Namespace = "My.App", Name = "ExplicitTable")] private sealed class WithStorage { }

    [StorageNaming(StorageNamingStyle.EntityType)] private sealed class WithEntityType { }

    [Fact]
    public void Defaults_To_FullNamespace_With_Dot()
    {
        var opts = new MongoOptions { Separator = "." };
        var name = MongoNaming.ResolveCollectionName(typeof(Outer.Inner), opts);
        name.Should().Be("Koan.Data.Connector.Mongo.Tests.MongoNamingTests+Outer+Inner");
    }

    [Fact]
    public void StorageName_Takes_Precendence()
    {
        var opts = new MongoOptions();
        var name = MongoNaming.ResolveCollectionName(typeof(WithStorageName), opts);
        name.Should().Be("ExplicitName");
    }

    [Fact]
    public void Storage_Attribute_Takes_Precendence()
    {
        var opts = new MongoOptions();
        var name = MongoNaming.ResolveCollectionName(typeof(WithStorage), opts);
        name.Should().Be("ExplicitTable");
    }

    [Fact]
    public void PerEntity_Hint_Wins_Over_Default_Options()
    {
        var opts = new MongoOptions { NamingStyle = StorageNamingStyle.FullNamespace, Separator = "." };
        var name = MongoNaming.ResolveCollectionName(typeof(WithEntityType), opts);
        name.Should().Be("WithEntityType");
    }

}


