using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Settings;
using Soro.Domain.Common;
using Soro.Domain.Customers;

namespace Soro.Application.Customers;

public sealed record CreateCustomerCommand(CustomerId Id, string Name, string? Phone, long? CreditCeilingFcfa);

public sealed record CustomerCreated(CustomerView Customer, bool AlreadyProcessed);

/// <summary>
/// Création d'un client (CRD-01). Un client se crée à la caisse : propriétaire, gérant et caissier. Seuls le propriétaire et le gérant choisissent le
/// plafond ; le caissier reçoit celui par défaut du commerce. Rejouable par identifiant (le client est créé sur l'appareil, possiblement renvoyé).
/// </summary>
public sealed class CreateCustomerHandler(
    ICurrentUser user,
    ICustomerRepository customers,
    IShopSettings settings,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<CustomerCreated>> HandleAsync(CreateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanOperateRegister(user.Role))
        {
            return Result<CustomerCreated>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas créer de client."));
        }

        if (command.CreditCeilingFcfa is not null && !RolePolicy.IsManager(user.Role))
        {
            return Result<CustomerCreated>.Failure(ApplicationError.Forbidden("Seuls le propriétaire et le gérant choisissent le plafond de crédit."));
        }

        var existing = await customers.GetAsync(command.Id, user.Tenant, cancellationToken);
        if (existing is not null)
        {
            return Result<CustomerCreated>.Success(new(CustomerView.From(existing), AlreadyProcessed: true));
        }

        try
        {
            var shop = await settings.GetAsync(user.Tenant, cancellationToken);
            var customer = Customer.Create(
                command.Id,
                user.Tenant,
                command.Name,
                command.Phone,
                Money.Fcfa(command.CreditCeilingFcfa ?? shop.DefaultCreditCeilingFcfa),
                clock.UtcNow);
            if (customer.Phone is { } phone && await customers.FindByPhoneAsync(user.Tenant, phone, cancellationToken) is not null)
            {
                return Result<CustomerCreated>.Failure(PhoneTaken(phone));
            }

            await customers.AddAsync(customer, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<CustomerCreated>.Success(new(CustomerView.From(customer), AlreadyProcessed: false));
        }
        catch (DomainException exception)
        {
            return Result<CustomerCreated>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }

    internal static ApplicationError PhoneTaken(PhoneNumber phone) =>
        new(ErrorCodes.PhoneTaken, $"Le numéro {phone.Masked} est déjà celui d'un autre client.");
}

public sealed record UpdateCustomerCommand(CustomerId Id, string Name, string? Phone, long CreditCeilingFcfa);

/// <summary>Modification du nom, du téléphone et du plafond (CRD-01) : propriétaire et gérant.</summary>
public sealed class UpdateCustomerHandler(ICurrentUser user, ICustomerRepository customers, IUnitOfWork unitOfWork)
{
    public async Task<Result<CustomerView>> HandleAsync(UpdateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.IsManager(user.Role))
        {
            return Result<CustomerView>.Failure(ApplicationError.Forbidden("Seuls le propriétaire et le gérant peuvent modifier un client."));
        }

        var customer = await customers.GetAsync(command.Id, user.Tenant, cancellationToken);
        if (customer is null)
        {
            return Result<CustomerView>.Failure(ApplicationError.NotFound("Client introuvable."));
        }

        try
        {
            // Le numéro est contrôlé avant de modifier le client : sinon on se comparerait à soi-même.
            if (PhoneNumber.TryParse(command.Phone, out var phone)
                && await customers.FindByPhoneAsync(user.Tenant, phone, cancellationToken) is { } other
                && other.Id != customer.Id)
            {
                return Result<CustomerView>.Failure(CreateCustomerHandler.PhoneTaken(phone));
            }

            customer.Edit(command.Name, command.Phone, Money.Fcfa(command.CreditCeilingFcfa));
            await customers.UpdateAsync(customer, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<CustomerView>.Success(CustomerView.From(customer));
        }
        catch (DomainException exception)
        {
            return Result<CustomerView>.Failure(ApplicationError.RuleViolation(exception.Message));
        }
    }
}

public sealed record DeactivateCustomerCommand(CustomerId Id);

/// <summary>Désactivation (CRD-01) : propriétaire et gérant, seulement sans dette. Le client disparaît des listes, son historique reste.</summary>
public sealed class DeactivateCustomerHandler(ICurrentUser user, ICustomerRepository customers, IUnitOfWork unitOfWork)
{
    public async Task<Result<CustomerView>> HandleAsync(DeactivateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.IsManager(user.Role))
        {
            return Result<CustomerView>.Failure(ApplicationError.Forbidden("Seuls le propriétaire et le gérant peuvent désactiver un client."));
        }

        var customer = await customers.GetAsync(command.Id, user.Tenant, cancellationToken);
        if (customer is null)
        {
            return Result<CustomerView>.Failure(ApplicationError.NotFound("Client introuvable."));
        }

        try
        {
            customer.Deactivate();
        }
        catch (DomainException exception)
        {
            return Result<CustomerView>.Failure(ApplicationError.RuleViolation(exception.Message));
        }

        await customers.UpdateAsync(customer, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<CustomerView>.Success(CustomerView.From(customer));
    }
}

public sealed record ListCustomersQuery(string? Search, bool IncludeInactive);

/// <summary>Liste des clients (CRD-03) : la caisse, le gérant et le comptable. Les clients désactivés ne se voient que sur demande, par ceux qui gèrent ou lisent les comptes.</summary>
public sealed class ListCustomersHandler(ICurrentUser user, ICustomerRepository customers)
{
    public async Task<Result<IReadOnlyList<CustomerView>>> HandleAsync(ListCustomersQuery query, CancellationToken cancellationToken = default)
    {
        if (!RolePolicy.CanReadCustomers(user.Role))
        {
            return Result<IReadOnlyList<CustomerView>>.Failure(ApplicationError.Forbidden("Ce profil ne peut pas consulter les clients."));
        }

        var includeInactive = query.IncludeInactive && RolePolicy.CanSeeRepayments(user.Role); // ceux qui gèrent ou lisent les comptes, pas la caisse
        var found = await customers.ListAsync(user.Tenant, query.Search?.Trim(), includeInactive, cancellationToken);
        IReadOnlyList<CustomerView> views = found.Select(CustomerView.From).ToArray();
        return Result<IReadOnlyList<CustomerView>>.Success(views);
    }
}
