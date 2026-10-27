import { Injectable, computed, signal } from '@angular/core';
import { Product } from './models';

export interface CartLine {
  product: Product;
  quantity: number;
}

/**
 * Shopping cart state using Angular SIGNALS: reading a signal in a template makes the view
 * update automatically when it changes. computed() values derive from other signals.
 *
 * Note: prices shown here are for display only. The Order Service re-reads the real price
 * from the Product Service - a backend must never trust prices sent by a browser.
 */
@Injectable({ providedIn: 'root' })
export class Cart {
  readonly lines = signal<CartLine[]>([]);
  readonly count = computed(() => this.lines().reduce((sum, l) => sum + l.quantity, 0));
  readonly total = computed(() => this.lines().reduce((sum, l) => sum + l.quantity * l.product.price, 0));

  add(product: Product, quantity = 1): void {
    this.lines.update((lines) => {
      const existing = lines.find((l) => l.product.id === product.id);
      return existing
        ? lines.map((l) => (l === existing ? { ...l, quantity: l.quantity + quantity } : l))
        : [...lines, { product, quantity }];
    });
  }

  setQuantity(productId: string, quantity: number): void {
    this.lines.update((lines) =>
      quantity <= 0
        ? lines.filter((l) => l.product.id !== productId)
        : lines.map((l) => (l.product.id === productId ? { ...l, quantity } : l)),
    );
  }

  clear(): void {
    this.lines.set([]);
  }
}
