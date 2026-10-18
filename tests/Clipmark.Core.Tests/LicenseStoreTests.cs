using Clipmark.Core.Licensing;

namespace Clipmark.Core.Tests;

public class LicenseStoreTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void License_file_round_trips()
    {
        var file = new LicenseFile("ABCDE-FGHIJ");

        Assert.Equal(file, LicenseStore.Parse(LicenseStore.Serialize(file)));
    }

    [Fact]
    public void Empty_file_round_trips()
    {
        Assert.Equal(new LicenseFile(null), LicenseStore.Parse(LicenseStore.Serialize(new LicenseFile(null))));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    public void Broken_file_gives_an_empty_license(string json)
    {
        Assert.Equal(new LicenseFile(null), LicenseStore.Parse(json));
    }

    [Fact]
    public void Parsed_timestamps_are_utc()
    {
        var file = LicenseStore.Parse("""{ "activatedUtc": "2026-10-12T11:00:00+02:00" }""");

        Assert.Equal(Start, file.ActivatedUtc);
        Assert.Equal(DateTimeKind.Utc, file.ActivatedUtc!.Value.Kind);
    }

    [Fact]
    public void Activation_fields_round_trip()
    {
        var file = new LicenseFile(null, "rl1.a.b", "install-1", "tpm", Start, Start.AddHours(1), Start.AddHours(2), Start.AddDays(30));

        Assert.Equal(file, LicenseStore.Parse(LicenseStore.Serialize(file)));
    }

    [Fact]
    public void Absent_activation_fields_serialize_as_null()
    {
        string json = LicenseStore.Serialize(new LicenseFile("K"));

        foreach (string name in new[] { "entitlement", "installId", "keyStore", "activatedUtc", "lastRefreshUtc", "highWaterUtc", "retryAfterUtc" })
            Assert.Contains($"\"{name}\": null", json);
    }

    [Fact]
    public void An_old_file_with_local_trial_fields_still_loads_and_drops_them()
    {
        string path = Path.Combine(Path.GetTempPath(), $"clipmark-license-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            { "key": "ABCDE", "trialStarted": "2026-10-12T09:00:00Z", "trialLastSeen": "2026-10-12T09:00:00Z", "trialTag": "t",
              "entitlement": "rl1.a.b", "installId": "install-1", "keyStore": null, "activatedUtc": null, "lastRefreshUtc": null }
            """);
        try
        {
            var file = LicenseStore.Load(path);

            Assert.Equal(new LicenseFile("ABCDE", "rl1.a.b", "install-1"), file);
            Assert.DoesNotContain("trial", LicenseStore.Serialize(file));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
