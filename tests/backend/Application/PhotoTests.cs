using Soro.Application.Common;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

internal static class ImageSamples
{
    public static byte[] Jpeg(int size = 2_000) => Pad([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00], size);

    public static byte[] Png(int size = 2_000) => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], size);

    public static byte[] Webp(int size = 2_000) =>
        Pad([0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50], size);

    public static byte[] Pdf() => Pad("%PDF-1.7"u8.ToArray(), 2_000);

    private static byte[] Pad(byte[] header, int size)
    {
        var data = new byte[Math.Max(size, header.Length)];
        header.CopyTo(data, 0);
        return data;
    }
}

public class PhotoTests
{
    private static async Task<(TestWorld World, FamilyId Family, ProductId Format)> WorldWithFamilyAsync()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        var format = ProductId.New();
        var family = FamilyId.New();
        await world.CreateFamily.HandleAsync(
            new CreateFamilyCommand(family, "Riz parfumé", null, [new(format, "5 kg", null, 3000, 3500, 2, true)]));
        return (world, family, format);
    }

    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task A_photo_is_accepted_when_its_content_is_a_known_image(string kind, string contentType)
    {
        var (world, family, _) = await WorldWithFamilyAsync();
        var data = kind switch { "jpeg" => ImageSamples.Jpeg(), "png" => ImageSamples.Png(), _ => ImageSamples.Webp() };

        var result = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, data));

        Assert.True(result.IsSuccess);
        var photo = await world.GetPhoto.HandleAsync(new GetPhotoQuery(result.Value));
        Assert.Equal(contentType, photo.Value!.ContentType);
        Assert.Equal(data, photo.Value.Data);
    }

    // 6. Photo facultative : un PDF renommé en .jpg est refusé
    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_whatever_its_name()
    {
        var (world, family, _) = await WorldWithFamilyAsync();

        var result = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Pdf()));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("image", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, world.Photos.Count);
    }

    [Fact]
    public async Task A_photo_over_200_kilobytes_is_refused_and_one_of_exactly_200_is_accepted()
    {
        var (world, family, _) = await WorldWithFamilyAsync();

        var tooBig = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Jpeg(200 * 1024 + 1)));
        var limit = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Jpeg(200 * 1024)));

        Assert.Equal(ErrorCodes.RuleViolation, tooBig.Error!.Code);
        Assert.Contains("lourde", tooBig.Error.Message, StringComparison.Ordinal);
        Assert.True(limit.IsSuccess);
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var (world, family, _) = await WorldWithFamilyAsync();

        var result = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, []));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
    }

    [Fact]
    public async Task Replacing_a_photo_removes_the_previous_one()
    {
        var (world, family, _) = await WorldWithFamilyAsync();
        var first = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Jpeg()));

        var second = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Png()));

        Assert.Equal(1, world.Photos.Count);
        Assert.Equal(ErrorCodes.NotFound, (await world.GetPhoto.HandleAsync(new GetPhotoQuery(first.Value))).Error!.Code);
        Assert.True((await world.GetPhoto.HandleAsync(new GetPhotoQuery(second.Value))).IsSuccess);
    }

    [Fact]
    public async Task A_format_may_have_its_own_photo_and_removing_it_falls_back_to_the_family_photo()
    {
        var (world, family, format) = await WorldWithFamilyAsync();
        var familyPhoto = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Jpeg()));
        var ownPhoto = await world.SetPhoto.HandleAsync(new SetPhotoCommand(format.Value, PhotoOwner.Format, ImageSamples.Png()));

        var withOwn = (await world.ListProducts.HandleAsync(new ListProductsQuery(null, false))).Single();
        await world.RemovePhoto.HandleAsync(new RemovePhotoCommand(format.Value, PhotoOwner.Format));
        var withoutOwn = (await world.ListProducts.HandleAsync(new ListProductsQuery(null, false))).Single();

        Assert.Equal(ownPhoto.Value.Value, withOwn.PhotoId);
        Assert.Equal(familyPhoto.Value.Value, withoutOwn.PhotoId);
        Assert.Equal(1, world.Photos.Count);
    }

    [Fact]
    public async Task Removing_the_last_photo_leaves_the_product_without_photo_and_still_sellable()
    {
        var (world, family, _) = await WorldWithFamilyAsync();
        await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Jpeg()));

        var removed = await world.RemovePhoto.HandleAsync(new RemovePhotoCommand(family.Value, PhotoOwner.Family));

        Assert.True(removed.IsSuccess);
        Assert.Null((await world.ListProducts.HandleAsync(new ListProductsQuery(null, false))).Single().PhotoId);
        Assert.Equal(0, world.Photos.Count);
    }

    [Fact]
    public async Task Removing_a_photo_that_does_not_exist_is_not_an_error()
    {
        var (world, family, _) = await WorldWithFamilyAsync();

        Assert.True((await world.RemovePhoto.HandleAsync(new RemovePhotoCommand(family.Value, PhotoOwner.Family))).IsSuccess);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Only_a_manager_changes_a_photo(Role role)
    {
        var (world, family, format) = await WorldWithFamilyAsync();
        world.ActAs(world.Cashier, role);

        var set = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Jpeg()));
        var setFormat = await world.SetPhoto.HandleAsync(new SetPhotoCommand(format.Value, PhotoOwner.Format, ImageSamples.Jpeg()));
        var remove = await world.RemovePhoto.HandleAsync(new RemovePhotoCommand(family.Value, PhotoOwner.Family));

        Assert.Equal(ErrorCodes.Forbidden, set.Error!.Code);
        Assert.Equal(ErrorCodes.Forbidden, setFormat.Error!.Code);
        Assert.Equal(ErrorCodes.Forbidden, remove.Error!.Code);
        Assert.Equal(0, world.Photos.Count);
    }

    [Fact]
    public async Task Any_signed_in_user_of_the_shop_reads_a_photo()
    {
        var (world, family, _) = await WorldWithFamilyAsync();
        var saved = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Jpeg()));
        world.ActAs(world.Cashier, Role.Cashier);

        Assert.True((await world.GetPhoto.HandleAsync(new GetPhotoQuery(saved.Value))).IsSuccess);
    }

    // 12. Isolation
    [Fact]
    public async Task A_photo_of_another_shop_is_not_found_even_with_its_exact_identifier()
    {
        var (world, family, _) = await WorldWithFamilyAsync();
        var saved = await world.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Jpeg()));
        var stranger = new TestWorld();
        stranger.ActAs(stranger.Manager, Role.Manager);
        foreach (var photo in world.Photos.All)
        {
            stranger.Photos.Put(photo);
        }

        var read = await stranger.GetPhoto.HandleAsync(new GetPhotoQuery(saved.Value));
        var set = await stranger.SetPhoto.HandleAsync(new SetPhotoCommand(family.Value, PhotoOwner.Family, ImageSamples.Jpeg()));

        Assert.Equal(ErrorCodes.NotFound, read.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, set.Error!.Code);
    }

    [Fact]
    public async Task A_photo_for_an_unknown_owner_is_not_found()
    {
        var (world, _, _) = await WorldWithFamilyAsync();

        var result = await world.SetPhoto.HandleAsync(new SetPhotoCommand(Guid.NewGuid(), PhotoOwner.Format, ImageSamples.Jpeg()));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }
}
