public static class InvalidRelationshipsReceiverConsumer
{
    public static object Invalid(IEnumerable<PlainModel> models)
        => models.Relatives();

    public sealed class PlainModel;
}
