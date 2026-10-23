import 'fake-indexeddb/auto';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { runInInjectionContext, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { initializeApp } from './app.config';
import { AuthService } from './services/auth.service';

describe('initializeApp', () => {
  it('loads the local context from IndexedDB inside the injection context, before the first screen', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const injector = TestBed.inject(EnvironmentInjector);

    await runInInjectionContext(injector, () => initializeApp());

    expect(TestBed.inject(AuthService).loaded()).toBe(true);
  });
});
