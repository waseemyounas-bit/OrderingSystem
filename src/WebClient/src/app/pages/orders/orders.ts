import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Order } from '../../core/models';
import { OrderingApi, describeError } from '../../core/ordering-api';

@Component({
  selector: 'app-orders',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  template: `
    <section class="card">
      <div class="row-between">
        <h2>Orders <small class="muted">Order Service &middot; orderdb</small></h2>
        <button class="secondary" (click)="load()">Refresh</button>
      </div>
      @if (error()) { <div class="alert error">{{ error() }}</div> }
      <table>
        <thead><tr><th>Created</th><th>Customer</th><th>Items</th><th>Total</th><th>Status</th><th></th></tr></thead>
        <tbody>
          @for (o of orders(); track o.id) {
            <tr>
              <td>{{ o.createdAt | date: 'short' }}</td>
              <td>{{ o.customerEmail }}</td>
              <td>{{ o.items.length }}</td>
              <td>{{ o.totalAmount | currency }}</td>
              <td><span class="status" [attr.data-status]="o.status">{{ o.status }}</span></td>
              <td><a [routerLink]="['/orders', o.id]">Saga &rarr;</a></td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="muted">No orders yet. Place one from the Shop.</td></tr>
          }
        </tbody>
      </table>
    </section>
  `,
})
export class OrdersPage implements OnInit {
  private readonly api = inject(OrderingApi);
  protected readonly orders = signal<Order[]>([]);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.api.getOrders().subscribe({
      next: (orders) => this.orders.set(orders),
      error: (err) => this.error.set(describeError(err)),
    });
  }
}
