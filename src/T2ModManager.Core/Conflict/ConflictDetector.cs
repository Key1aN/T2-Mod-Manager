using T2ModManager.Core.Models;

namespace T2ModManager.Core.Conflict;

public static class ConflictDetector
{
    public static IReadOnlyList<ModConflict> FindConflicts(IEnumerable<ModManifest> manifests)
    {
        ModManifest[] mods = manifests.ToArray();
        List<ModConflict> conflicts = [];

        for (int firstIndex = 0; firstIndex < mods.Length; firstIndex++)
        {
            for (int secondIndex = firstIndex + 1; secondIndex < mods.Length; secondIndex++)
            {
                ModManifest first = mods[firstIndex];
                ModManifest second = mods[secondIndex];

                if (first.Conflicts.Contains(second.Id, StringComparer.OrdinalIgnoreCase) ||
                    second.Conflicts.Contains(first.Id, StringComparer.OrdinalIgnoreCase))
                {
                    conflicts.Add(new ModConflict(
                        first.Id,
                        second.Id,
                        "manifest:explicit-conflict",
                        "The mod authors declared these packages incompatible."));
                }

                HashSet<string> firstExclusive = first.Resources
                    .Where(resource => resource.Mode == "exclusive")
                    .Select(resource => resource.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (string resource in second.Resources
                    .Where(resource => resource.Mode == "exclusive")
                    .Select(resource => resource.Id))
                {
                    if (firstExclusive.Contains(resource))
                    {
                        conflicts.Add(new ModConflict(
                            first.Id,
                            second.Id,
                            resource,
                            "Both mods require exclusive ownership of the same resource."));
                    }
                }
            }
        }

        return conflicts;
    }

    public static IReadOnlyList<ModDependency> FindMissingDependencies(
        ModManifest manifest,
        IEnumerable<ModManifest> activeManifests)
    {
        HashSet<string> active = activeManifests
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return manifest.Dependencies.Where(dependency => !active.Contains(dependency.Id)).ToArray();
    }
}

