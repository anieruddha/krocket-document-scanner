using System.Text.Json;

namespace KRocketDocumentScanner.App.ViewModels.Localization;

public static class Strings
{
    public static string MinimizeTooltip { get; set; } = "Minimize";
    public static string MaximizeTooltip { get; set; } = "Maximize";
    public static string CloseTooltip { get; set; } = "Close";

    public static string SaveScanDialogTitle { get; set; } = "Save scan";

    public static string AlreadyRunningMessage { get; set; } = "KRocketDocumentScanner is already running.";
    public static string AlreadyRunningManageMessage { get; set; } = "KRocketDocumentScanner is already running, so Manage Scanners can't open on its own. Close KRocketDocumentScanner and try again.";

    public static string ScanWindowTitle { get; set; } = "Scan";
    public static string SaveButton { get; set; } = "Save…";
    public static string ScannerUnavailableHeadline { get; set; } = "Scanner not available";
    public static string ManageScannerButton { get; set; } = "Manage scanners";
    public static string UnsavedPagesTitle { get; set; } = "Unsaved pages";
    public static string UnsavedPagesMessage { get; set; } = "You have {0} you haven't saved. Close anyway?";
    public static string PreviewNotScannedTitle { get; set; } = "Preview not scanned";
    public static string PreviewNotScannedMessage { get; set; } = "You have a preview that hasn't been scanned. Close anyway?";
    public static string ScannerBusyTitle { get; set; } = "Scanner is working";
    public static string ScannerBusyMessage { get; set; } = "The scanner is still working. Close anyway?";
    public static string ScannerBusyWithPagesMessage { get; set; } = "The scanner is still working, and you have {0} you haven't saved. Close anyway?";
    public static string DiscardAndCloseButton { get; set; } = "Close Anyway";
    public static string CancelButton { get; set; } = "Cancel";
    public static string SourceLabel { get; set; } = "Source";
    public static string ColorLabel { get; set; } = "Color";
    public static string ResolutionLabel { get; set; } = "Resolution (DPI)";
    public static string CropAreaLabel { get; set; } = "Paper size";
    public static string CropPanelToggleButton { get; set; } = "Crop Adjustment";
    public static string AutoDeskewLabel { get; set; } = "Straighten pages automatically";
    public static string SkipBlankPagesLabel { get; set; } = "Skip blank pages";
    public static string ReduceFileSizeLabel { get; set; } = "Reduce file size";
    public static string ReduceFileSizeTip { get; set; } = "Compresses pages when saved as PDF or JPEG. Turn off to keep full quality.";
    public static string PreviewButton { get; set; } = "Preview";
    public static string ScanActionButton { get; set; } = "Scan";
    public static string PreviewPlaceholder { get; set; } = "Preview the scan to see the page here";
    public static string KeepPageRatio { get; set; } = "Keep the {0} page ratio";
    public static string SizeMinimumTip { get; set; } = "Minimum {0} {1} while Fill the page is on (70% of the page).";
    public static string KeepRatioTip { get; set; } = "Keeps the crop in the same shape as the selected page size. Stays on while Fill the page is on.";
    public static string MinimapStretchTip { get; set; } = "Fills the whole page with the crop when saved as a PDF. Keeps the page shape, and the crop can't be smaller than 70% of the page width.";
    public static string MinimapResetButton { get; set; } = "Reset";
    public static string MinimapResetTip { get; set; } = "Put the paper size, crop, Keep the page ratio and Fill the page back to how they were when the preview appeared.";
    public static string CheckingScannerStatus { get; set; } = "Checking scanner…";
    public static string PreviewingStatus { get; set; } = "Getting a preview…";
    public static string PreviewReadyStatus { get; set; } = "Preview ready. Adjust the area if you like, then scan.";
    public static string PreviewReadyLooksLikeStatus { get; set; } = "Preview ready — looks like {0}. Adjust the area if you like, then scan.";
    public static string PreviewFailedStatus { get; set; } = "Couldn't get a preview: {0}";
    public static string NoScannerSelectedMessage { get; set; } = "No scanner is selected.";
    public static string ScanningFeederStatus { get; set; } = "Scanning pages from the feeder…";
    public static string ScanningStatus { get; set; } = "Scanning…";
    public static string PageCountZero { get; set; } = "{0} pages";
    public static string PageCountOne { get; set; } = "{0} page";
    public static string PageCountMany { get; set; } = "{0} pages";
    public static string Pages(int count) =>
        string.Format(count == 0 ? PageCountZero : count == 1 ? PageCountOne : PageCountMany, count);
    public static string ScannedProgressStatus { get; set; } = "Scanned {0}…";
    public static string NoPagesScannedStatus { get; set; } = "No pages were scanned. Check the feeder isn't empty.";
    public static string BatchFinishedStatus { get; set; } = "Finished — {0} scanned.";
    public static string ScannedTotalStatus { get; set; } = "Scanned — {0} in total.";
    public static string ScanStoppedStatus { get; set; } = "Scanning stopped: {0} What was already scanned ({1}) is still here.";
    public static string ScanFailedStatus { get; set; } = "Couldn't scan: {0}";
    public static string NoScannerSetUpMessage { get; set; } = "No scanner is set up yet.";
    public static string SelectedScannerFallbackName { get; set; } = "The selected scanner";
    public static string ScannerNotReachableMessage { get; set; } = "{0} isn't reachable right now. Check it's powered on and connected to the network.";
    public static string ScannerReachFailedMessage { get; set; } = "Couldn't reach {0}: {1}";
    public static string NoScannerSelectedName { get; set; } = "No scanner selected";
    public static string SuggestedFileName { get; set; } = "Scan";
    public static string SaveFailedStatus { get; set; } = "Couldn't save: {0}";
    public static string UnitToggleTip { get; set; } = "Switch between inches and centimetres";
    public static string ScannerStatusRefreshFailedMessage { get; set; } = "Couldn't refresh scanner status: {0}";
    public static string SetDefaultScannerFailedMessage { get; set; } = "Couldn't set the default scanner: {0}";
    public static string RemoveScannerFailedMessage { get; set; } = "Couldn't remove that scanner: {0}";
    public static string ScannerSearchFailedMessage { get; set; } = "Couldn't search for scanners: {0}";
    public static string AddScannerFailedMessage { get; set; } = "Couldn't add that scanner: {0}";
    public static string ConnectedDeviceAddress { get; set; } = "Connected device";
    public static string WidthLabel { get; set; } = "Width";
    public static string HeightLabel { get; set; } = "Height";
    public static string MinimapStretchToPage { get; set; } = "Fill the page";
    public static string PagesHeading { get; set; } = "Pages";
    public static string NoPagesYet { get; set; } = "Scanned pages appear here.";
    public static string RemovePageButton { get; set; } = "Remove";
    public static string MovePageUpTooltip { get; set; } = "Move page up";
    public static string MovePageDownTooltip { get; set; } = "Move page down";
    public static string PageLabelFormat { get; set; } = "Page {0}";
    public static string SourceFlatbed { get; set; } = "Flatbed";
    public static string SourceFeeder { get; set; } = "Document feeder";
    public static string SourceFeederDuplex { get; set; } = "Document feeder (both sides)";
    public static string ColorModeColor { get; set; } = "Color";
    public static string ColorModeGrayscale { get; set; } = "Grayscale";
    public static string ColorModeBlackAndWhite { get; set; } = "Black & white";
    public static string ColorModeBlackAndWhiteClean { get; set; } = "Black & white (clean)";

    public static string ManageScannersTitle { get; set; } = "Manage Scanners";
    public static string RefreshStatusButton { get; set; } = "Refresh status";
    public static string AddScannerButton { get; set; } = "Add scanner…";
    public static string AddScannerEmptyStateButton { get; set; } = "Add Scanner";
    public static string NoScannersHeadline { get; set; } = "No scanners added yet.";
    public static string NoScannersSubtext { get; set; } = "Your scanner list is empty. Add a network or manual scanner to get started.";
    public static string MakeDefaultButton { get; set; } = "Make default";
    public static string RemoveButton { get; set; } = "Remove";
    public static string ReadyStatus { get; set; } = "Ready";
    public static string NotReachableStatus { get; set; } = "Not reachable";
    public static string UnknownStatus { get; set; } = "Unknown";

    public static string AddScannerTitle { get; set; } = "Add scanner";
    public static string DiscoverTab { get; set; } = "Search network";
    public static string ManualEntryTab { get; set; } = "Add manually";
    public static string SearchForScannersButton { get; set; } = "Search for scanners";
    public static string DiscoveredListHeading { get; set; } = "Discovered List";
    public static string SearchingText { get; set; } = "Searching…";
    public static string NoScannersFoundText { get; set; } = "No scanners found. Check the scanner is powered on and connected. Some scanners — especially network ones — won't show up automatically; use the Add manually tab for those.";
    public static string AddButton { get; set; } = "Add";
    public static string AddressLabel { get; set; } = "Scanner Address (IP or Hostname)";
    public static string ManualScannerUnreachableFormat { get; set; } = "Couldn't reach a scanner at {0}. Check that it's turned on and on the same network. If the address is right, some scanners need a number after it, for example {0}:8080.";
    public static string ManualAddressInvalidFormat { get; set; } = "'{0}' isn't a valid address. Enter the scanner's IP address, for example 192.168.1.50, or its web address, for example http://192.168.1.50.";
    public static string AddressWatermark { get; set; } = "e.g. 192.168.1.50";
    public static string AddScannerActionButton { get; set; } = "Add scanner";
    public static string AirScanLabel { get; set; } = "AirScan";
    public static string EsclLabel { get; set; } = "eSCL";

    public static void Load(string? requestedLanguage = null)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Localization");
        LoadFile(Path.Combine(dir, "strings.en.json"));

        var requested = LanguageCode(requestedLanguage);
        if (requested is not null)
        {
            if (requested == "en") return;
            var requestedPath = Path.Combine(dir, $"strings.{requested}.json");
            if (File.Exists(requestedPath))
            {
                LoadFile(requestedPath);
                return;
            }
        }

        var language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (language.Length == 2 && language != "en")
            LoadFile(Path.Combine(dir, $"strings.{language}.json"));
    }

    private static string? LanguageCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var code = value.Trim().Split('-', '_')[0].ToLowerInvariant();
        return code.Length is 2 or 3 && code.All(c => c is >= 'a' and <= 'z') ? code : null;
    }

    private static void LoadFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var groups = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (groups is null) return;
            var values = groups.Values.SelectMany(g => g).ToDictionary(p => p.Key, p => p.Value);

            foreach (var prop in typeof(Strings).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (values.TryGetValue(prop.Name, out var value))
                    prop.SetValue(null, value);
            }
        }
        catch
        {
        }
    }
}
