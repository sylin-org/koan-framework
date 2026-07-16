using Koan.Communication.Adapters;

namespace Koan.Communication.Connector.RabbitMq.Tests;

public sealed class RabbitMqTopologySpec
{
    [Fact]
    public void Logical_channel_participates_in_routes_and_queue_identity()
    {
        var defaultBinding = Binding("default");
        var priorityBinding = Binding("priority");

        var defaultRoute = RabbitMqCommunicationAdapter.Topology.Route(
            defaultBinding.Lane,
            defaultBinding.Channel,
            defaultBinding.ContractId);
        var priorityRoute = RabbitMqCommunicationAdapter.Topology.Route(
            priorityBinding.Lane,
            priorityBinding.Channel,
            priorityBinding.ContractId);

        defaultRoute.Should().NotBe(priorityRoute);
        RabbitMqCommunicationAdapter.Topology.Queue("app", defaultBinding)
            .Should().NotBe(RabbitMqCommunicationAdapter.Topology.Queue("app", priorityBinding));
    }

    private static CommunicationAdapterBinding Binding(string channel)
        => new(
            $"transport|{channel}|order|receiver",
            CommunicationLane.Transport,
            channel,
            "order@1",
            "receiver@1");
}
