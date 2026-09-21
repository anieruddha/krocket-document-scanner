using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Scanning;
using NAPS2.Scan;
using Xunit;

namespace KRocketDocumentScanner.Tests.Functional;

[Trait("Category", "Functional")]
public class AddressAndCodecTests
{
    // ---------------- address / device id extraction ----------------
    [Theory]
    [InlineData("airscan:ip=192.0.2.10", "192.0.2.10")]
    [InlineData("escl:https://192.0.2.10:443", "192.0.2.10")]
    [InlineData("plain text", null)]
    [InlineData("300.300", null)]
    public void Ipv4_is_extracted_from_ids(string text, string? expected) =>
        Assert.Equal(expected, ScannerAddressExtractor.Extract(text));

    [Fact]
    public void Extraction_uses_the_first_candidate_that_has_an_address() =>
        Assert.Equal("192.0.2.2", ScannerAddressExtractor.Extract(null, "nothing here", "x 192.0.2.2 y", "192.0.2.3"));

    [Theory]
    [InlineData("escl:https://h/AAAAAAAA-1111-2222-3333-BBBBBBBBBBBB", "aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb")]
    [InlineData("no id here", null)]
    [InlineData("1234-5678", null)]
    public void Device_id_is_extracted_and_lowercased(string text, string? expected) =>
        Assert.Equal(expected, ScannerAddressExtractor.ExtractDeviceId(text));

    // ---------------- manual address validation ----------------
    [Theory]
    [InlineData("192.0.2.211")]
    [InlineData("scanner.local")]
    [InlineData("scanner")]
    [InlineData("192.0.2.5:8080")]
    [InlineData("http://192.0.2.5")]
    [InlineData("https://192.0.2.5/eSCL")]
    [InlineData("[fe80::1]")]
    public void Valid_manual_addresses_are_accepted(string address) =>
        Assert.True(AddScannerViewModel.IsValidManualAddress(address));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("my scanner")]
    [InlineData("192.0.2.999")]
    [InlineData("192.0.2")]
    [InlineData("192..2.5")]
    [InlineData("192.0.2.5:99999")]
    [InlineData("ftp://192.0.2.5")]
    [InlineData("http://")]
    [InlineData("-bad-.local")]
    public void Invalid_manual_addresses_are_rejected(string address) =>
        Assert.False(AddScannerViewModel.IsValidManualAddress(address));

    // ---------------- display name cleanup ----------------
    [Theory]
    [InlineData("Model X Series (escl:https://192.0.2.10:443)", "Model X Series")]
    [InlineData("Model X Series (airscan:ip=192.0.2.10)", "Model X Series")]
    [InlineData("Office Scanner", "Office Scanner")]
    [InlineData("  Padded Name  ", "Padded Name")]
    [InlineData("Network Scanner (https://192.0.2.5)", "Network Scanner")]
    public void Protocol_suffixes_are_stripped_from_display_names(string raw, string expected) =>
        Assert.Equal(expected, new ScannerRegistryEntry { DriverId = "x", DisplayName = raw }.DisplayName);

    // ---------------- device id codec ----------------
    [Fact]
    public void Encoded_devices_decode_to_the_same_device()
    {
        var device = new ScanDevice(Driver.Sane, "airscan:e0:Model X", "Model X", ConnectionUri: null);
        var back = Naps2DeviceIdCodec.Decode(Naps2DeviceIdCodec.Encode(device));
        Assert.Equal(device.Driver, back.Driver);
        Assert.Equal(device.ID, back.ID);
        Assert.Equal(device.Name, back.Name);
        Assert.Null(back.ConnectionUri);
    }

    [Fact]
    public void Ids_with_separator_characters_survive_the_round_trip()
    {
        var device = new ScanDevice(Driver.Escl, "a|b%c d", "N|ame", ConnectionUri: "https://192.0.2.1/eSCL");
        var back = Naps2DeviceIdCodec.Decode(Naps2DeviceIdCodec.Encode(device));
        Assert.Equal("a|b%c d", back.ID);
        Assert.Equal("N|ame", back.Name);
        Assert.Equal("https://192.0.2.1/eSCL", back.ConnectionUri);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("x|y|z|w")]
    [InlineData("")]
    public void Unrecognised_ids_are_rejected(string id) =>
        Assert.Throws<FormatException>(() => Naps2DeviceIdCodec.Decode(id));

    [Theory]
    [InlineData("192.0.2.5", "https://192.0.2.5/eSCL")]
    [InlineData("http://192.0.2.5", "http://192.0.2.5/eSCL")]
    [InlineData("https://192.0.2.5/custom", "https://192.0.2.5/custom")]
    [InlineData("scanner.local", "https://scanner.local/eSCL")]
    public void Manual_addresses_become_escl_endpoints(string typed, string expectedUri)
    {
        var device = Naps2DeviceIdCodec.Decode(Naps2DeviceIdCodec.EncodeManualEscl(typed, "n"));
        Assert.Equal(Driver.Escl, device.Driver);
        Assert.Equal(expectedUri, device.ConnectionUri);
    }
}
