import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PhotoService } from '../services/photo.service';
import { ProductAvatar } from './product-avatar';

@Component({
  imports: [ProductAvatar],
  template: `<app-product-avatar [name]="name()" [photoId]="photo()" />`,
})
class Host {
  readonly name = signal('Eau minérale');
  readonly photo = signal<string | null>(null);
}

describe('ProductAvatar', () => {
  const load = vi.fn<(id: string | null | undefined) => Promise<string | null>>();

  beforeEach(() => {
    load.mockReset();
    load.mockResolvedValue(null);
    TestBed.configureTestingModule({ providers: [{ provide: PhotoService, useValue: { load } }] });
  });

  it('shows the initials when the product has no photo', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent.trim()).toBe('EM');
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
  });

  it('shows the photo once it is loaded', async () => {
    load.mockResolvedValue('blob:abc');
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.photo.set('p1');
    await fixture.whenStable();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('img')?.getAttribute('src')).toBe('blob:abc');
    });
  });

  it('keeps the initials when the photo cannot be loaded', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.photo.set('p-missing');
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.textContent.trim()).toBe('EM');
  });

  it('is hidden from screen readers : the name is always written next to it', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('app-product-avatar').getAttribute('aria-hidden')).toBe('true');
  });
});
