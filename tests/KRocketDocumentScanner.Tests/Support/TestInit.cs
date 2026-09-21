using System.Runtime.CompilerServices;
using KRocketDocumentScanner.App.ViewModels.Localization;

namespace KRocketDocumentScanner.Tests.Support;

internal static class TestInit
{
    [ModuleInitializer]
    internal static void Init() => Strings.Load();
}
