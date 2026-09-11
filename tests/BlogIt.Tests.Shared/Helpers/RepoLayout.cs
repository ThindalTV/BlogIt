using System.Reflection;

namespace BlogIt.Tests.Helpers;

/// <summary>Locates the repository root from a test assembly's output directory, for the
/// tests that assert on repository layout (solution membership, workflow wiring) rather
/// than on runtime behaviour.</summary>
public static class RepoLayout
{
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location)!);

            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
                directory = directory.Parent;

            if (directory is null)
                throw new InvalidOperationException("Could not locate the repository root from the test output directory.");

            return directory.FullName;
        }
    }

    public static string Combine(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);
}
