using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The logo and the signature of a letterhead, end to end on the pipeline that ships and a real SQL Server (Prompt
/// 10.4c, ADR 0059): uploaded as the file itself by the administrator under the version of the settings, told by their
/// bytes, read by anyone signed in of the same hospital and by no one of another, replaced and removed without a row
/// ever changing, and audited by their hash.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class LetterheadImagesApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Settings = new("/api/v1/settings", UriKind.Relative);
    private static readonly Uri Letterhead = new("/api/v1/settings/letterhead", UriKind.Relative);
    private static readonly Uri Logo = new("/api/v1/settings/letterhead/logo", UriKind.Relative);
    private static readonly Uri Signature = new("/api/v1/settings/letterhead/signature", UriKind.Relative);
    private static readonly Uri RemoveLogo = new("/api/v1/settings/letterhead/logo/remove", UriKind.Relative);
    private static readonly Uri RemoveSignature = new("/api/v1/settings/letterhead/signature/remove", UriKind.Relative);

    [Fact]
    public async Task ADR0059_the_administrator_uploads_replaces_and_removes_the_logo_and_anyone_signed_in_reads_it()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var cashier = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Cashier");
        var first = Png(320, 96);
        var second = Jpeg(640, 200);

        using (var none = await cashier.GetAsync(Logo))
        {
            await none.ShouldBeProblemAsync(HttpStatusCode.NotFound, "letterhead_image_not_found");
        }

        var settings = await ReadSettingsAsync(administrator);
        settings.Letterhead.LogoSha256.ShouldBeNull();

        // Without the version nothing is stored; nor by a cashier.
        using (var noVersion = await SendImageAsync(administrator, Logo, first, "image/png", ifMatch: null))
        {
            noVersion.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        }

        using (var notTheirs = await SendImageAsync(cashier, Logo, first, "image/png", PayersApi.ETag(settings.Version)))
        {
            await notTheirs.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using var uploaded = await SendImageAsync(administrator, Logo, first, "image/png", PayersApi.ETag(settings.Version));
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
        var afterFirst = await uploaded.ReadAsAsync<TenantSettingsDto>();
        afterFirst.Version.ShouldNotBe(settings.Version, "the images share the version of the settings");
        afterFirst.Letterhead.LogoSha256.ShouldBe(TenantImage.HashOf(first));
        uploaded.Headers.ETag!.Tag.ShouldBe(PayersApi.ETag(afterFirst.Version));

        // The version read before the upload is stale now.
        using (var stale = await SendImageAsync(administrator, Logo, second, "image/jpeg", PayersApi.ETag(settings.Version)))
        {
            await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        }

        using (var seen = await cashier.GetAsync(Logo))
        {
            seen.StatusCode.ShouldBe(HttpStatusCode.OK);
            seen.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            (await seen.Content.ReadAsByteArrayAsync()).ShouldBe(first);
            seen.Headers.CacheControl!.NoStore.ShouldBeTrue("an answer of the API is never kept by a browser or a proxy");
            seen.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        }

        using var replaced = await SendImageAsync(administrator, Logo, second, "image/jpeg", PayersApi.ETag(afterFirst.Version));
        var afterSecond = await replaced.ReadAsAsync<TenantSettingsDto>();
        afterSecond.Letterhead.LogoSha256.ShouldBe(TenantImage.HashOf(second));

        using (var seen = await cashier.GetAsync(Logo))
        {
            seen.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
            (await seen.Content.ReadAsByteArrayAsync()).ShouldBe(second);
        }

        using var removed = await administrator.SendWithIfMatchAsync(HttpMethod.Post, RemoveLogo, body: null, PayersApi.ETag(afterSecond.Version));
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        var afterRemoval = await removed.ReadAsAsync<TenantSettingsDto>();
        afterRemoval.Letterhead.LogoSha256.ShouldBeNull();

        using (var gone = await cashier.GetAsync(Logo))
        {
            await gone.ShouldBeProblemAsync(HttpStatusCode.NotFound, "letterhead_image_not_found");
        }

        // The first file again is the row there is: two rows for two files, however often they are named.
        using var again = await SendImageAsync(administrator, Logo, first, "image/png", PayersApi.ETag(afterRemoval.Version));
        (await again.ReadAsAsync<TenantSettingsDto>()).Letterhead.LogoSha256.ShouldBe(TenantImage.HashOf(first));

        await using var context = ApiContext(admin);
        var rows = await context.Set<TenantImage>().AsNoTracking().OrderBy(image => image.StoredAtUtc).ToListAsync();
        rows.Select(image => image.Sha256).ShouldBe([TenantImage.HashOf(first), TenantImage.HashOf(second)], ignoreOrder: true);
        rows.ShouldAllBe(image => image.Kind == LetterheadImageKind.Logo && image.TenantId == admin.TenantId);

        // Four changes, each audited with the hash before and after, and the image in none of them.
        using var trail = await administrator.GetAsync(new Uri($"/api/v1/audit-records?entityType=Tenant&entityId={admin.TenantId!.Value.Value}", UriKind.Relative));
        var body = await trail.Content.ReadAsStringAsync();
        var records = System.Text.Json.JsonSerializer.Deserialize<PageOf<AuditRecordDto>>(body, TestJson.Options)!.Items.Where(entry => entry.Action == "LetterheadImageChanged").ToList();
        records.Count.ShouldBe(4);
        records.Select(entry => entry.After.GetProperty("LogoSha256").GetString()).ShouldBe(
            [TenantImage.HashOf(first), TenantImage.HashOf(second), null, TenantImage.HashOf(first)], ignoreOrder: true);
        body.ShouldNotContain(Convert.ToBase64String(first)[..40]);
        body.ShouldNotContain(Convert.ToBase64String(second)[..40]);
    }

    [Fact]
    public async Task ADR0059_a_wrong_type_an_oversized_file_and_an_svg_are_refused_and_nothing_is_stored()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        var settings = await ReadSettingsAsync(administrator);
        var version = PayersApi.ETag(settings.Version);
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><script>alert(1)</script></svg>");

        var refusals = new (Uri Path, byte[] Content, string ContentType, string Reason)[]
        {
            (Logo, svg, "image/svg+xml", "An SVG is not accepted"),
            (Logo, svg, "image/png", "An SVG is not accepted"),
            (Logo, Encoding.ASCII.GetBytes("%PDF-1.7 and then the rest of a document that is no image"), "application/pdf", "not a PNG, JPEG or WebP image"),
            (Logo, Png(320, 96, TenantImage.LogoMaxBytes + 1), "image/png", "at most 256 KB"),
            (Logo, Png(320, 96, 900_000), "image/png", "at most 256 KB"),
            (Signature, Png(320, 96, TenantImage.SignatureMaxBytes + 1), "image/png", "at most 128 KB"),
            (Logo, Png(TenantImage.MaxSidePixels + 1, 96), "image/png", "at most 2000 pixels"),
            (Logo, Png(320, 96), "image/webp", "Content-Type"),
            (Logo, [], "image/png", "Send the image file"),
        };

        foreach (var (path, content, contentType, reason) in refusals)
        {
            using var refused = await SendImageAsync(administrator, path, content, contentType, version);

            var problem = await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            var errors = problem.GetProperty("errors");
            errors.EnumerateObject().Select(field => field.Name).ShouldBe(["content"], reason);
            errors.GetProperty("content")[0].GetString().ShouldNotBeNull().ShouldContain(reason);
        }

        // A JSON body, as every other change of the API sends, is no image either.
        using (var json = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Logo, new { content = Convert.ToBase64String(Png(320, 96)) }, version))
        {
            await json.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        }

        (await ReadSettingsAsync(administrator)).ShouldBe(settings, "nothing changed, the version included");
        await using var context = ApiContext(admin);
        (await context.Set<TenantImage>().CountAsync()).ShouldBe(0);

        // The largest signature that is allowed is taken.
        using var largest = await SendImageAsync(administrator, Signature, Png(320, 96, TenantImage.SignatureMaxBytes), "image/png", version);
        largest.StatusCode.ShouldBe(HttpStatusCode.OK, await largest.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ADR0059_another_hospital_never_reads_or_changes_the_images_of_this_one()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var ours = await NewApiTenantAsync();
        var theirs = await NewApiTenantAsync();
        using var ourAdministrator = factory.CreateClientFor(ours, "TenantAdmin");
        using var theirAdministrator = factory.CreateClientFor(theirs, "TenantAdmin");
        var logo = Png(320, 96);
        var signature = WebpExtended(400, 120);

        var ourSettings = await ReadSettingsAsync(ourAdministrator);
        using var withLogo = await SendImageAsync(ourAdministrator, Logo, logo, "image/png", PayersApi.ETag(ourSettings.Version));
        var afterLogo = await withLogo.ReadAsAsync<TenantSettingsDto>();
        using var withSignature = await SendImageAsync(ourAdministrator, Signature, signature, "image/webp", PayersApi.ETag(afterLogo.Version));
        var afterSignature = await withSignature.ReadAsAsync<TenantSettingsDto>();
        afterSignature.Letterhead.SignatureSha256.ShouldBe(TenantImage.HashOf(signature));

        // The other hospital has no image: it is answered 404 for both, and its letterhead names none.
        using (var theirLogo = await theirAdministrator.GetAsync(Logo))
        {
            await theirLogo.ShouldBeProblemAsync(HttpStatusCode.NotFound, "letterhead_image_not_found");
        }

        using (var theirSignature = await theirAdministrator.GetAsync(Signature))
        {
            await theirSignature.ShouldBeProblemAsync(HttpStatusCode.NotFound, "letterhead_image_not_found");
        }

        using (var theirLetterhead = await theirAdministrator.GetAsync(Letterhead))
        {
            var letterhead = await theirLetterhead.ReadAsAsync<LetterheadDto>();
            (letterhead.LogoSha256, letterhead.SignatureSha256).ShouldBe((null, null));
        }

        // The same bytes uploaded there are that hospital's own row, and removing them there leaves ours.
        var theirSettings = await ReadSettingsAsync(theirAdministrator);
        using var theirUpload = await SendImageAsync(theirAdministrator, Logo, logo, "image/png", PayersApi.ETag(theirSettings.Version));
        var theirAfter = await theirUpload.ReadAsAsync<TenantSettingsDto>();
        using var theirRemoval = await theirAdministrator.SendWithIfMatchAsync(HttpMethod.Post, RemoveLogo, body: null, PayersApi.ETag(theirAfter.Version));
        theirRemoval.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Nor does our version open their settings.
        using (var crossed = await theirAdministrator.SendWithIfMatchAsync(HttpMethod.Post, RemoveSignature, body: null, PayersApi.ETag(afterSignature.Version)))
        {
            await crossed.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        }

        using (var ourLogo = await ourAdministrator.GetAsync(Logo))
        {
            (await ourLogo.Content.ReadAsByteArrayAsync()).ShouldBe(logo);
        }

        using (var ourSignature = await ourAdministrator.GetAsync(Signature))
        {
            ourSignature.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
            (await ourSignature.Content.ReadAsByteArrayAsync()).ShouldBe(signature);
        }

        await using var ourContext = ApiContext(ours);
        (await ourContext.Set<TenantImage>().CountAsync()).ShouldBe(2, "a hospital's context sees its own images only");
        await using var theirContext = ApiContext(theirs);
        (await theirContext.Set<TenantImage>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ADR0059_the_signatory_and_the_designation_are_set_with_the_letterhead()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        var settings = await ReadSettingsAsync(administrator);

        using var saved = await administrator.SendWithIfMatchAsync(
            HttpMethod.Put,
            Letterhead,
            new UpdateLetterheadRequest { Signatory = "Dr A. Example", SignatoryDesignation = " Medical Superintendent " },
            PayersApi.ETag(settings.Version));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var letterhead = (await saved.ReadAsAsync<TenantSettingsDto>()).Letterhead;
        (letterhead.Signatory, letterhead.SignatoryDesignation).ShouldBe(("Dr A. Example", "Medical Superintendent"));
    }

    private static async Task<TenantSettingsDto> ReadSettingsAsync(HttpClient client)
    {
        using var read = await client.GetAsync(Settings);

        return await read.ReadAsAsync<TenantSettingsDto>();
    }

    // The upload as a browser sends it: the file as the body, its media type as the Content-Type.
    private static async Task<HttpResponseMessage> SendImageAsync(HttpClient client, Uri uri, byte[] content, string contentType, string? ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = new ByteArrayContent(content) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch).ShouldBeTrue();
        }

        return await client.SendAsync(request);
    }

    private static byte[] Png(uint width, uint height, int length = 64)
    {
        var bytes = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static byte[] Jpeg(ushort width, ushort height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        bytes.AddRange(new byte[14]);
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03]);
        bytes.AddRange(new byte[12]);
        return [.. bytes];
    }

    private static byte[] WebpExtended(int width, int height)
    {
        var bytes = new byte[40];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 32);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        "VP8X"u8.CopyTo(bytes.AsSpan(12));
        (bytes[24], bytes[25], bytes[26]) = ((byte)(width - 1), (byte)((width - 1) >> 8), (byte)((width - 1) >> 16));
        (bytes[27], bytes[28], bytes[29]) = ((byte)(height - 1), (byte)((height - 1) >> 8), (byte)((height - 1) >> 16));
        return bytes;
    }
}
