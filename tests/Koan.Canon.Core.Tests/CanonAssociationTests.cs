using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using FluentAssertions;
using Koan.Canon.Attributes;
using Koan.Canon.Infrastructure;
using Koan.Canon.Model;
using Koan.Data.Core.Relationships;
using Newtonsoft.Json;
using Xunit;

namespace Koan.Canon.Core.Tests;

public class CanonRegistryTests
{
    [Fact]
    public void GetAggregationTags_ReturnsAggregationKeysFromAttributes()
    {
        var tags = CanonRegistry.GetAggregationTags(typeof(Customer));

        tags.Should().BeEquivalentTo(new[] { "email", "contact.phone" });
    }

    [Fact]
    public void GetAggregationTags_ForDynamicEntities_ComposesClassKeys()
    {
        var tags = CanonRegistry.GetAggregationTags(typeof(DynamicCustomer));

        tags.Should().BeEquivalentTo(new[] { "profile.email", "profile.userName" });
    }

    [Fact]
    public void GetEntityIdStructure_UsesAggregationTags()
    {
        var structure = CanonRegistry.GetEntityIdStructure(typeof(Customer));

        structure.Should().NotBeNull();
        structure!.Value.KeyComponents.Should().NotBeNull();
        structure.Value.KeyComponents.Select(c => c.Path)
            .Should().BeEquivalentTo(new[] { "email", "contact.phone" });
    }

    [Fact]
    public void GetValueObjectParent_ResolvesParentKey()
    {
        var parent = CanonRegistry.GetValueObjectParent(typeof(OrderAddress));

        parent.Should().NotBeNull();
        parent!.Value.Parent.Should().Be(typeof(Order));
        parent.Value.ParentKeyPath.Should().Be("orderId");
    }

    [Fact]
    public void GetEntityParent_ResolvesParentKey()
    {
        var parent = CanonRegistry.GetEntityParent(typeof(OrderLine));

        parent.Should().NotBeNull();
        parent!.Value.Parent.Should().Be(typeof(Order));
        parent.Value.ParentKeyPath.Should().Be("orderId");
    }

    [Fact]
    public void GetExternalIdKeys_UsesExplicitPolicyKey()
    {
        var keys = CanonRegistry.GetExternalIdKeys(typeof(CustomerWithExternalIdPolicy));

        keys.Should().BeEquivalentTo(new[] { "identifier.external.systemA" });
    }

    [Fact]
    public void GetExternalIdKeys_DefaultsToKeyPropertyWhenNoOverride()
    {
        var keys = CanonRegistry.GetExternalIdKeys(typeof(CustomerWithDefaultExternalId));

        keys.Should().BeEquivalentTo(new[] { "customerId" });
    }

    [Fact]
    public void GetExternalIdKeys_ReturnsEmptyWhenPolicyIsManual()
    {
        var keys = CanonRegistry.GetExternalIdKeys(typeof(CustomerWithManualPolicy));

        keys.Should().BeEmpty();
    }

    private sealed class Customer : CanonEntity<Customer>
    {
        [Key]
        [JsonProperty("customerId")]
        public string CustomerNumber { get; set; } = string.Empty;

        [AggregationKey]
        public string? Email { get; set; }

        [AggregationTag("contact.phone")]
        public string? Phone { get; set; }
    }

    [AggregationKeys("profile.email", "profile.userName")]
    private sealed class DynamicCustomer : DynamicCanonEntity<DynamicCustomer>
    {
    }

    private sealed class Order : CanonEntity<Order>
    {
        [Key]
        [JsonProperty("orderId")]
        public string OrderNumber { get; set; } = string.Empty;
    }

    private sealed class OrderAddress : CanonValueObject<OrderAddress>
    {
        [Parent(typeof(Order))]
        public string OrderId { get; set; } = string.Empty;
    }

    private sealed class OrderLine : CanonEntity<OrderLine>
    {
        [Key]
        public string LineNumber { get; set; } = string.Empty;

        [Parent(typeof(Order))]
        public string OrderId { get; set; } = string.Empty;
    }

    [CanonPolicy(ExternalIdPolicy = ExternalIdPolicy.AutoPopulate, ExternalIdKey = "identifier.external.systemA")]
    private sealed class CustomerWithExternalIdPolicy : CanonEntity<CustomerWithExternalIdPolicy>
    {
        [Key]
        public string CustomerNumber { get; set; } = string.Empty;
    }

    [CanonPolicy(ExternalIdPolicy = ExternalIdPolicy.AutoPopulate)]
    private sealed class CustomerWithDefaultExternalId : CanonEntity<CustomerWithDefaultExternalId>
    {
        [Key]
        [JsonProperty("customerId")]
        public string CustomerId { get; set; } = string.Empty;
    }

    [CanonPolicy(ExternalIdPolicy = ExternalIdPolicy.Manual)]
    private sealed class CustomerWithManualPolicy : CanonEntity<CustomerWithManualPolicy>
    {
        [Key]
        public string CustomerNumber { get; set; } = string.Empty;
    }
}



