import { HttpContext, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';

import type { RecommendationList, RecommendedBook } from '../../api/models';
import { CatalogReference } from '../../catalog/catalog-reference';
import { AuthService } from '../../core/auth/auth.service';
import { SILENT_ERRORS } from '../../core/http/error.interceptor';
import { RecommendationRow } from '../../shared/recommendation-row/recommendation-row';

const COUNT = 24;
const POPULAR_REASONS = new Set<RecommendedBook['reason']>(['Bestseller', 'Trending']);

interface Row {
  heading: string;
  items: RecommendedBook[];
}

/**
 * «Рекомендовано для вас» from the model (personal: the account's history, or a guest's views in this browser).
 * When the model has nothing to say (new visitor, or the recommendation service is down), the API returns
 * the non-personalized collections instead, shown as «Популярне зараз» and «Новинки».
 */
@Component({
  selector: 'app-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatChipsModule, MatIconModule, RecommendationRow, RouterLink],
  templateUrl: './home-page.html',
  styleUrl: './home-page.scss',
})
export class HomePage {
  protected readonly auth = inject(AuthService);
  protected readonly reference = inject(CatalogReference);

  /** Reloaded when the user signs in or out: the recommendations are theirs. */
  protected readonly recommendations = httpResource<RecommendationList>(() => {
    if (!this.auth.isReady()) {
      return undefined; // wait for the session, so a returning user doesn't first get a guest's list
    }
    // Read only to depend on it: a new request object (and so a reload) whenever the user changes.
    this.auth.user();
    return {
      url: '/api/recommendations/home',
      params: { count: COUNT },
      context: new HttpContext().set(SILENT_ERRORS, true),
    };
  });

  protected readonly personal = computed(
    () => this.recommendations.hasValue() && this.recommendations.value().source !== 'baseline',
  );

  protected readonly rows = computed<Row[]>(() => {
    if (!this.recommendations.hasValue()) {
      return [];
    }
    const items = this.recommendations.value().items;
    if (this.personal()) {
      return [{ heading: 'Рекомендовано для вас', items }];
    }
    return [
      { heading: 'Популярне зараз', items: items.filter((i) => POPULAR_REASONS.has(i.reason)) },
      { heading: 'Новинки', items: items.filter((i) => !POPULAR_REASONS.has(i.reason)) },
    ].filter((row) => row.items.length);
  });

  protected readonly topGenres = computed(() =>
    this.reference.genreOptions().filter((g) => g.depth === 0),
  );

  protected readonly greeting = computed(() => {
    const name = this.auth.user()?.fullName.split(' ')[0];
    return name ? `Вітаємо, ${name}!` : 'Книжки, дібрані для вас';
  });
}
