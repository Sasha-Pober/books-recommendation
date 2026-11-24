// Short names for the API contracts generated from the OpenAPI document (`npm run api:types`).
import type { components } from './schema';

type Schemas = components['schemas'];

export type BookDto = Schemas['BookDto'];
export type AuthorDto = Schemas['AuthorDto'];
export type GenreDto = Schemas['GenreDto'];
export type PagedBooks = Schemas['PagedResultOfBookDto'];
export type RecommendationList = Schemas['RecommendationListDto'];
export type RecommendedBook = Schemas['RecommendedBookDto'];
export type UserDto = Schemas['UserDto'];
export type SessionResponse = Schemas['SessionResponse'];
export type LoginRequest = Schemas['LoginRequest'];
export type RegisterRequest = Schemas['RegisterRequest'];

/** RFC 7807 error body returned by the API (`detail` is safe to show for 4xx; `errors` for model validation). */
export interface ProblemDetails {
  status?: number;
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}
export type BookSort = NonNullable<Schemas['BookSort']>;
export type PublisherDto = Schemas['PublisherDto'];
export type SeriesDto = Schemas['SeriesDto'];
export type InteractionType = Schemas['InteractionType'];
export type InteractionEvent = Schemas['InteractionEvent'];
export type BookSummaryDto = Schemas['BookSummaryDto'];
export type CartDto = Schemas['CartDto'];
export type CartItemDto = Schemas['CartItemDto'];
export type WishlistItemDto = Schemas['WishlistItemDto'];
export type OrderDto = Schemas['OrderDto'];
export type OrderStatus = Schemas['OrderStatus'];
export type PagedOrders = Schemas['PagedResultOfOrderDto'];
export type ReviewDto = Schemas['ReviewDto'];
export type PagedReviews = Schemas['PagedResultOfReviewDto'];
export type PagedAuthors = Schemas['PagedResultOfAuthorDto'];
export type UpsertBookRequest = Schemas['UpsertBookRequest'];
export type ModelVersionDto = Schemas['ModelVersionDto'];
