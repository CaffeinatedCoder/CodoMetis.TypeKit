// Rule types that read alike: the same name, in two namespaces, for FaultTests. A file of their own,
// since a file with a file-scoped namespace declares no other.

namespace CodoMetis.TypeKit.Tests.Shipping
{
    internal enum Rule { Blank }

    /// <summary>A closed set written by hand, the contract alone, since this assembly runs no generator.</summary>
    internal sealed class RuleSet : CodoMetis.TypeKit.ClosedSets.IClosedSet<RuleSet>
    {
        public static readonly RuleSet Blank = new("Blank");

        private RuleSet(string name) => Name = name;

        public string Name { get; }
        public string QualifiedName => $"Rule.{Name}";
        public static IReadOnlyList<RuleSet> All => [Blank];

        public static bool TryParse(string? name, out RuleSet member)
        {
            member = Blank;
            return name == Blank.Name;
        }
    }
}

namespace CodoMetis.TypeKit.Tests.Invoicing
{
    internal enum Rule { Blank }

    /// <summary>A closed set written by hand, the contract alone, since this assembly runs no generator.</summary>
    internal sealed class RuleSet : CodoMetis.TypeKit.ClosedSets.IClosedSet<RuleSet>
    {
        public static readonly RuleSet Blank = new("Blank");

        private RuleSet(string name) => Name = name;

        public string Name { get; }
        public string QualifiedName => $"Rule.{Name}";
        public static IReadOnlyList<RuleSet> All => [Blank];

        public static bool TryParse(string? name, out RuleSet member)
        {
            member = Blank;
            return name == Blank.Name;
        }
    }
}
