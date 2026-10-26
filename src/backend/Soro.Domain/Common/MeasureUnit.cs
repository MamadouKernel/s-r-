namespace Soro.Domain.Common;

/// <summary>
/// Unité de vente d'un produit (docs/decisions/0017). Les quantités restent des entiers : pour un produit au kilogramme ou au litre, la quantité
/// est comptée en <b>millièmes</b> (grammes, millilitres) ; pour une pièce, telle quelle. Aucun calcul décimal n'entre dans l'argent ni dans le stock.
/// </summary>
public enum MeasureUnit
{
    Piece,
    Kilogram,
    Litre,
}

public static class MeasureUnits
{
    /// <summary>Nombre d'unités stockées pour une unité vendue : 1 000 grammes font un kilogramme.</summary>
    public static int Scale(this MeasureUnit unit) => unit == MeasureUnit.Piece ? 1 : 1_000;

    /// <summary>« kg », « L » ; rien pour une pièce.</summary>
    public static string Symbol(this MeasureUnit unit) => unit switch
    {
        MeasureUnit.Kilogram => "kg",
        MeasureUnit.Litre => "L",
        _ => string.Empty,
    };

    /// <summary>Le mot affiché dans les fichiers : « pièce », « kg », « litre ».</summary>
    public static string Label(this MeasureUnit unit) => unit switch
    {
        MeasureUnit.Kilogram => "kg",
        MeasureUnit.Litre => "litre",
        _ => "pièce",
    };
}
