import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { bookGuard } from './core/book/book.guard';
import { DeniedPage } from './features/denied/denied.page';
import { HomePage } from './features/home/home.page';

export const routes: Routes = [
  { path: '', component: HomePage, canActivate: [authGuard] },
  { path: 'denied', component: DeniedPage },
  {
    path: 'books/:bookId',
    canActivate: [authGuard, bookGuard],
    children: [
      {
        path: 'transactions',
        loadComponent: () => import('./features/transactions/transactions.page').then((m) => m.TransactionsPage),
      },
      {
        path: 'summary',
        loadComponent: () => import('./features/summary/summary.page').then((m) => m.SummaryPage),
      },
      { path: '', pathMatch: 'full', redirectTo: 'transactions' },
    ],
  },
  { path: '**', redirectTo: '' },
];
