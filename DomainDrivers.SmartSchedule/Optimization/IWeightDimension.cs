using DomainDrivers.SmartSchedule.Shared;

namespace DomainDrivers.SmartSchedule.Optimization;

public interface IWeightDimension
{
    bool IsSatisfiedBy(ICapacityDimension capacityDimension);

    TimeSlot? GetTimeSlot();
}

public interface IWeightDimension<in T> : IWeightDimension where T : ICapacityDimension
{
    bool IsSatisfiedBy(T capacityDimension);
}