import { ChangeDetectionStrategy, Component, computed, input, linkedSignal } from '@angular/core';

export interface CoverBook {
  id: string;
  title: string;
  coverImageUrl: string | null;
  authors: readonly (string | { fullName: string })[];
}

/** Cover backgrounds for books without an image: muted, with white text readable on all of them. */
const PALETTE = [
  '#3e5c76',
  '#6b4f7a',
  '#2f6f62',
  '#8a5a44',
  '#4a5d8f',
  '#7a4b55',
  '#55704a',
  '#6a6150',
];

/**
 * The book's cover image; if there is none, or it fails to load, a generated cover with the title and author.
 * Covers are 2:3 and fill the width of their container.
 */
@Component({
  selector: 'app-book-cover',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.generated]': '!showImage()' },
  template: `
    @if (showImage()) {
      <img
        [src]="book().coverImageUrl"
        [alt]="'Обкладинка: ' + book().title"
        loading="lazy"
        decoding="async"
        referrerpolicy="no-referrer"
        (error)="failed.set(true)"
      />
    } @else {
      <div
        class="generated"
        [style.background-color]="color()"
        role="img"
        [attr.aria-label]="'Обкладинка: ' + book().title"
      >
        <span class="title">{{ book().title }}</span>
        @if (author(); as name) {
          <span class="author">{{ name }}</span>
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      aspect-ratio: 2 / 3;
      overflow: hidden;
      border-radius: 6px;
      background: var(--mat-sys-surface-container-high);
      box-shadow: var(--mat-sys-level1);
    }

    img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }

    .generated {
      height: 100%;
      box-sizing: border-box;
      padding: 14% 10%;
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      color: #fff;
      text-align: center;
      background-image: linear-gradient(160deg, rgb(255 255 255 / 0.14), transparent 55%);
    }

    .title {
      font:
        500 clamp(0.8rem, 1.1vw + 0.4rem, 1.15rem) / 1.25 Georgia,
        'Times New Roman',
        serif;
      display: -webkit-box;
      -webkit-line-clamp: 5;
      -webkit-box-orient: vertical;
      overflow: hidden;
      overflow-wrap: anywhere;
    }

    .author {
      font: var(--mat-sys-label-medium);
      opacity: 0.85;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
})
export class BookCover {
  /** A full book or a summary (whose authors are plain names). */
  readonly book = input.required<CoverBook>();

  /** Reset whenever the book changes, so one broken image doesn't hide the next book's cover. */
  protected readonly failed = linkedSignal({ source: this.book, computation: () => false });
  protected readonly showImage = computed(() => !!this.book().coverImageUrl && !this.failed());
  protected readonly author = computed(() => {
    const first = this.book().authors[0];
    return typeof first === 'string' ? first : first?.fullName;
  });
  protected readonly color = computed(() => PALETTE[hash(this.book().id) % PALETTE.length]);
}

function hash(text: string): number {
  let value = 0;
  for (let i = 0; i < text.length; i++) {
    value = (value * 31 + text.charCodeAt(i)) >>> 0;
  }
  return value;
}
