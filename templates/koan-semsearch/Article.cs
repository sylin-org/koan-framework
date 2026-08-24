using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;

namespace KoanSemSearchApp;

[Embedding(Template = "{Title}. {Body}")]
public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}
