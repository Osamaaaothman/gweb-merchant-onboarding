using System.Runtime.CompilerServices;

namespace Gweb.Tests;

/// <summary>
/// Runs once when the test assembly loads, before any test executes -- guaranteed by
/// the CLR, so this is race-free even though xUnit can run test classes in parallel.
/// Every WebApplicationFactory&lt;Program&gt;-based test boots the SAME Program.cs
/// (one Lambda hosts the whole API -- see ADR-0001), which requires
/// APPLICATIONS_TABLE_NAME unless PERSISTENCE_PROVIDER=inmemory. Tests never talk to
/// real AWS, so this is set unconditionally for the whole test run.
///
/// AI_PROVIDER=mock is set the same way, for the same reason: the endpoint test suite
/// must be fast, deterministic, and runnable with no network access or API key --
/// GeminiEvaluationProvider itself is unit-tested separately with a fake HttpMessageHandler.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    public static void UseInMemoryPersistence()
    {
        Environment.SetEnvironmentVariable("PERSISTENCE_PROVIDER", "inmemory");
        Environment.SetEnvironmentVariable("AI_PROVIDER", "mock");
    }
}
