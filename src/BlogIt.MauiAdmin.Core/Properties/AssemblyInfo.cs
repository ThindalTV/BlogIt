using System.Runtime.CompilerServices;

// The storage key format is internal because nothing outside this library should build one, but
// the tests assert that a deleted site's token is actually removed from secure storage.
[assembly: InternalsVisibleTo("BlogIt.Tests.MAUI")]
