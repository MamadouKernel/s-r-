using System.Globalization;
using System.Text;

namespace Soro.Application.Stock;

/// <summary>Les colonnes du fichier d'import, dans l'ordre du modèle (docs/specs/import-excel IMP-01).</summary>
public enum ImportColumn
{
    Name,
    Format,
    Category,
    Barcode,
    PurchasePrice,
    SalePrice,
    AlertThreshold,
    InitialStock,
    TracksStock,
    Unit,
}

public static class ImportColumns
{
    public static IReadOnlyList<ImportColumn> All { get; } = Enum.GetValues<ImportColumn>();

    /// <summary>Le titre tel qu'écrit dans le modèle et dans les messages.</summary>
    public static string Title(ImportColumn column) => column switch
    {
        ImportColumn.Name => "Nom du produit",
        ImportColumn.Format => "Format",
        ImportColumn.Category => "Catégorie",
        ImportColumn.Barcode => "Code-barres",
        ImportColumn.PurchasePrice => "Prix d'achat",
        ImportColumn.SalePrice => "Prix de vente",
        ImportColumn.AlertThreshold => "Alerte stock",
        ImportColumn.InitialStock => "Stock de départ",
        ImportColumn.TracksStock => "Suivi du stock",
        _ => "Unité",
    };

    private static readonly Dictionary<string, ImportColumn> Aliases = new()
    {
        ["nomduproduit"] = ImportColumn.Name, ["nom"] = ImportColumn.Name, ["produit"] = ImportColumn.Name, ["designation"] = ImportColumn.Name,
        ["format"] = ImportColumn.Format, ["taille"] = ImportColumn.Format, ["conditionnement"] = ImportColumn.Format,
        ["categorie"] = ImportColumn.Category,
        ["codebarres"] = ImportColumn.Barcode, ["codebarre"] = ImportColumn.Barcode, ["code"] = ImportColumn.Barcode, ["ean"] = ImportColumn.Barcode,
        ["prixdachat"] = ImportColumn.PurchasePrice, ["prixachat"] = ImportColumn.PurchasePrice, ["achat"] = ImportColumn.PurchasePrice,
        ["prixdevente"] = ImportColumn.SalePrice, ["prixvente"] = ImportColumn.SalePrice, ["prix"] = ImportColumn.SalePrice, ["vente"] = ImportColumn.SalePrice,
        ["alertestock"] = ImportColumn.AlertThreshold, ["alerte"] = ImportColumn.AlertThreshold, ["seuil"] = ImportColumn.AlertThreshold,
        ["seuildalerte"] = ImportColumn.AlertThreshold,
        ["stockdedepart"] = ImportColumn.InitialStock, ["stockdepart"] = ImportColumn.InitialStock, ["stockinitial"] = ImportColumn.InitialStock,
        ["stock"] = ImportColumn.InitialStock, ["quantite"] = ImportColumn.InitialStock,
        ["suividustock"] = ImportColumn.TracksStock, ["suivistock"] = ImportColumn.TracksStock, ["suivi"] = ImportColumn.TracksStock,
        ["unite"] = ImportColumn.Unit, ["unit"] = ImportColumn.Unit, ["vendupar"] = ImportColumn.Unit,
    };

    /// <summary>Reconnaît un en-tête sans tenir compte de la casse, des accents, des espaces ni des apostrophes.</summary>
    public static ImportColumn? Match(string header)
    {
        var key = TextKey.Compact(header);
        return Aliases.TryGetValue(key, out var column) ? column : null;
    }
}

/// <summary>Une ligne du fichier telle que lue : le texte de chaque cellule, numéro de ligne tel qu'affiché dans Excel.</summary>
public sealed record ImportRow(int Line, IReadOnlyDictionary<ImportColumn, string?> Cells)
{
    public string? Get(ImportColumn column) => Cells.TryGetValue(column, out var value) ? value : null;
}

/// <summary>Ce que le lecteur de classeur a trouvé : un problème de fichier (illisible, colonne manquante, trop long) ou les lignes.</summary>
public sealed record SheetRead(string? FileProblem, IReadOnlyList<ImportRow> Rows);

/// <summary>Lit un classeur .xlsx : valeurs seulement, aucune formule évaluée (docs/specs/import-excel IMP-08).</summary>
public interface IProductSheetReader
{
    SheetRead Read(byte[] file);
}

/// <summary>Produit le classeur modèle à remplir.</summary>
public interface IProductTemplateWriter
{
    byte[] Write();
}

/// <summary>Comparaisons de texte saisi à la main : sans accents, sans casse, sans espaces superflus.</summary>
public static class TextKey
{
    private const string Accented = "àáâãäåçèéêëìíîïñòóôõöùúûüýÿ";
    private const string Plain = "aaaaaaceeeeiiiinooooouuuuyy";

    /// <summary>
    /// Minuscules sans accents. Table écrite à la main plutôt que la normalisation Unicode : cette dernière ne fait rien quand le serveur tourne
    /// en mode « globalisation invariante » (cas courant en conteneur), et un accent oublié romprait la comparaison des noms.
    /// </summary>
    public static string Fold(string text)
    {
        var lower = text.ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);
        foreach (var character in lower)
        {
            var index = Accented.IndexOf(character, StringComparison.Ordinal);
            if (index >= 0)
            {
                builder.Append(Plain[index]);
            }
            else if (character == 'œ')
            {
                builder.Append("oe");
            }
            else if (character == 'æ')
            {
                builder.Append("ae");
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>Clé de regroupement : accents et casse ignorés, espaces multiples réduits.</summary>
    public static string Key(string text) => string.Join(' ', Fold(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Lettres et chiffres seulement : « Prix d'achat » et « prix d’achat » donnent la même clé.</summary>
    public static string Compact(string text) => new(Fold(text).Where(char.IsLetterOrDigit).ToArray());
}
