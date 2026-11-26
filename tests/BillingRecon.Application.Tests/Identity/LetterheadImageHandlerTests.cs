using System.Buffers.Binary;
using System.Text;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using NSubstitute;

namespace BillingRecon.Application.Tests.Identity;

/// <summary>
/// The use cases of the letterhead's images, with the ports replaced (ADR 0059): an upload is refused by its shape
/// before anything is read, stored once under the version of the settings and named by its hash; a removal clears the
/// name and deletes nothing; a read finds the image the tenant names and no other.
/// </summary>
public sealed class LetterheadImageHandlerTests
{
    private readonly IClock _clock = TestDoubles.ClockAt(TestDoubles.Today);
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ITenantImageRepository _images = Substitute.For<ITenantImageRepository>();
    private readonly Tenant _tenant = Tenant.Create("northfield", "Northfield Care Hospital", DateTimeOffset.UnixEpoch).Value.Saved(0, 0, 0, 0, 0, 0, 7, 209);

    public LetterheadImageHandlerTests()
    {
        _tenant.ClearDomainEvents();
        _tenants.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(_tenant);
    }

    private SetLetterheadImageHandler Setter(IUnitOfWork unitOfWork) => new(new SetLetterheadImageCommandValidator(), _tenants, _images, _clock, unitOfWork);

    private RemoveLetterheadImageHandler Remover(IUnitOfWork unitOfWork) => new(new RemoveLetterheadImageCommandValidator(), _tenants, unitOfWork);

    private SetLetterheadImageCommand Upload(byte[] content, string? declared = "image/png", LetterheadImageKind kind = LetterheadImageKind.Logo) => new()
    {
        Kind = kind,
        Content = content,
        DeclaredContentType = declared,
        ExpectedVersion = RowVersionText.Of(_tenant),
    };

    [Fact]
    public async Task ADR0059_an_upload_is_stored_once_and_the_tenant_names_it_by_its_hash()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        var content = Png(320, 96);
        var sha256 = TenantImage.HashOf(content);
        var storedAt = _clock.UtcNow;

        var result = await Setter(unitOfWork).HandleAsync(Upload(content), CancellationToken.None);

        result.Value.Letterhead.LogoSha256.ShouldBe(sha256);
        result.Value.Letterhead.SignatureSha256.ShouldBeNull();
        _images.Received(1).Add(Arg.Is<TenantImage>(image =>
            image.Kind == LetterheadImageKind.Logo && image.Sha256 == sha256 && image.Content.SequenceEqual(content) && image.StoredAtUtc == storedAt));
        _tenant.DomainEvents.ShouldHaveSingleItem().ShouldBe(new TenantLetterheadImageChanged(LetterheadImageKind.Logo, null, sha256));
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());

        // The same file again: nothing is stored, saved or audited.
        await Setter(unitOfWork).HandleAsync(Upload(content), CancellationToken.None);
        _images.Received(1).Add(Arg.Any<TenantImage>());
        _tenant.DomainEvents.Count.ShouldBe(1);
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0059_bytes_the_hospital_stored_before_are_named_again_without_a_second_row()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        var content = Png(320, 96);
        var sha256 = TenantImage.HashOf(content);
        _images.FindAsync(LetterheadImageKind.Logo, sha256, Arg.Any<CancellationToken>()).Returns(TenantImage.Store(LetterheadImageKind.Logo, content, DateTimeOffset.UnixEpoch));

        var result = await Setter(unitOfWork).HandleAsync(Upload(content), CancellationToken.None);

        result.Value.Letterhead.LogoSha256.ShouldBe(sha256);
        _images.DidNotReceive().Add(Arg.Any<TenantImage>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0059_an_upload_under_a_stale_version_stores_nothing()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Setter(unitOfWork).HandleAsync(Upload(Png(320, 96)) with { ExpectedVersion = "AAAAAAAAAAE=" }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflict().Code);
        _tenant.LogoSha256.ShouldBeNull();
        _images.DidNotReceive().Add(Arg.Any<TenantImage>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0059_a_file_of_the_wrong_kind_size_or_name_is_refused_with_its_reason()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        var refusals = new (SetLetterheadImageCommand Command, string Reason)[]
        {
            (Upload(svg, "image/svg+xml"), "An SVG is not accepted"),
            (Upload(svg, "image/png"), "An SVG is not accepted"),
            (Upload(Encoding.ASCII.GetBytes("%PDF-1.7 and then some more bytes of a document"), "application/pdf"), "not a PNG, JPEG or WebP image"),
            (Upload([]), "Send the image file"),
            (Upload(Png(320, 96, TenantImage.LogoMaxBytes + 1)), "at most 256 KB"),
            (Upload(Png(320, 96, TenantImage.SignatureMaxBytes + 1), kind: LetterheadImageKind.Signature), "at most 128 KB"),
            (Upload(Png(TenantImage.MaxSidePixels + 1, 96)), "at most 2000 pixels"),
            (Upload(Png(320, 96), "image/jpeg"), "Content-Type"),
            (Upload(Png(320, 96), declared: null), "Content-Type"),
        };

        foreach (var (command, reason) in refusals)
        {
            var result = await Setter(unitOfWork).HandleAsync(command, CancellationToken.None);

            result.Error.Code.ShouldBe("validation_failed");
            result.Error.FieldErrors.ShouldNotBeNull().Keys.ShouldBe(["content"], reason);
            result.Error.FieldErrors["content"].ShouldHaveSingleItem().ShouldContain(reason);
        }

        _tenant.DomainEvents.ShouldBeEmpty();
        _images.DidNotReceive().Add(Arg.Any<TenantImage>());
        await _tenants.DidNotReceive().GetCurrentAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0059_a_signature_of_the_largest_size_allowed_is_accepted()
    {
        var unitOfWork = TestDoubles.UnitOfWork();

        var result = await Setter(unitOfWork).HandleAsync(Upload(Png(320, 96, TenantImage.SignatureMaxBytes), kind: LetterheadImageKind.Signature), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Letterhead.SignatureSha256.ShouldNotBeNull();
    }

    [Fact]
    public async Task ADR0059_a_removal_clears_the_name_and_a_second_one_changes_nothing()
    {
        var unitOfWork = TestDoubles.UnitOfWork();
        var sha256 = TenantImage.HashOf(Png(320, 96));
        _tenant.ChangeLetterheadImage(LetterheadImageKind.Signature, sha256);
        _tenant.ClearDomainEvents();
        var command = new RemoveLetterheadImageCommand { Kind = LetterheadImageKind.Signature, ExpectedVersion = RowVersionText.Of(_tenant) };

        var stale = await Remover(unitOfWork).HandleAsync(command with { ExpectedVersion = "AAAAAAAAAAE=" }, CancellationToken.None);
        stale.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflict().Code);
        _tenant.SignatureSha256.ShouldBe(sha256);

        var result = await Remover(unitOfWork).HandleAsync(command, CancellationToken.None);

        result.Value.Letterhead.SignatureSha256.ShouldBeNull();
        _tenant.DomainEvents.ShouldHaveSingleItem().ShouldBe(new TenantLetterheadImageChanged(LetterheadImageKind.Signature, sha256, null));
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());

        await Remover(unitOfWork).HandleAsync(command, CancellationToken.None);
        _tenant.DomainEvents.Count.ShouldBe(1);
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADR0059_a_read_answers_the_image_the_tenant_names_and_404_when_it_names_none()
    {
        var handler = new GetLetterheadImageHandler(_tenants, _images);
        var content = Png(320, 96);
        var sha256 = TenantImage.HashOf(content);

        var none = await handler.HandleAsync(new GetLetterheadImageQuery(LetterheadImageKind.Logo), CancellationToken.None);
        none.Error.Code.ShouldBe(IdentityErrors.LetterheadImageNotFoundCode);
        none.Error.Kind.ShouldBe(ErrorKind.NotFound);
        await _images.DidNotReceive().FindAsync(Arg.Any<LetterheadImageKind>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        _tenant.ChangeLetterheadImage(LetterheadImageKind.Logo, sha256);
        _images.FindAsync(LetterheadImageKind.Logo, sha256, Arg.Any<CancellationToken>()).Returns(TenantImage.Store(LetterheadImageKind.Logo, content, DateTimeOffset.UnixEpoch));

        var found = await handler.HandleAsync(new GetLetterheadImageQuery(LetterheadImageKind.Logo), CancellationToken.None);
        found.Value.ContentType.ShouldBe("image/png");
        found.Value.Content.ShouldBe(content);
        found.Value.Sha256.ShouldBe(sha256);

        // The signature is another image: the logo is never answered for it.
        var signature = await handler.HandleAsync(new GetLetterheadImageQuery(LetterheadImageKind.Signature), CancellationToken.None);
        signature.Error.Code.ShouldBe(IdentityErrors.LetterheadImageNotFoundCode);
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
}
