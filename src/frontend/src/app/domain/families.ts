import { CatalogueProduct } from './types';

/** Une famille telle que la caisse l'affiche : un nom et ses formats (docs/specs/formats-produits FMT-06). */
export interface FamilyGroup {
  familyId: string;
  /** Nom de la famille. */
  name: string;
  /** Ce qu'on écrit sur la carte : le nom du format s'il n'en reste qu'un, sinon celui de la famille. */
  title: string;
  photoId: string | null;
  /** Formats du plus petit prix au plus grand. */
  formats: CatalogueProduct[];
}

/** Retire accents et casse pour comparer des textes saisis à la main. */
export function fold(text: string): string {
  return text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();
}

/**
 * Regroupe les produits par famille, dans l'ordre où chaque famille apparaît. Un produit sans famille (copie locale d'avant les formats)
 * est sa propre famille.
 */
export function groupByFamily(products: readonly CatalogueProduct[]): FamilyGroup[] {
  const groups = new Map<string, CatalogueProduct[]>();
  for (const product of products) {
    const key = product.familyId ?? product.id;
    const members = groups.get(key);
    if (members) {
      members.push(product);
    } else {
      groups.set(key, [product]);
    }
  }
  return [...groups].map(([familyId, members]) => {
    const formats = [...members].sort((a, b) => a.salePriceFcfa - b.salePriceFcfa || (a.formatLabel ?? '').localeCompare(b.formatLabel ?? '', 'fr', { numeric: true }));
    const name = formats[0].familyName ?? formats[0].name;
    return {
      familyId,
      name,
      title: formats.length === 1 ? formats[0].name : name,
      photoId: formats.find((format) => format.photoId)?.photoId ?? null,
      formats,
    };
  });
}

/**
 * Recherche à la caisse : chaque mot saisi doit se trouver dans le nom affiché (« Riz parfumé · 25 kg »), sans tenir compte des accents ni
 * de la casse ; un code-barres exact trouve aussi.
 */
export function matchesSearch(product: CatalogueProduct, term: string): boolean {
  const trimmed = term.trim();
  if (trimmed === '') {
    return true;
  }
  if (product.barcode === trimmed) {
    return true;
  }
  const name = fold(product.name);
  return fold(trimmed)
    .split(/\s+/)
    .every((word) => name.includes(word));
}

/** Initiales affichées quand un produit n'a pas de photo (deux lettres au plus). */
export function initialsOf(name: string): string {
  const words = name
    .split(/[\s·]+/)
    .map((word) => word.trim())
    .filter((word) => /\p{L}/u.test(word));
  if (words.length === 0) {
    return '?';
  }
  const letters = words.length === 1 ? [...words[0]].slice(0, 2).join('') : [...words[0]][0] + [...words[1]][0];
  return letters.toLocaleUpperCase('fr');
}
