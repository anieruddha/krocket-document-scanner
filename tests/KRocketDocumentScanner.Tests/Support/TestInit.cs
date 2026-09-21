using System.Runtime.CompilerServices;
using KRocketDocumentScanner.App.ViewModels.Localization;

namespace KRocketDocumentScanner.Tests.Support;

internal static class TestInit
{
    // The app loads the real strings at startup; tests read the same text the user sees.
    [ModuleInitializer]
    internal static void Init() => Strings.Load();
}
