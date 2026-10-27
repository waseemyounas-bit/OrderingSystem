import { Routes } from '@angular/router';
import { ChaosLabPage } from './pages/chaos-lab/chaos-lab';
import { OrderTrackerPage } from './pages/order-tracker/order-tracker';
import { OrdersPage } from './pages/orders/orders';
import { PaymentsPage } from './pages/payments/payments';
import { ShopPage } from './pages/shop/shop';

export const routes: Routes = [
  { path: '', component: ShopPage, title: 'Shop' },
  { path: 'orders', component: OrdersPage, title: 'Orders' },
  { path: 'orders/:id', component: OrderTrackerPage, title: 'Order tracker' },
  { path: 'payments', component: PaymentsPage, title: 'Payments' },
  { path: 'chaos-lab', component: ChaosLabPage, title: 'Circuit breaker lab' },
  { path: '**', redirectTo: '' },
];
