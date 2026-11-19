import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

import type { InteractionType } from '../../api/models';
import { SILENT_ERRORS } from '../http/error.interceptor';

/**
 * Reports what the visitor does with books (views; cart, wishlist and ratings record their own events on the
 * server) for the recommendation model. Best effort: failures are ignored, never shown to the user.
 */
@Injectable({ providedIn: 'root' })
export class InteractionEvents {
  private readonly http = inject(HttpClient);

  /**
   * @param recommendationLogId when the book was opened from a recommendation: marks that impression clicked
   *        (CTR in the A/B comparison).
   */
  record(bookId: string, type: InteractionType, recommendationLogId?: number | null): void {
    const event = { bookId, type, ...(recommendationLogId ? { recommendationLogId } : {}) };
    this.http
      .post(
        '/api/events',
        { events: [event] },
        { context: new HttpContext().set(SILENT_ERRORS, true), keepalive: true },
      )
      .subscribe({ error: () => undefined });
  }
}
