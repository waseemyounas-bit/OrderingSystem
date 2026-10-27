import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { tap } from 'rxjs';

export const CORRELATION_HEADER = 'X-Correlation-Id';

/** Set to true on a request to keep it out of the activity panel (used for 1-second polling). */
export const SKIP_ACTIVITY_LOG = new HttpContextToken<boolean>(() => false);

export interface ApiCall {
  method: string;
  url: string;
  status: number;
  durationMs: number;
  correlationId: string;
  at: Date;
}

/** Keeps the most recent API calls so the UI can show what the browser sent and how long it took. */
@Injectable({ providedIn: 'root' })
export class ActivityLog {
  readonly calls = signal<ApiCall[]>([]);

  add(call: ApiCall): void {
    this.calls.update((calls) => [call, ...calls].slice(0, 25));
  }

  clear(): void {
    this.calls.set([]);
  }
}

/**
 * HTTP INTERCEPTOR - runs for every HttpClient request.
 *
 * 1. Starts the correlation id HERE, in the browser (e.g. "web-k3j9x2a1"). The gateway keeps it,
 *    forwards it to the services, and they copy it onto RabbitMQ messages. One id for the whole flow.
 * 2. Records method, URL, status and duration in the ActivityLog.
 *
 * Search Seq for  CorrelationId = 'web-k3j9x2a1'  to see the logs from every service for that click.
 */
export const correlationInterceptor: HttpInterceptorFn = (req, next) => {
  const log = inject(ActivityLog);
  const correlationId = req.headers.get(CORRELATION_HEADER) ?? newCorrelationId();
  const started = performance.now();

  const record = (status: number) => {
    if (req.context.get(SKIP_ACTIVITY_LOG)) return;
    log.add({
      method: req.method,
      url: req.urlWithParams,
      status,
      durationMs: Math.round(performance.now() - started),
      correlationId,
      at: new Date(),
    });
  };

  return next(req.clone({ setHeaders: { [CORRELATION_HEADER]: correlationId } })).pipe(
    tap({
      next: (event) => {
        if (event instanceof HttpResponse) record(event.status);
      },
      error: (error: HttpErrorResponse) => record(error.status),
    }),
  );
};

export function newCorrelationId(): string {
  return 'web-' + Math.random().toString(36).slice(2, 10);
}
