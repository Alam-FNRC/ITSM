namespace ItsTool.Domain.Entities.Risks;

/// <summary>
/// The Excel register's formula: the average of the three impacts times the likelihood, 1 to 25. The bands close
/// the Excel's gaps for fractional scores (decided 2 Nov 2026): Low up to 4, Medium up to 12, High above.
/// </summary>
public static class RiskScore
{
    public const double LowMax = 4;
    public const double MediumMax = 12;

    public static bool IsRating(int value) => value is >= 1 and <= 5;

    public static double Of(int confidentiality, int integrity, int availability, int likelihood) =>
        Math.Round((confidentiality + integrity + availability) / 3.0 * likelihood, 2);

    /// <summary>The residual score, once all four residual ratings are given.</summary>
    public static double? Of(int? confidentiality, int? integrity, int? availability, int? likelihood) =>
        confidentiality.HasValue && integrity.HasValue && availability.HasValue && likelihood.HasValue
            ? Of(confidentiality.Value, integrity.Value, availability.Value, likelihood.Value)
            : null;

    public static string Level(double score) => score <= LowMax ? "Low" : score <= MediumMax ? "Medium" : "High";

    public static double Of(Risk risk) => Of(risk.ImpactConfidentiality, risk.ImpactIntegrity, risk.ImpactAvailability, risk.Likelihood);

    public static double? ResidualOf(Risk risk) =>
        Of(risk.ResidualConfidentiality, risk.ResidualIntegrity, risk.ResidualAvailability, risk.ResidualLikelihood);
}
