import { CurrencyPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { newCorrelationId } from '../../core/activity-log';
import { Cart } from '../../core/cart';
import { CreateOrderRequest, DemoProducts, Product } from '../../core/models';
import { OrderingApi, describeError } from '../../core/ordering-api';

interface Scenario {
  title: string;
  explains: string;
  request: CreateOrderRequest;
}

@Component({
  selector: 'app-shop',
  imports: [CurrencyPipe, FormsModule],
  templateUrl: './shop.html',
})
export class ShopPage implements OnInit {
  private readonly api = inject(OrderingApi);
  private readonly router = inject(Router);
  protected readonly cart = inject(Cart);

  protected readonly products = signal<Product[]>([]);
  protected readonly loading = signal(true);
  protected readonly placing = signal(false);
  protected readonly error = signal<string | null>(null);
  protected email = 'student@example.com';

  /** One-click orders that drive the saga down each of its paths. */
  protected readonly scenarios: Scenario[] = [
    {
      title: 'Happy path',
      explains: '2 x Headphones (259.98). Stock reserved, payment approved, order CONFIRMED.',
      request: { customerEmail: 'alice@example.com', items: [{ productId: DemoProducts.headphones, quantity: 2 }] },
    },
    {
      title: 'Payment fails, compensation runs',
      explains: '1 x Laptop (1499 > 1000 limit). Stock reserved, payment DECLINED, stock RELEASED, order CANCELLED.',
      request: { customerEmail: 'bob@example.com', items: [{ productId: DemoProducts.laptop, quantity: 1 }] },
    },
    {
      title: 'Out of stock',
      explains: '5 x Limited Watch (only 2 in stock). Reservation fails at step 1, nothing to compensate.',
      request: { customerEmail: 'carol@example.com', items: [{ productId: DemoProducts.watch, quantity: 5 }] },
    },
  ];

  ngOnInit(): void {
    this.loadProducts();
  }

  protected loadProducts(): void {
    this.loading.set(true);
    this.api.getProducts().subscribe({
      next: (products) => {
        this.products.set(products);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(describeError(err));
        this.loading.set(false);
      },
    });
  }

  protected checkout(): void {
    this.placeOrder({
      customerEmail: this.email,
      items: this.cart.lines().map((l) => ({ productId: l.product.id, quantity: l.quantity })),
    }, true);
  }

  protected runScenario(scenario: Scenario): void {
    this.placeOrder(scenario.request, false);
  }

  /**
   * POST /api/orders returns 202 ACCEPTED: the order exists but its outcome is not known yet.
   * We navigate to the tracker page, which polls until the saga finishes.
   */
  private placeOrder(request: CreateOrderRequest, fromCart: boolean): void {
    const correlationId = newCorrelationId();
    this.placing.set(true);
    this.error.set(null);

    this.api.createOrder(request, correlationId).subscribe({
      next: (accepted) => {
        if (fromCart) this.cart.clear();
        this.placing.set(false);
        this.router.navigate(['/orders', accepted.orderId], { queryParams: { cid: correlationId } });
      },
      error: (err) => {
        // e.g. 503 when the circuit breaker is OPEN, 400 for validation, 429 for the rate limiter
        this.error.set(describeError(err));
        this.placing.set(false);
      },
    });
  }
}
