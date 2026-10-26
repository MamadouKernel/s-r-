import { lineTotal } from '../core/measure';
import { CartLine, CatalogueProduct } from './types';

// Fonctions pures : le panier est une valeur, jamais modifiée sur place.

function assertQuantity(quantity: number): void {
  if (!Number.isInteger(quantity) || quantity < 1) {
    throw new Error('La quantité doit être un entier supérieur ou égal à 1.'); // en grammes ou millilitres pour un produit pesé
  }
}

/** Ajoute un produit ; une ligne existante est augmentée et garde son prix figé (RG-21). */
export function addProduct(cart: readonly CartLine[], product: CatalogueProduct, quantity = 1): CartLine[] {
  assertQuantity(quantity);
  const existing = cart.find((line) => line.productId === product.id);
  if (existing) {
    return cart.map((line) =>
      line.productId === product.id ? { ...line, quantity: line.quantity + quantity } : line,
    );
  }
  return [
    ...cart,
    {
      productId: product.id,
      name: product.name,
      unitPriceFcfa: product.salePriceFcfa,
      quantity,
      ...(product.unit && product.unit !== 'Piece' ? { unit: product.unit } : {}),
    },
  ];
}

export function setQuantity(cart: readonly CartLine[], productId: string, quantity: number): CartLine[] {
  assertQuantity(quantity);
  return cart.map((line) => (line.productId === productId ? { ...line, quantity } : line));
}

export function removeLine(cart: readonly CartLine[], productId: string): CartLine[] {
  return cart.filter((line) => line.productId !== productId);
}

export function cartTotal(cart: readonly CartLine[]): number {
  return cart.reduce((total, line) => total + lineTotal(line.unitPriceFcfa, line.quantity, line.unit), 0);
}
