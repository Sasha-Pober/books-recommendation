import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import type {
  AuthorDto,
  BookDto,
  GenreDto,
  ModelVersionDto,
  PagedAuthors,
  PublisherDto,
  SeriesDto,
  UpsertBookRequest,
} from '../api/models';

/** Catalog management (Manager, Admin) and model versions (Admin). */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  createBook(request: UpsertBookRequest): Observable<BookDto> {
    return this.http.post<BookDto>('/api/admin/books', request);
  }

  updateBook(id: string, request: UpsertBookRequest): Observable<BookDto> {
    return this.http.put<BookDto>(`/api/admin/books/${id}`, request);
  }

  /** 409 if the book has been ordered (it stays for order history; set its stock to 0 instead). */
  deleteBook(id: string): Observable<void> {
    return this.http.delete<void>(`/api/admin/books/${id}`);
  }

  searchAuthors(search: string, pageSize = 10): Observable<PagedAuthors> {
    return this.http.get<PagedAuthors>('/api/authors', { params: { search, pageSize } });
  }

  createAuthor(fullName: string): Observable<AuthorDto> {
    return this.http.post<AuthorDto>('/api/admin/authors', { fullName, bio: null });
  }

  createGenre(name: string, parentId: string | null): Observable<GenreDto> {
    return this.http.post<GenreDto>('/api/admin/genres', { name, parentId });
  }

  createPublisher(name: string): Observable<PublisherDto> {
    return this.http.post<PublisherDto>('/api/admin/publishers', { name });
  }

  createSeries(name: string): Observable<SeriesDto> {
    return this.http.post<SeriesDto>('/api/admin/series', { name });
  }

  /** The recommendation service loads the model first, then switches; 503 if it is unavailable. */
  activateModel(id: number): Observable<ModelVersionDto> {
    return this.http.post<ModelVersionDto>(`/api/admin/models/${id}/activate`, null);
  }
}
