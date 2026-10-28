using CodoMetis.TypeKit.ValueObjects;

namespace AspectTestingSpike;

// A refusal: not readonly, so CMTK1002 and nothing generated.
public partial record struct Draft : IValue<int>;
