import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Payment } from '../../core/models';
import { OrderingApi, describeError } from '../../core/ordering-api';

@Component({
  selector: 'app-payments',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  template: `
    <section class="card">
      <div class="row-between">
        <h2>Payments <small class="muted">Payment Service &middot; paymentdb</small></h2>
        <button class="secondary" (click)="load()">Refresh</button>
      </div>
      <p class="hint">
        There is no "create payment" API. Payments are only created by the saga, through a
        ProcessPaymentCommand sent over RabbitMQ.
      </p>
      @if (error()) { <div class="alert error">{{ error() }}</div> }
      <table>
        <thead><tr><th>Created</th><th>Order</th><th>Customer</th><th>Amount</th><th>Status</th><th>Reason</th></tr></thead>
        <tbody>
          @for (p of payments(); track p.id) {
            <tr>
              <td>{{ p.createdAt | date: 'short' }}</td>
              <td><a [routerLink]="['/orders', p.orderId]"><code>{{ p.orderId.slice(0, 8) }}</code></a></td>
              <td>{{ p.customerEmail }}</td>
              <td>{{ p.amount | currency }}</td>
              <td><span class="status" [attr.data-status]="p.status">{{ p.status }}</span></td>
              <td class="muted">{{ p.declineReason }}</td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="muted">No payments yet.</td></tr>
          }
        </tbody>
      </table>
    </section>
  `,
})
export class PaymentsPage implements OnInit {
  private readonly api = inject(OrderingApi);
  protected readonly payments = signal<Payment[]>([]);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.api.getPayments().subscribe({
      next: (payments) => this.payments.set(payments),
      error: (err) => this.error.set(describeError(err)),
    });
  }
}
