using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Application.Stock;

public enum PhotoOwner
{
    /// <summary>La photo de la famille, partagée par tous ses formats.</summary>
    Family,

    /// <summary>La photo propre à un format, qui remplace celle de la famille.</summary>
    Format,
}

/// <summary>Reconnaît une image par son contenu : le nom du fichier ne prouve rien (FMT-04).</summary>
internal static class ImageSniffer
{
    public const int MaxBytes = 200 * 1024;

    public static string? ContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}

public sealed record SetPhotoCommand(Guid OwnerId, PhotoOwner Owner, byte[] Data);

/// <summary>Met ou remplace la photo d'une famille ou d'un format ; l'ancienne est supprimée.</summary>
public sealed class SetPhotoHandler(
    ICurrentUser user,
    IProductFamilyRepository families,
    IProductRepository products,
    IPhotoStore photos,
    IUnitOfWork unitOfWork)
{
    /// <summary>Taille maximale d'une photo acceptée (FMT-04).</summary>
    public const int MaxBytes = ImageSniffer.MaxBytes;

    public async Task<Result<PhotoId>> HandleAsync(SetPhotoCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<PhotoId>.Failure(ApplicationError.Forbidden("Seul un gérant peut changer une photo."));
        }

        var target = await PhotoTarget.FindAsync(command.OwnerId, command.Owner, user.Tenant, families, products, cancellationToken);
        if (target is null)
        {
            return Result<PhotoId>.Failure(ApplicationError.NotFound("Produit introuvable."));
        }

        if (command.Data.Length > ImageSniffer.MaxBytes)
        {
            return Result<PhotoId>.Failure(ApplicationError.RuleViolation("Cette image est trop lourde (200 Ko au plus)."));
        }

        var contentType = ImageSniffer.ContentType(command.Data);
        if (contentType is null)
        {
            return Result<PhotoId>.Failure(ApplicationError.RuleViolation("Ce fichier n'est pas une image (JPEG, PNG ou WebP)."));
        }

        var previous = target.Photo;
        var photo = new Photo(PhotoId.New(), user.Tenant, contentType, command.Data);
        await photos.AddAsync(photo, cancellationToken);
        await target.SetAsync(photo.Id, cancellationToken);
        if (previous is { } old)
        {
            await photos.RemoveAsync(old, user.Tenant, cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Result<PhotoId>.Success(photo.Id);
    }
}

public sealed record RemovePhotoCommand(Guid OwnerId, PhotoOwner Owner);

/// <summary>Retire la photo ; sans photo propre, un format retrouve celle de sa famille. Retirer ce qui n'existe pas n'est pas une erreur.</summary>
public sealed class RemovePhotoHandler(
    ICurrentUser user,
    IProductFamilyRepository families,
    IProductRepository products,
    IPhotoStore photos,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<bool>> HandleAsync(RemovePhotoCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanManageCatalogue(user.Role))
        {
            return Result<bool>.Failure(ApplicationError.Forbidden("Seul un gérant peut changer une photo."));
        }

        var target = await PhotoTarget.FindAsync(command.OwnerId, command.Owner, user.Tenant, families, products, cancellationToken);
        if (target is null)
        {
            return Result<bool>.Failure(ApplicationError.NotFound("Produit introuvable."));
        }

        if (target.Photo is { } old)
        {
            await target.SetAsync(null, cancellationToken);
            await photos.RemoveAsync(old, user.Tenant, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Result<bool>.Success(true);
    }
}

public sealed record PhotoContent(string ContentType, byte[] Data);

public sealed record GetPhotoQuery(PhotoId Id);

/// <summary>Lecture d'une photo : tout utilisateur connecté du commerce ; celle d'un autre commerce est introuvable.</summary>
public sealed class GetPhotoHandler(ICurrentUser user, IPhotoStore photos)
{
    public async Task<Result<PhotoContent>> HandleAsync(GetPhotoQuery query, CancellationToken cancellationToken = default)
    {
        var photo = await photos.GetAsync(query.Id, user.Tenant, cancellationToken);
        return photo is null
            ? Result<PhotoContent>.Failure(ApplicationError.NotFound("Photo introuvable."))
            : Result<PhotoContent>.Success(new(photo.ContentType, photo.Data));
    }
}

/// <summary>Ce qui porte une photo : une famille ou un format.</summary>
internal sealed class PhotoTarget
{
    private readonly Func<PhotoId?, CancellationToken, Task> _set;

    private PhotoTarget(PhotoId? photo, Func<PhotoId?, CancellationToken, Task> set)
    {
        Photo = photo;
        _set = set;
    }

    public PhotoId? Photo { get; }

    public Task SetAsync(PhotoId? photo, CancellationToken cancellationToken) => _set(photo, cancellationToken);

    public static async Task<PhotoTarget?> FindAsync(
        Guid ownerId,
        PhotoOwner owner,
        TenantId tenant,
        IProductFamilyRepository families,
        IProductRepository products,
        CancellationToken cancellationToken)
    {
        if (owner == PhotoOwner.Family)
        {
            var family = await families.GetAsync(new FamilyId(ownerId), tenant, cancellationToken);
            return family is null
                ? null
                : new PhotoTarget(family.PhotoId, (photo, ct) =>
                {
                    family.SetPhoto(photo);
                    return families.UpdateAsync(family, ct);
                });
        }

        var product = await products.GetAsync(new ProductId(ownerId), tenant, cancellationToken);
        return product is null
            ? null
            : new PhotoTarget(product.PhotoId, (photo, ct) =>
            {
                product.SetPhoto(photo);
                return products.UpdateAsync(product, ct);
            });
    }
}
