using System;
using System.Collections.Generic;
using System.Linq;

namespace Focus.ModManagers
{
    public class ModInfo : IModLocatorKey
    {
        public static readonly IEqualityComparer<ModInfo> KeyComparer = new ModInfoByKeyComparer();

        public IEnumerable<string> AllNames => Components.Select(x => x.Name).Prepend(Name);

        public IReadOnlyList<ModComponentInfo> Components { get; init; }
        public string Id { get; init; }
        public string Name { get; init; }

        public ModInfo(string id, string name, IEnumerable<ModComponentInfo>? components = null)
        {
            Id = id;
            Name = name;
            Components = (components ?? Enumerable.Empty<ModComponentInfo>()).ToList().AsReadOnly();
        }

        public bool IncludesName(string name)
        {
            return AllNames.Any(x => x.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    // Components are compared by identity (owning mod + component id + directory), not by reference. Callers such as
    // the post-build report build a component in one place (e.g. the synthetic "Vanilla" component) and compare it to
    // one that came out of the mod repository; with reference equality those never match, which silently turned
    // correct results into reported conflicts.
    public class ModComponentInfo : IEquatable<ModComponentInfo>
    {
        public static readonly ModComponentInfo Invalid = new(ModLocatorKey.Empty, "", "", "");

        private static readonly StringComparer stringComparer = StringComparer.CurrentCultureIgnoreCase;

        public IModLocatorKey ModKey { get; init; }
        public string Id { get; init; }
        public bool IsEnabled { get; init; }
        public string Name { get; init; }
        public string Path { get; init; }

        public ModComponentInfo(IModLocatorKey modKey, string id, string name, string path, bool isEnabled = true)
        {
            ModKey = modKey;
            Id = id;
            Name = name;
            Path = path;
            IsEnabled = isEnabled;
        }

        public bool Equals(ModComponentInfo? other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other is null)
                return false;
            return
                ModLocatorKeyComparer.Default.Equals(ModKey, other.ModKey) &&
                stringComparer.Equals(Id, other.Id) &&
                stringComparer.Equals(Path, other.Path);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as ModComponentInfo);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                ModLocatorKeyComparer.Default.GetHashCode(ModKey),
                stringComparer.GetHashCode(Id),
                stringComparer.GetHashCode(Path));
        }

        public override string ToString()
        {
            return !string.IsNullOrEmpty(Name) ? Name : Id;
        }

        public static bool operator ==(ModComponentInfo? x, ModComponentInfo? y)
        {
            return Equals(x, y);
        }

        public static bool operator !=(ModComponentInfo? x, ModComponentInfo? y)
        {
            return !Equals(x, y);
        }
    }

    class ModInfoByKeyComparer : IEqualityComparer<ModInfo>
    {
        public bool Equals(ModInfo? x, ModInfo? y)
        {
            return ModLocatorKeyComparer.Default.Equals(x, y);
        }

        public int GetHashCode(ModInfo obj)
        {
            return obj is not null ? ModLocatorKeyComparer.Default.GetHashCode(obj) : 0;
        }
    }
}
