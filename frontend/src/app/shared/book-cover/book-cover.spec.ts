import { TestBed } from '@angular/core/testing';

import { BookCover } from './book-cover';

describe('BookCover', () => {
  const book = {
    id: 'b1',
    title: 'Маруся Чурай',
    authors: [{ id: 'a', fullName: 'Ліна Костенко', bio: null }],
  };

  async function render(coverImageUrl: string | null) {
    const fixture = TestBed.createComponent(BookCover);
    fixture.componentRef.setInput('book', { ...book, coverImageUrl });
    await fixture.whenStable();
    return fixture;
  }

  it('shows the cover image', async () => {
    const fixture = await render('https://images.example/cover.jpg');
    const img = (fixture.nativeElement as HTMLElement).querySelector('img')!;

    expect(img.getAttribute('src')).toBe('https://images.example/cover.jpg');
    expect(img.alt).toContain('Маруся Чурай');
  });

  it('generates a cover with the title and author when there is no image or it fails', async () => {
    const fixture = await render('https://images.example/broken.jpg');
    const element: HTMLElement = fixture.nativeElement;

    element.querySelector('img')!.dispatchEvent(new Event('error'));
    await fixture.whenStable();

    expect(element.querySelector('img')).toBeNull();
    expect(element.textContent).toContain('Маруся Чурай');
    expect(element.textContent).toContain('Ліна Костенко');
  });
});
