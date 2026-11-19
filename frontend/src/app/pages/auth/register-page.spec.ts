import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { session } from '../../../testing/session';
import { passwordRulesValidator } from './password-rules';
import { RegisterPage } from './register-page';

describe('RegisterPage', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [RegisterPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  function fill(element: HTMLElement, name: string, value: string): void {
    const input = element.querySelector<HTMLInputElement>(`input[formcontrolname=${name}]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('checks the same password rules as the API', () => {
    const check = (value: string) => passwordRulesValidator({ value } as never);

    expect(check('Reader123!')).toBeNull();
    expect(check('reader123!')).toEqual({ passwordRules: ['upper'] });
    expect(check('Короткий1')).toEqual({ passwordRules: ['lower', 'upper'] }); // Cyrillic counts as a symbol
  });

  it('registers and returns to the page the user came from', async () => {
    const fixture = TestBed.createComponent(RegisterPage);
    fixture.componentRef.setInput('returnUrl', '/catalog');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const element: HTMLElement = fixture.nativeElement;
    await fixture.whenStable();

    fill(element, 'fullName', ' Читач ');
    fill(element, 'email', 'reader@example.com');
    fill(element, 'password', 'Reader123!');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    const request = TestBed.inject(HttpTestingController).expectOne('/api/auth/register');
    expect(request.request.body).toEqual({
      fullName: 'Читач',
      email: 'reader@example.com',
      password: 'Reader123!',
    });
    request.flush(session());
    expect(navigate).toHaveBeenCalledWith('/catalog');
  });

  it('explains a taken email', async () => {
    const fixture = TestBed.createComponent(RegisterPage);
    const element: HTMLElement = fixture.nativeElement;
    await fixture.whenStable();

    fill(element, 'fullName', 'Читач');
    fill(element, 'email', 'taken@example.com');
    fill(element, 'password', 'Reader123!');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    TestBed.inject(HttpTestingController)
      .expectOne('/api/auth/register')
      .flush({ status: 409 }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();

    expect(element.querySelector('[role=alert]')?.textContent).toContain('уже існує');
  });
});
