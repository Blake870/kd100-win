using System.Runtime.CompilerServices;

namespace Kd100Win.Tests;

static class TestSetup
{
    [ModuleInitializer]
    internal static void Init() => Log.ToFile = false;
}
