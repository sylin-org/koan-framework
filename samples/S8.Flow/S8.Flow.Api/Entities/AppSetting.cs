using Koan.Data.Abstractions;

namespace S8.Flow.Api.Entities;

// Force Mongo provider selection explicitly for the probe (avoids any ambiguity)
[SourceAdapter("mongo")]
public class AppSetting : Koan.Data.Core.Model.Entity<AppSetting>
{
    // Inherit Id from base Entity<T>. Declaring a new Id caused duplicate _id mapping in Mongo.
    public string? Value { get; set; }
}
