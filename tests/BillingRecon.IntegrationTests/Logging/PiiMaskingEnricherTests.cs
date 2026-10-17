using System.Globalization;
using BillingRecon.Infrastructure.Common.Logging;
using BillingRecon.IntegrationTests.Support;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace BillingRecon.IntegrationTests.Logging;

/// <summary>The enricher on a real Serilog pipeline: what a sink receives after masking.</summary>
public sealed class PiiMaskingEnricherTests
{
    [Fact]
    public void A_string_property_is_masked_and_so_is_the_rendered_message()
    {
        var logEvent = Capture(logger => logger.Information("Reminder sent to {Recipient}", "priya.sharma@example.com"));

        logEvent.Scalar("Recipient").ShouldBe("p***@example.com");
        logEvent.RenderMessage(CultureInfo.InvariantCulture).ShouldBe("Reminder sent to \"p***@example.com\"");
    }

    [Fact]
    public void A_value_under_a_sensitive_name_is_masked_whatever_its_shape_or_type()
    {
        var logEvent = Capture(logger => logger.Information("{Phone} {Email} {MobileNumber}", "080-2345-6789", "see notes", 9876543210L));

        logEvent.Scalar("Phone").ShouldBe("***");
        logEvent.Scalar("Email").ShouldBe("***");
        logEvent.Scalar("MobileNumber").ShouldBe("***");
    }

    [Fact]
    public void Values_that_are_not_contact_data_are_left_exactly_as_they_were()
    {
        var billId = Guid.NewGuid();
        var postedAt = new DateTimeOffset(2026, 9, 29, 14, 3, 0, TimeSpan.Zero);

        var logEvent = Capture(logger => logger.Information(
            "{BillId} {BillNumber} {Amount} {PostedAtUtc} {TenantId} {Posted}",
            billId,
            "BIL/26-27/000123",
            9876543210.50m,
            postedAt,
            billId.ToString(),
            true));

        logEvent.Scalar("BillId").ShouldBe(billId);
        logEvent.Scalar("BillNumber").ShouldBe("BIL/26-27/000123");
        logEvent.Scalar("Amount").ShouldBe(9876543210.50m);
        logEvent.Scalar("PostedAtUtc").ShouldBe(postedAt);
        logEvent.Scalar("TenantId").ShouldBe(billId.ToString());
        logEvent.Scalar("Posted").ShouldBe(true);
    }

    [Fact]
    public void A_destructured_object_is_masked_at_every_depth()
    {
        var patient = new
        {
            Name = "Priya",
            Mobile = 9876543210L,
            Notes = "prefers e-mail: priya.sharma@example.com",
            Guarantor = new { Phone = "landline", EmailAddress = "ravi@example.org" },
        };

        var logEvent = Capture(logger => logger.Information("Registered {@Patient}", patient));

        var structure = logEvent.Properties["Patient"].ShouldBeOfType<StructureValue>();
        Scalar(structure, "Name").ShouldBe("Priya");
        Scalar(structure, "Mobile").ShouldBe("***");
        Scalar(structure, "Notes").ShouldBe("prefers e-mail: p***@example.com");

        var guarantor = Property(structure, "Guarantor").ShouldBeOfType<StructureValue>();
        Scalar(guarantor, "Phone").ShouldBe("***");
        Scalar(guarantor, "EmailAddress").ShouldBe("r***@example.org");
    }

    [Fact]
    public void An_object_logged_under_a_sensitive_name_is_masked_throughout()
    {
        var logEvent = Capture(logger => logger.Information("Changed {@Phone}", new { Number = "080-2345-6789", Extension = 12, Verified = true }));

        var structure = logEvent.Properties["Phone"].ShouldBeOfType<StructureValue>();
        Scalar(structure, "Number").ShouldBe("***");
        Scalar(structure, "Extension").ShouldBe("***");
        Scalar(structure, "Verified").ShouldBe(true);
    }

    [Fact]
    public void Dictionary_keys_are_masked_too_and_stay_distinct()
    {
        var sent = new Dictionary<string, int>
        {
            ["priya.sharma@example.com"] = 1,
            ["pooja.sharma@example.com"] = 2,
            ["front desk"] = 3,
        };

        var logEvent = Capture(logger => logger.Information("Sent {@Sent}", sent));

        var dictionary = logEvent.Properties["Sent"].ShouldBeOfType<DictionaryValue>();
        var entries = dictionary.Elements.ToDictionary(pair => (string)pair.Key.Value!, pair => ((ScalarValue)pair.Value).Value);
        entries.Count.ShouldBe(3);
        entries["p***@example.com"].ShouldBe(1);
        entries["p***@example.com (2)"].ShouldBe(2);
        entries["front desk"].ShouldBe(3);
        logEvent.ToJson().ShouldNotContain("sharma");
    }

    [Fact]
    public void Every_element_of_a_sequence_is_masked()
    {
        string[] recipients = ["a.one@example.com", "b.two@example.com"];

        var logEvent = Capture(logger => logger.Information("Sent to {Recipients}", recipients));

        var sequence = logEvent.Properties["Recipients"].ShouldBeOfType<SequenceValue>();
        sequence.Elements.Select(element => ((ScalarValue)element).Value).ShouldBe(["a***@example.com", "b***@example.com"]);
    }

    [Fact]
    public void Dictionary_values_are_masked_by_key_name_and_by_content()
    {
        var contact = new Dictionary<string, string>
        {
            ["phone"] = "extension 12",
            ["note"] = "call 9876543210",
            ["city"] = "Pune",
        };

        var logEvent = Capture(logger => logger.Information("Updated {@Contact}", contact));

        var dictionary = logEvent.Properties["Contact"].ShouldBeOfType<DictionaryValue>();
        var values = dictionary.Elements.ToDictionary(pair => (string)pair.Key.Value!, pair => ((ScalarValue)pair.Value).Value);
        values["phone"].ShouldBe("***");
        values["note"].ShouldBe("call ******3210");
        values["city"].ShouldBe("Pune");
    }

    [Fact]
    public void A_uri_that_contains_an_address_is_masked()
    {
        var logEvent = Capture(logger => logger.Information("Opened {Link}", new Uri("mailto:priya.sharma@example.com")));

        logEvent.Scalar("Link").ShouldBe("mailto:p***@example.com");
    }

    [Fact]
    public void Properties_added_by_the_log_context_are_masked_too()
    {
        LogEvent logEvent;
        using (LogContext.PushProperty("ContactEmail", "priya.sharma@example.com"))
        {
            logEvent = Capture(logger => logger.Information("No properties of its own"));
        }

        logEvent.Scalar("ContactEmail").ShouldBe("p***@example.com");
    }

    private static LogEvent Capture(Action<ILogger> write)
    {
        var sink = new InMemoryLogSink();
        using (var logger = new LoggerConfiguration().Enrich.FromLogContext().Enrich.With<PiiMaskingEnricher>().WriteTo.Sink(sink).CreateLogger())
        {
            write(logger);
        }

        return sink.Events.ShouldHaveSingleItem();
    }

    private static LogEventPropertyValue Property(StructureValue structure, string name)
        => structure.Properties.Where(property => property.Name == name).ShouldHaveSingleItem().Value;

    private static object? Scalar(StructureValue structure, string name)
        => Property(structure, name).ShouldBeOfType<ScalarValue>().Value;
}
