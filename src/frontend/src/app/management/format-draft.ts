import { MeasureUnit, parseThreshold } from '../core/measure';
import { parseFcfa } from '../core/money';
import { fold } from '../domain/families';
import { FormatInput } from './products-api';

/** Un format en cours de saisie : tout est du texte tel que tapé, la vérification vient à l'enregistrement. */
export interface FormatDraft {
  /** Clé de l'écran (suivi de la liste) ; sans lien avec le serveur. */
  key: string;
  /** Identifiant du produit, fixé dès l'ouverture : un nouvel essai après une coupure ne crée pas un second format. */
  productId: string;
  label: string;
  barcode: string;
  purchasePrice: string;
  salePrice: string;
  threshold: string;
  tracksStock: boolean;
  /** Pièce, kg ou litre : se choisit à la création et ne change plus ensuite. */
  unit: MeasureUnit;
}

export interface FormatErrors {
  label?: string;
  barcode?: string;
  salePrice?: string;
  purchasePrice?: string;
  threshold?: string;
}

export function newDraft(): FormatDraft {
  const id = crypto.randomUUID();
  return { key: id, productId: id, label: '', barcode: '', purchasePrice: '0', salePrice: '', threshold: '0', tracksStock: true, unit: 'Piece' };
}

function cleanLabel(label: string): string {
  return label.split(/\s+/).filter(Boolean).join(' ');
}

/**
 * Vérifie la liste des formats d'une famille. `inputs` n'est rempli que si tout est valide ; sinon `errors` (une entrée par format) dit quoi
 * corriger, sous le champ concerné. Le serveur applique les mêmes règles. `labelRequired` : le format est ajouté à une famille qui en a déjà un.
 */
export function validateFormats(drafts: readonly FormatDraft[], labelRequired = drafts.length > 1): { errors: FormatErrors[]; inputs: FormatInput[] | null; general: string } {
  const errors: FormatErrors[] = drafts.map(() => ({}));
  const inputs: FormatInput[] = [];
  const seenLabels = new Set<string>();
  const seenBarcodes = new Set<string>();
  let valid = drafts.length > 0;

  drafts.forEach((draft, index) => {
    const error = errors[index];
    const label = cleanLabel(draft.label);
    const barcode = draft.barcode.trim();
    const sale = parseFcfa(draft.salePrice);
    const purchase = parseFcfa(draft.purchasePrice);
    const threshold = parseThreshold(draft.threshold, draft.unit);

    if (labelRequired && label === '') {
      error.label = 'Donnez un format à chaque produit (1 L, 50 kg…).';
    } else if (label !== '' && seenLabels.has(fold(label))) {
      error.label = `Le format « ${label} » existe déjà dans ce produit.`;
    }
    seenLabels.add(fold(label));

    if (barcode !== '' && seenBarcodes.has(barcode)) {
      error.barcode = 'Ce code-barres est déjà utilisé par un autre format de la liste.';
    }
    seenBarcodes.add(barcode);

    if (sale === null) {
      error.salePrice = 'Saisissez le prix de vente en chiffres, sans virgule.';
    }
    if (purchase === null) {
      error.purchasePrice = 'Saisissez le prix d’achat en chiffres, sans virgule (0 s’il est inconnu).';
    }
    if (!threshold.ok) {
      error.threshold = threshold.message;
    }

    if (Object.keys(error).length > 0 || sale === null || purchase === null || !threshold.ok) {
      valid = false;
      return;
    }
    inputs.push({
      productId: draft.productId,
      label: label === '' ? null : label,
      barcode: barcode === '' ? null : barcode,
      purchasePriceFcfa: purchase,
      salePriceFcfa: sale,
      alertThreshold: threshold.quantity,
      tracksStock: draft.tracksStock,
      unit: draft.unit,
    });
  });

  return { errors, inputs: valid ? inputs : null, general: drafts.length === 0 ? 'Ajoutez au moins un format.' : '' };
}
