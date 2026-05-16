namespace S5.Recs.Services;

public interface IRecommendationSettingsProvider
{
    (double PreferTagsWeight, int MaxPreferredTags, double DiversityWeight, double CensoredTagsPenaltyWeight) GetEffective();
    Task Invalidate(CancellationToken ct = default);
}