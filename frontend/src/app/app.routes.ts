import { Routes } from '@angular/router';

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
    path: '**',
    title: 'Сторінку не знайдено — Книгарня',
    loadComponent: () => import('./pages/not-found/not-found-page').then((m) => m.NotFoundPage),
  },
];
