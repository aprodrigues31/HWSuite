namespace HWSuite.Core.Cpuid;

/// <summary>Performance and efficiency cores of a hybrid processor, from the efficiency class Windows gives each core.</summary>
public static class CoreTopology
{
    /// <summary>
    /// (performance, efficiency) core counts, or null when every core is the same kind. Windows gives P-cores a higher
    /// efficiency class than E-cores (Intel Alder Lake and later; AMD parts with "c" cores).
    /// </summary>
    public static (int Performance, int Efficiency)? Split(IReadOnlyCollection<byte> efficiencyClasses)
    {
        ArgumentNullException.ThrowIfNull(efficiencyClasses);
        if (efficiencyClasses.Count == 0 || efficiencyClasses.Distinct().Count() < 2)
        {
            return null;
        }

        var highest = efficiencyClasses.Max();
        var performance = efficiencyClasses.Count(c => c == highest);
        return (performance, efficiencyClasses.Count - performance);
    }
}
