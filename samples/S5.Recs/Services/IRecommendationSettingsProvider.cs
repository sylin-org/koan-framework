namespace S5.Recs.Services;

public interface IRecommendationSettingsProvider
{
    (double PreferTagsWeight, int MaxPreferredTags, double DiversityWeight) GetEffective();
    Task InvalidateAsync(CancellationToken ct = default);
}