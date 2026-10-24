// Types partagés du poste de caisse. Les montants sont des entiers en FCFA (RG-18).

export type PaymentMethod = 'Cash' | 'OrangeMoney' | 'MtnMomo' | 'MoovMoney' | 'Wave' | 'Credit';

export type MobileMoneyMethod = 'OrangeMoney' | 'MtnMomo' | 'MoovMoney' | 'Wave';

export const MOBILE_MONEY_METHODS: readonly { method: MobileMoneyMethod; label: string }[] = [
  { method: 'OrangeMoney', label: 'Orange Money' },
  { method: 'MtnMomo', label: 'MTN MoMo' },
  { method: 'MoovMoney', label: 'Moov Money' },
  { method: 'Wave', label: 'Wave' },
];

/** Produit tel que la caisse le connaît : copie locale du catalogue du serveur. */
export interface CatalogueProduct {
  id: string;
  name: string;
  barcode: string | null;
  category: string | null;
  salePriceFcfa: number;
  tracksStock: boolean;
  quantityOnHand: number;
}

/** Ligne de panier : nom et prix figés à l'ajout (RG-21). */
export interface CartLine {
  productId: string;
  name: string;
  unitPriceFcfa: number;
  quantity: number;
}

/**
 * Client tel que la caisse le connaît : copie locale de la liste du serveur, avec sa dette et son plafond au dernier chargement. Les ventes à crédit
 * faites hors ligne s'y ajoutent localement, pour que le plafond tienne d'une vente à l'autre sans réseau (docs/specs/clients-credit).
 */
export interface CustomerSnapshot {
  id: string;
  name: string;
  phone: string | null;
  creditCeilingFcfa: number;
  debtFcfa: number;
  availableCreditFcfa: number;
  isActive: boolean;
}

export interface PaymentDraft {
  id: string;
  method: PaymentMethod;
  amountFcfa: number;
  /** Espèces : montant remis par le client. */
  tenderedFcfa?: number;
  /** Mobile Money : référence de la transaction. */
  reference?: string;
}
