import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** The API's password rules (ASP.NET Core Identity defaults, minimum length 8). */
export const PASSWORD_RULES = [
  { key: 'length', label: 'щонайменше 8 символів', test: (v: string) => v.length >= 8 },
  { key: 'lower', label: 'мала латинська літера', test: (v: string) => /[a-z]/.test(v) },
  { key: 'upper', label: 'велика латинська літера', test: (v: string) => /[A-Z]/.test(v) },
  { key: 'digit', label: 'цифра', test: (v: string) => /\d/.test(v) },
  { key: 'symbol', label: 'символ, напр. ! або #', test: (v: string) => /[^a-zA-Z0-9]/.test(v) },
] as const;

export const passwordRulesValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const value = String(control.value ?? '');
  const failed = PASSWORD_RULES.filter((rule) => !rule.test(value)).map((rule) => rule.key);
  return failed.length ? { passwordRules: failed } : null;
};
