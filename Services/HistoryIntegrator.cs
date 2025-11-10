using System.Collections.Generic;

namespace HomeAssistant.Services;

internal class HistoryIntegrator
{
    public static double Integrate(IEnumerable<HistoryEntry> historyEntries, DateTime startDate, DateTime endDate)
    {
        double lastValue = 0;
        DateTime lastTime = DateTime.MinValue;
        double totalArea = 0;
        foreach (HistoryEntry historyItem in historyEntries)
        {
            double w = double.Parse(historyItem.State ?? "0");
            double t = historyItem.LastChanged.Subtract(lastTime).TotalSeconds;

            if (historyItem.LastChanged < startDate)
            {
                continue;
            }

            if (lastTime > endDate)
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
