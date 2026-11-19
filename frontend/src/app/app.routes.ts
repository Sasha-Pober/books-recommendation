import { Routes } from '@angular/router';

import { authGuard, guestOnlyGuard, roleGuard } from './core/auth/auth.guards';
import { Role } from './core/auth/roles';

export const routes: Routes = [
  {
    path: '',
    title: 'Книгарня',
    loadComponent: () => import('./pages/home/home-page').then((m) => m.HomePage),
  },
  {
    path: 'catalog',
    title: 'Каталог — Книгарня',
    loadComponent: () => import('./pages/catalog/catalog-page').then((m) => m.CatalogPage),
  },
  {
    path: 'books/:id',
    title: 'Книгарня',
    loadComponent: () => import('./pages/book/book-page').then((m) => m.BookPage),
  },
  {
    path: 'login',
    title: 'Вхід — Книгарня',
    canActivate: [guestOnlyGuard],
    loadComponent: () => import('./pages/auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'register',
    title: 'Реєстрація — Книгарня',
    canActivate: [guestOnlyGuard],
    loadComponent: () => import('./pages/auth/register-page').then((m) => m.RegisterPage),
  },
  {
    path: 'account',
    title: 'Мій акаунт — Книгарня',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/account/account-page').then((m) => m.AccountPage),
  },
  {
    path: 'cart',
    title: 'Кошик — Книгарня',
    canActivate: [roleGuard(Role.Customer)],
    loadComponent: () => import('./pages/cart/cart-page').then((m) => m.CartPage),
  },
  {
    path: 'wishlist',
    title: 'Список бажань — Книгарня',
    canActivate: [roleGuard(Role.Customer)],
    loadComponent: () => import('./pages/wishlist/wishlist-page').then((m) => m.WishlistPage),
  },
  {
    path: 'orders',
    title: 'Мої замовлення — Книгарня',
    canActivate: [roleGuard(Role.Customer)],
    loadComponent: () => import('./pages/orders/orders-page').then((m) => m.OrdersPage),
  },
  {
    path: 'orders/:id',
    title: 'Замовлення — Книгарня',
    canActivate: [roleGuard(Role.Customer)],
    loadComponent: () => import('./pages/orders/order-page').then((m) => m.OrderPage),
  },
  {
    path: '**',
    title: 'Сторінку не знайдено — Книгарня',
    loadComponent: () => import('./pages/not-found/not-found-page').then((m) => m.NotFoundPage),
  },
];
