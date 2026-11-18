// Short names for the API contracts generated from the OpenAPI document (`npm run api:types`).
import type { components } from './schema';

type Schemas = components['schemas'];

export type BookDto = Schemas['BookDto'];
export type AuthorDto = Schemas['AuthorDto'];
export type GenreDto = Schemas['GenreDto'];
export type PagedBooks = Schemas['PagedResultOfBookDto'];
export type RecommendationList = Schemas['RecommendationListDto'];
export type RecommendedBook = Schemas['RecommendedBookDto'];
