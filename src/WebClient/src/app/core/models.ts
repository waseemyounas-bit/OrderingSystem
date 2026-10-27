// TypeScript shapes of the JSON returned by the backend (through the API gateway).
// ASP.NET Core serializes C# PascalCase properties as camelCase, and enums as strings.

export interface Product {
  id: string;
  name: string;
  description: string;
  price: number;
  stockQuantity: number;
}

export type OrderStatus = 'Pending' | 'StockReserved' | 'Confirmed' | 'Cancelled';

export interface OrderItem {
  id: string;
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
}

export interface Order {
  id: string;
  customerEmail: string;
  status: OrderStatus;
  totalAmount: number;
  failureReason: string | null;
  createdAt: string;
  updatedAt: string;
  items: OrderItem[];
}

export interface CreateOrderRequest {
  customerEmail: string;
  items: { productId: string; quantity: number }[];
}

/** Body of the 202 Accepted response: the order is NOT final yet, the saga runs asynchronously. */
export interface CreateOrderAccepted {
  orderId: string;
  status: OrderStatus;
  totalAmount: number;
  message: string;
}

export type SagaStep = 'AwaitingStockReservation' | 'AwaitingPayment' | 'Compensating' | 'Completed' | 'Failed';

export interface SagaLogEntry {
  timestamp: string;
  step: SagaStep;
  message: string;
}

export interface SagaState {
  orderId: string;
  currentStep: SagaStep;
  startedAt: string;
  updatedAt: string;
  log: SagaLogEntry[];
}

export interface Payment {
  id: string;
  orderId: string;
  customerEmail: string;
  amount: number;
  status: 'Approved' | 'Declined';
  declineReason: string | null;
  createdAt: string;
}

export interface ChaosSettings {
  enabled: boolean;
  delayMs: number;
}

export type CircuitState = 'Closed' | 'Open' | 'HalfOpen' | 'Isolated';

/** ASP.NET Core ProblemDetails - the standard error body (RFC 9457). */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

/** Seeded product ids (see ProductSeeder.cs) used by the one-click demo scenarios. */
export const DemoProducts = {
  laptop: '11111111-1111-1111-1111-111111111111',
  phone: '22222222-2222-2222-2222-222222222222',
  headphones: '33333333-3333-3333-3333-333333333333',
  watch: '44444444-4444-4444-4444-444444444444',
} as const;
