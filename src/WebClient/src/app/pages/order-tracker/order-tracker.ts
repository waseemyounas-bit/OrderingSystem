import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { forkJoin, switchMap, takeWhile, timer } from 'rxjs';
import { Order, SagaState } from '../../core/models';
import { OrderingApi, describeError } from '../../core/ordering-api';

type StepState = 'pending' | 'active' | 'done' | 'failed' | 'skipped';

interface SagaStepView {
  label: string;
  service: string;
  state: StepState;
  compensation?: boolean;
}

@Component({
  selector: 'app-order-tracker',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './order-tracker.html',
})
export class OrderTrackerPage implements OnInit {
  private readonly api = inject(OrderingApi);
  private readonly destroyRef = inject(DestroyRef);

  /** Route parameter /orders/:id  and query parameter ?cid=... (bound by withComponentInputBinding). */
  readonly id = input.required<string>();
  readonly cid = input<string>();

  protected readonly order = signal<Order | null>(null);
  protected readonly saga = signal<SagaState | null>(null);
  protected readonly polling = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly seqUrl = computed(() =>
    `http://localhost:8081/#/events?filter=${encodeURIComponent(`CorrelationId = '${this.cid()}'`)}`);

  /** Derives a visual step-by-step picture from the saga's persisted state machine. */
  protected readonly steps = computed<SagaStepView[]>(() => {
    const saga = this.saga();
    if (!saga) return [];

    const step = saga.currentStep;
    const paymentFailed = saga.log.some((l) => l.step === 'Compensating');
    const stockFailed = step === 'Failed' && !paymentFailed;

    const steps: SagaStepView[] = [
      {
        label: 'Reserve stock',
        service: 'Product Service',
        state: step === 'AwaitingStockReservation' ? 'active' : stockFailed ? 'failed' : 'done',
      },
      {
        label: 'Process payment',
        service: 'Payment Service',
        state:
          step === 'AwaitingStockReservation' ? 'pending'
          : step === 'AwaitingPayment' ? 'active'
          : step === 'Completed' ? 'done'
          : paymentFailed ? 'failed'
          : 'skipped',
      },
    ];

    if (paymentFailed) {
      steps.push({
        label: 'Release stock (compensation)',
        service: 'Product Service',
        state: step === 'Compensating' ? 'active' : 'done',
        compensation: true,
      });
    }

    steps.push({
      label: step === 'Failed' ? 'Order cancelled' : 'Order confirmed',
      service: 'Order Service',
      state: step === 'Completed' ? 'done' : step === 'Failed' ? 'failed' : 'pending',
    });

    return steps;
  });

  ngOnInit(): void {
    // POLLING: ask every second until the order reaches a final status.
    // (Real systems can push updates instead, e.g. with SignalR/WebSockets.)
    timer(0, 1000)
      .pipe(
        switchMap(() => forkJoin({ order: this.api.getOrder(this.id(), true), saga: this.api.getSaga(this.id(), true) })),
        takeWhile(({ order }) => order.status !== 'Confirmed' && order.status !== 'Cancelled', true),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ order, saga }) => {
          this.order.set(order);
          this.saga.set(saga);
        },
        error: (err) => {
          this.error.set(describeError(err));
          this.polling.set(false);
        },
        complete: () => this.polling.set(false),
      });
  }
}
