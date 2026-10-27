import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ActivityLog } from './core/activity-log';
import { Cart } from './core/cart';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe],
  templateUrl: './app.html',
})
export class App {
  protected readonly cart = inject(Cart);
  protected readonly activity = inject(ActivityLog);
  protected readonly showActivity = signal(true);
}
