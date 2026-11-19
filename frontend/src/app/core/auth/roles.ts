/** Role names as issued by the API (BookStore.Domain.Common.Roles). */
export const Role = {
  Customer: 'Customer',
  Manager: 'Manager',
  Admin: 'Admin',
} as const;

export type Role = (typeof Role)[keyof typeof Role];
