using DomainDrivers.SmartSchedule.Shared;

namespace DomainDrivers.SmartSchedule.Optimization;

public class OptimizationFacade
{
    #region config generyka

    private const int Generations = 5;
    private const int GenerationSize = 200;
    private const int SurvivePerGeneration = 20;

    private const int MutationChance = 10;
    private const int PercentChanceToSkipProject = 0;
    private readonly Random _random = new();

    #endregion

    public Result Calculate(IList<Item> items, TotalCapacity totalCapacity)
    {
        return Calculate(items, totalCapacity, Comparer<Item>.Create((x, y) => y.Value.CompareTo(x.Value)));
    }

    public Result Calculate(IList<Item> items, TotalCapacity totalCapacity, Comparer<Item> comparator)
    {
        List<Item> automaticallyIncludedItems = items
            .Where(item => item.IsWeightZero)
            .ToList();
        double guaranteedValue = automaticallyIncludedItems
            .Sum(item => item.Value);

        List<Dictionary<Item, ISet<ICapacityDimension>>> candidates = InitGeneration(items, totalCapacity);
        for (int generation = 0; generation < Generations; generation++)
        {
            for (int i = candidates.Count; i < GenerationSize; i++)
            {
                Dictionary<Item, ISet<ICapacityDimension>> firstCandidate = candidates[GetRandomCandidateToCross(SurvivePerGeneration)];
                Dictionary<Item, ISet<ICapacityDimension>> secondCandidate = candidates[GetRandomCandidateToCross(SurvivePerGeneration)];
                candidates.Add(Cross(firstCandidate, secondCandidate, items, totalCapacity));
            }

            candidates = candidates.OrderByDescending(c => c.Select(x => x.Key.Value).Sum()).Take(SurvivePerGeneration).ToList();
        }

        var result = candidates.First();
        var resultItems = result.Select(x => x.Key).ToList();
        resultItems.AddRange(automaticallyIncludedItems);

        return new Result(result.Select(x => x.Key.Value).Sum() + guaranteedValue,
            resultItems, result);
    }

    private List<Dictionary<Item, ISet<ICapacityDimension>>> InitGeneration(IList<Item> items, TotalCapacity totalCapacity)
    {
        List<Dictionary<Item, ISet<ICapacityDimension>>> candidates = new List<Dictionary<Item, ISet<ICapacityDimension>>>();
        var orderedItems = items.OrderByDescending(x => x.Value).ToList();
        for (int i = candidates.Count; i < GenerationSize; i++)
        {
            if (GetRandomOutcome(MutationChance))
            {
                SwapTwoRandomItems(orderedItems);
            }
            candidates.Add(GetResultCandidate(orderedItems, totalCapacity, PercentChanceToSkipProject));
        }

        candidates = candidates.OrderByDescending(c => c.Select(x => x.Key.Value).Sum()).Take(SurvivePerGeneration).ToList();
        return candidates;
    }

    private bool GetRandomOutcome(int percentChance)
    {
        return _random.Next(0, 100) < percentChance;
    }

    private int GetRandomCandidateToCross(int maxIt)
    {
        return _random.Next(0, maxIt);
    }

    private Dictionary<Item, ISet<ICapacityDimension>> Cross(Dictionary<Item, ISet<ICapacityDimension>> candidate1, Dictionary<Item, ISet<ICapacityDimension>> candidate2,
        IList<Item> items, TotalCapacity totalCapacity)
    {
        var bothIncluded = new List<Item>();
        var oneIncluded = new List<Item>();
        var noIncluded = new List<Item>();

        foreach (var item in items)
        {
            if (candidate1.ContainsKey(item))
            {
                if (candidate2.ContainsKey(item))
                {
                    bothIncluded.Add(item);
                }
                else
                {
                    oneIncluded.Add(item);
                }
            }
            else
            {
                if (candidate2.ContainsKey(item))
                {
                    oneIncluded.Add(item);
                }
                else
                {
                    noIncluded.Add(item);
                }
            }
        }

        //krzy¿ujemy poprzez branie w pierwszej kolejnoœci projektów, które s¹ zawarte w obu kandydatach, nastêpnie w jednym z nich,
        // a na koniec dodajemy projekty, których nie ma w ¿adnym kandydacie
        var orderedItems = bothIncluded;
        orderedItems.AddRange(oneIncluded.OrderBy(_ => Guid.NewGuid()));
        orderedItems.AddRange(noIncluded.OrderBy(x => x.Value));

        while (GetRandomOutcome(MutationChance))
        {
            SwapTwoRandomItems(orderedItems);
        }

        return GetResultCandidate(orderedItems, totalCapacity, 0);
    }

    private void SwapTwoRandomItems(List<Item> orderedItems)
    {
        int indexA = GetRandomCandidateToCross(orderedItems.Count);
        int indexB = GetRandomCandidateToCross(orderedItems.Count);
        (orderedItems[indexA], orderedItems[indexB]) = (orderedItems[indexB], orderedItems[indexA]);
    }

    private Dictionary<Item, ISet<ICapacityDimension>> GetResultCandidate(IList<Item> items, TotalCapacity totalCapacity,
        int chanceToSkipProject)
    {
        var allCapacities = totalCapacity.Capacities().OrderBy(_ => Guid.NewGuid());
        var mappedAllCapacities = allCapacities.Select(capacity => new CapacityDimensionWithUsedTimes(capacity)).ToDictionary(x => x.Guid, y => y);
        Dictionary<Item, ISet<ICapacityDimension>> itemToCapacitiesMap = new Dictionary<Item, ISet<ICapacityDimension>>();

        foreach (Item item in items.ToList())
        {
            if (GetRandomOutcome(chanceToSkipProject))
            {
                continue;
            }

            Dictionary<Guid, List<TimeSlot>> chosenCapacities = MatchCapacities(item.TotalWeight, mappedAllCapacities);

            if (chosenCapacities.Count == 0)
            {
                continue;
            }

            foreach (KeyValuePair<Guid, List<TimeSlot>> chosenCapacity in chosenCapacities)
            {
                mappedAllCapacities[chosenCapacity.Key].UsedTimeSlots.AddRange(chosenCapacity.Value);
            }

            itemToCapacitiesMap.Add(item, new HashSet<ICapacityDimension>(chosenCapacities.Select(x => mappedAllCapacities[x.Key].CapacityDimension)));
        }

        return itemToCapacitiesMap;
    }

    private Dictionary<Guid, List<TimeSlot>> MatchCapacities(
        TotalWeight totalWeight,
        Dictionary<Guid, CapacityDimensionWithUsedTimes> availableCapacities)
    {
        Dictionary<Guid, List<TimeSlot>> result = new Dictionary<Guid, List<TimeSlot>>();
        foreach (IWeightDimension weightComponent in totalWeight.Components())
        {
            TimeSlot? timeSlot = weightComponent.GetTimeSlot();
            IEnumerable<KeyValuePair<Guid, CapacityDimensionWithUsedTimes>> matchingCapacities = availableCapacities
                .Where(dimension => weightComponent.IsSatisfiedBy(dimension.Value.CapacityDimension));
            if (timeSlot != null)
            {
                matchingCapacities = matchingCapacities.Where(dimension =>
                    dimension.Value.UsedTimeSlots.All(ts => !ts.OverlapsEdgesWith(timeSlot)));
            }

            KeyValuePair<Guid, CapacityDimensionWithUsedTimes>? matchingCapacity = PickFromAvailableCapacities(matchingCapacities.ToList());
            if (matchingCapacity != null)
            {
                if (timeSlot != null)
                {
                    if (result.ContainsKey(matchingCapacity.Value.Key))
                    {
                        result[matchingCapacity.Value.Key].Add(timeSlot);
                    }
                    else
                    {
                        result.Add(matchingCapacity.Value.Key, [timeSlot]);
                    }
                }
            }
            else
            {
                return new Dictionary<Guid, List<TimeSlot>>();
            }
        }

        return result;
    }

    private KeyValuePair<Guid, CapacityDimensionWithUsedTimes>? PickFromAvailableCapacities(List<KeyValuePair<Guid, CapacityDimensionWithUsedTimes>> availableCapacities)
    {
        if (!availableCapacities.Any())
        {
            return null;
        }

        return availableCapacities.FirstOrDefault();
    }


    private class CapacityDimensionWithUsedTimes(ICapacityDimension capacityDimension)
    {
        //Aby obs³u¿yæ wiele umiejêtnoœci per zasób powinno byæ ResourceId
        public ICapacityDimension CapacityDimension { get; set; } = capacityDimension;

        //Aby obs³u¿yæ wiele umiejêtnoœci per zasób powinniœmy u¿ywaæ kalendarzy dostêpnoœci zamiast trzymaæ u¿yte TimeSloty
        public List<TimeSlot> UsedTimeSlots { get; set; } = new List<TimeSlot>();

        public Guid Guid { get; set; } = Guid.NewGuid();
    }
}