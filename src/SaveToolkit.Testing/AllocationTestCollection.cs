using Xunit;

namespace SaveToolkit.Testing;

// Concurrent runner activity can force unrelated allocations onto a measured thread.
[CollectionDefinition(DisableParallelization = true)]
public sealed class AllocationTestCollection;
