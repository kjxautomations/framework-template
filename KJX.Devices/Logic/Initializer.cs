using System.Collections.Immutable;

namespace KJX.Devices.Logic;

public static class Initializer
{
    public static void Initialize(IEnumerable<ISupportsInitialization> devices)
    {
        var groups = devices.GroupBy(x => x.InitializationGroup, y => y)
            .ToImmutableSortedDictionary(x => x.Key, y => y.ToImmutableList());
        foreach (var kvp in groups)
        {
            Parallel.ForEach(kvp.Value, x =>
            {
                if (!x.IsInitialized) x.Initialize();
            });

        }
    }

    public static void Shutdown(IEnumerable<ISupportsInitialization> devices)
    {
        var groups = devices.GroupBy(x => x.InitializationGroup, y => y)
            .ToImmutableSortedDictionary(x => -(int)x.Key, y => y.ToImmutableList());
        foreach (var kvp in groups)
        {
            Parallel.ForEach(kvp.Value, x =>
            {
                if (x.IsInitialized) x.Shutdown();
            });

        }
    }
}