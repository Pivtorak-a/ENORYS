namespace Enorys.Core.Decision;

public static class Choice
{
    /// <summary>Stable numerical softmax; temperatures at/below 1e-9 use argmax.</summary>
    public static double[] Probabilities(ReadOnlySpan<double> utilities, double temperature)
    {
        if (utilities.IsEmpty) return [];
        if (!double.IsFinite(temperature) || temperature < 0)
            throw new ArgumentOutOfRangeException(nameof(temperature));
        double max = double.NegativeInfinity;
        int winner = 0;
        for (int i = 0; i < utilities.Length; i++)
        {
            if (!double.IsFinite(utilities[i]))
                throw new ArgumentException("Utilities must be finite.", nameof(utilities));
            if (utilities[i] > max) { max = utilities[i]; winner = i; }
        }
        var result = new double[utilities.Length];
        if (temperature <= 1e-9) { result[winner] = 1; return result; }

        double sum = 0;
        for (int i = 0; i < utilities.Length; i++)
        {
            result[i] = Math.Exp((utilities[i] - max) / temperature);
            sum += result[i];
        }
        for (int i = 0; i < result.Length; i++) result[i] /= sum;
        return result;
    }
}
