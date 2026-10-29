using System.Runtime.CompilerServices;

// Core retains its public pack/store contracts while sharing the SDK's internal pure decoder.
[assembly: InternalsVisibleTo("SaveToolkit.Core")]
