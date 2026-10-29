namespace SaveToolkit.Plugins.GenericJson.Tests;

// Allocation measurements also run in the full suite, where concurrent tests add runtime noise.
[CollectionDefinition(DisableParallelization = true)]
public sealed class JsonPerformanceCollection;
