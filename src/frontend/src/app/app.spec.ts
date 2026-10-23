import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should show the Sôrô brand as the page title', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('h1')?.textContent).toContain('Sôrô');
  });

  it('should offer a skip link that targets the main landmark', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('a[href="#contenu"]')).not.toBeNull();
    expect(page.querySelector('main#contenu')).not.toBeNull();
  });

  it('should announce the network state in a status region', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const status = (fixture.nativeElement as HTMLElement).querySelector('app-network-banner [role="status"]');
    expect(status?.textContent).toMatch(/En ligne|Hors ligne/);
  });
});
