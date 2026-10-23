import { addProduct, cartTotal, removeLine, setQuantity } from './cart';
import { CatalogueProduct } from './types';

const jus: CatalogueProduct = {
  id: 'p-jus', name: "Jus d'orange 1L", barcode: '111', category: 'Boissons',
  salePriceFcfa: 600, tracksStock: true, quantityOnHand: 10,
};
const savon: CatalogueProduct = {
  id: 'p-savon', name: 'Savon', barcode: '222', category: null,
  salePriceFcfa: 250, tracksStock: true, quantityOnHand: 4,
};

describe('cart', () => {
  it('adds a product with its name and price frozen', () => {
    const cart = addProduct([], jus);
    expect(cart).toEqual([{ productId: 'p-jus', name: "Jus d'orange 1L", unitPriceFcfa: 600, quantity: 1 }]);
  });

  it('adds the same product to the existing line instead of duplicating it', () => {
    const cart = addProduct(addProduct([], jus), jus, 2);
    expect(cart).toHaveLength(1);
    expect(cart[0].quantity).toBe(3);
  });

  it('keeps the frozen price when the catalogue price changes afterwards', () => {
    const cart = addProduct([], jus);
    const repriced = addProduct(cart, { ...jus, salePriceFcfa: 700 });
    expect(repriced[0].unitPriceFcfa).toBe(600);
  });

  it('does not mutate the previous cart', () => {
    const before = addProduct([], jus);
    addProduct(before, savon);
    expect(before).toHaveLength(1);
  });

  it('sets an explicit quantity and rejects a quantity below one', () => {
    const cart = addProduct([], jus);
    expect(setQuantity(cart, 'p-jus', 5)[0].quantity).toBe(5);
    expect(() => setQuantity(cart, 'p-jus', 0)).toThrow();
    expect(() => setQuantity(cart, 'p-jus', 1.5)).toThrow();
  });

  it('removes a line', () => {
    const cart = addProduct(addProduct([], jus), savon);
    expect(removeLine(cart, 'p-jus').map((line) => line.productId)).toEqual(['p-savon']);
  });

  it('totals price times quantity', () => {
    const cart = addProduct(addProduct([], jus, 2), savon, 3);
    expect(cartTotal(cart)).toBe(2 * 600 + 3 * 250);
    expect(cartTotal([])).toBe(0);
  });

  it('rejects adding a product without a positive integer quantity', () => {
    expect(() => addProduct([], jus, 0)).toThrow();
    expect(() => addProduct([], jus, -1)).toThrow();
  });
});
