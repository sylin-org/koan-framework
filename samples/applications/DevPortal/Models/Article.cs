using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transfers;

namespace DevPortal.Models;

public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public ArticleStatus Status { get; set; } = ArticleStatus.Draft;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public async Task Approve(CancellationToken ct = default)
    {
        Status = ArticleStatus.Approved;
        UpdatedAt = DateTimeOffset.UtcNow;
        await this.Save(ct);
    }

    public static Task<TransferResult<string>> PublishApproved(
        PublicationChannel channel,
        CancellationToken ct = default)
        => Article.Copy(article => article.Status == ArticleStatus.Approved)
            .To(source: channel.ToString())
            .Run(ct);
}
