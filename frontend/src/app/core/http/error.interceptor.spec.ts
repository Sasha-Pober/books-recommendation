import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';

import { errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  const snackBar = { open: vi.fn() };

  beforeEach(() => {
    snackBar.open.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  it('reports server errors and passes them on', () => {
    let failed = false;
    http.get('/api/books').subscribe({ error: () => (failed = true) });

    backend.expectOne('/api/books').flush(null, { status: 500, statusText: 'Server Error' });

    expect(failed).toBe(true);
    expect(snackBar.open).toHaveBeenCalledOnce();
  });

  it('leaves client errors (e.g. validation) to the page', () => {
    http.get('/api/books').subscribe({ error: () => undefined });

    backend.expectOne('/api/books').flush(null, { status: 400, statusText: 'Bad Request' });

    expect(snackBar.open).not.toHaveBeenCalled();
  });
});
