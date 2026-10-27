import { HttpClient, HttpContext, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CORRELATION_HEADER, SKIP_ACTIVITY_LOG } from './activity-log';
import {
  ChaosSettings,
  CircuitState,
  CreateOrderAccepted,
  CreateOrderRequest,
  Order,
  Payment,
  ProblemDetails,
  Product,
  SagaState,
} from './models';

/**
 * Every call goes to a RELATIVE url ("/api/..."), i.e. the same host that served the Angular app:
 *   - ng serve      -> proxy.conf.json forwards /api to the gateway (http://localhost:5000)
 *   - Docker/K8s    -> nginx forwards /api to the gateway (http://api-gateway:8080)
 * The browser only ever talks to ONE origin, so no CORS configuration is needed, and the
 * gateway stays the single entry point to the microservices.
 */
@Injectable({ providedIn: 'root' })
export class OrderingApi {
  private readonly http = inject(HttpClient);

  // ---------- Product Service ----------
  getProducts(): Observable<Product[]> {
    return this.http.get<Product[]>('/api/products');
  }

  getChaos(): Observable<ChaosSettings> {
    return this.http.get<ChaosSettings>('/api/products/chaos');
  }

  setChaos(settings: ChaosSettings): Observable<ChaosSettings> {
    return this.http.post<ChaosSettings>('/api/products/chaos', settings);
  }

  // ---------- Order Service ----------
  /** Returns 202 Accepted. Pass a correlation id to follow this order through the logs. */
  createOrder(request: CreateOrderRequest, correlationId: string): Observable<CreateOrderAccepted> {
    return this.http.post<CreateOrderAccepted>('/api/orders', request, {
      headers: new HttpHeaders({ [CORRELATION_HEADER]: correlationId }),
    });
  }

  getOrders(): Observable<Order[]> {
    return this.http.get<Order[]>('/api/orders');
  }

  getOrder(id: string, polling = false): Observable<Order> {
    return this.http.get<Order>(`/api/orders/${id}`, { context: this.pollingContext(polling) });
  }

  getSaga(orderId: string, polling = false): Observable<SagaState> {
    return this.http.get<SagaState>(`/api/orders/${orderId}/saga`, { context: this.pollingContext(polling) });
  }

  getCircuitState(polling = false): Observable<{ productServiceCircuit: CircuitState }> {
    return this.http.get<{ productServiceCircuit: CircuitState }>('/api/orders/circuit-breaker', {
      context: this.pollingContext(polling),
    });
  }

  // ---------- Payment Service ----------
  getPayments(): Observable<Payment[]> {
    return this.http.get<Payment[]>('/api/payments');
  }

  private pollingContext(polling: boolean): HttpContext {
    return new HttpContext().set(SKIP_ACTIVITY_LOG, polling);
  }
}

/** Turns any HTTP error into one readable sentence for the UI. */
export function describeError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return String(error);

  if (error.status === 0) return 'Cannot reach the API gateway. Is the backend running?';
  if (error.status === 429) return 'HTTP 429 - rate limit hit at the API gateway. Wait a few seconds.';

  const problem = error.error as ProblemDetails | null;
  const validation = problem?.errors ? Object.values(problem.errors).flat().join(' ') : '';
  const text = problem?.detail ?? (validation || problem?.title || error.message);
  return `HTTP ${error.status} - ${text}`;
}
