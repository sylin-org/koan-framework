using Sora.Data.Core.Model;

namespace S0.ConsoleJsonRepo
{
    public class Todo : Entity<Todo>
    {
        public string Title { get; set; } = string.Empty;
    }
}