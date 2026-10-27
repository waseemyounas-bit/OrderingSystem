import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, concatMap, from, map, of, switchMap, timer } from 'rxjs';
import { newCorrelationId } from '../../core/activity-log';
import { CircuitState, DemoProducts } from '../../core/models';
import { OrderingApi, describeError } from '../../core/ordering-api';

interface ProbeResult {
  attempt: number;
  ok: boolean;
  durationMs: number;
  message: string;
}

/**
 * CIRCUIT BREAKER LAB
 *  1. Turn chaos ON: the Product Service starts returning HTTP 500.
 *  2. Send test orders: the Order Service retries (slow, ~1s), failures pile up, and the circuit OPENS.
 *  3. Once it's open, orders fail in a few milliseconds without calling the Product Service (fail fast).
 *  4. Turn chaos OFF and wait 20s: HALF-OPEN, one trial call succeeds, and the circuit is CLOSED again.
 */
@Component({
  selector: 'app-chaos-lab',
  templateUrl: './chaos-lab.html',
})
export class ChaosLabPage implements OnInit {
  private readonly api = inject(OrderingApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly chaosEnabled = signal(false);
  protected readonly circuit = signal<CircuitState | 'Unknown'>('Unknown');
  protected readonly results = signal<ProbeResult[]>([]);
  protected readonly running = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.getChaos().subscribe({
      next: (c) => this.chaosEnabled.set(c.enabled),
      error: (err) => this.error.set(describeError(err)),
    });

    // Live circuit state, refreshed every 2 seconds.
    timer(0, 2000)
      .pipe(
        switchMap(() => this.api.getCircuitState(true).pipe(catchError(() => of(null)))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.circuit.set(state?.productServiceCircuit ?? 'Unknown'));
  }

  protected toggleChaos(): void {
    this.api.setChaos({ enabled: !this.chaosEnabled(), delayMs: 0 }).subscribe({
      next: (c) => this.chaosEnabled.set(c.enabled),
      error: (err) => this.error.set(describeError(err)),
    });
  }

  /** Sends orders ONE AFTER ANOTHER (concatMap) so you can watch the breaker change state. */
  protected sendOrders(count: number): void {
    this.running.set(true);
    this.results.set([]);

    from(Array.from({ length: count }, (_, i) => i + 1))
      .pipe(
        concatMap((attempt) => {
          const started = performance.now();
          const elapsed = () => Math.round(performance.now() - started);
          return this.api
            .createOrder(
              { customerEmail: 'chaos@example.com', items: [{ productId: DemoProducts.phone, quantity: 1 }] },
              newCorrelationId(),
            )
            .pipe(
              map((r) => ({ attempt, ok: true, durationMs: elapsed(), message: `202 Accepted - order ${r.orderId.slice(0, 8)}` })),
              catchError((err) => of({ attempt, ok: false, durationMs: elapsed(), message: describeError(err) })),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => this.results.update((r) => [...r, result]),
        complete: () => this.running.set(false),
      });
  }
}
