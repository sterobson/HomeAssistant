using System.Collections.Generic;

namespace HomeAssistant.Services;

internal class HistoryIntegrator
{
    public static double Integrate(IEnumerable<NumericHistoryEntry> historyEntries, DateTime startDate, DateTime endDate)
    {
        double lastValue = 0;
        DateTime lastTime = DateTime.MinValue;
        double totalArea = 0;
        foreach (NumericHistoryEntry historyItem in historyEntries)
        {
            double t = historyItem.LastChanged.Subtract(lastTime).TotalSeconds;
            double w = historyItem.State;

            if (historyItem.LastChanged < startDate)
            {
                continue;
            }

            if (lastTime > endDate)
            {
                continue;
            }

            if (historyItem.LastChanged > endDate && lastTime == DateTime.MinValue)
            {
                continue;
            }

            double cutOff = 0;
            if (lastTime < startDate && historyItem.LastChanged > startDate)
            {
                // We need to cut off the proportion of the trapezium that occured before the start date
                double secondsFromLastToStart = (startDate - lastTime).TotalSeconds;
                double secondsFromLastToThis = (historyItem.LastChanged - lastTime).TotalSeconds;

                double valAtStart = lastValue + (w - lastValue) * (secondsFromLastToStart / secondsFromLastToThis);
                cutOff += 0.5 * (lastValue + valAtStart) * secondsFromLastToStart;
            }

            if (lastTime < endDate && historyItem.LastChanged > endDate)
            {
                double secondsFromLastToEnd = (endDate - lastTime).TotalSeconds;
                double secondsFromEndToThis = (historyItem.LastChanged - endDate).TotalSeconds;

                double valAtEnd = lastValue + (w - lastValue) * (1 - secondsFromLastToEnd / secondsFromEndToThis);
                cutOff += 0.5 * (lastValue + valAtEnd) * secondsFromLastToEnd;
            }

            if (historyItem.LastChanged > startDate && lastTime < endDate)
            {
                // Wh = (a + b) * t / 2
                totalArea += 0.5 * (lastValue + w) * t - cutOff;
            }

            lastTime = historyItem.LastChanged;
            lastValue = w;
        }

        return totalArea;
    }
}
