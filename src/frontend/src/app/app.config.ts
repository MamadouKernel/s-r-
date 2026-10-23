import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, isDevMode, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { routes } from './app.routes';
import { AuthService } from './services/auth.service';
import { authInterceptor } from './services/auth.interceptor';
import { CashService } from './services/cash.service';
import { CatalogueService } from './services/catalogue.service';
import { SyncService } from './services/sync.service';

/**
 * `inject()` n'est permis qu'avant le premier `await` : les services sont donc tous récupérés d'abord.
 * Exportée pour être jouée telle quelle dans les tests.
 */
export async function initializeApp(): Promise<void> {
  const auth = inject(AuthService);
  const cash = inject(CashService);
  const catalogue = inject(CatalogueService);
  const sync = inject(SyncService);
  await auth.load();
  await cash.load();
  await catalogue.loadLocal();
  sync.start();
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    // Le contexte local (appareil, jeton, session, catalogue) est lu dans IndexedDB avant le premier écran.
    provideAppInitializer(() => initializeApp()),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
