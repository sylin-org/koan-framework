using Koan.Jobs;

public static class InvalidJobsSubmissionReceiverConsumer
{
    public static object Invalid(IEnumerable<Todo> todos)
        => todos.Submit();
}
