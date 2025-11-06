using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S5.Recs.Models;


[Storage(Name = "RecsSettings")]
public sealed class SettingsDoc : Entity<SettingsDoc>
{
    public double PreferTagsWeight { get; set; } = 0.2;   // 0..1.0
    public int MaxPreferredTags { get; set; } = 3;        // 1..5
    public double DiversityWeight { get; set; } = 0.1;    // 0..0.2
    public double CensoredTagsPenaltyWeight { get; set; } = -0.7;  // -1.0..-0.1 (multiplier = 1 + value)
    public DateTimeOffset UpdatedAt { get; set; }
}
