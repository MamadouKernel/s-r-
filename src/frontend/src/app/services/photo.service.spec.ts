import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PhotoService } from './photo.service';

describe('PhotoService', () => {
  let http: HttpTestingController;
  let service: PhotoService;
  const created: string[] = [];

  beforeEach(() => {
    created.length = 0;
    URL.createObjectURL = vi.fn(() => {
      created.push('blob:photo-' + created.length);
      return created[created.length - 1];
    });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(PhotoService);
  });

  afterEach(() => http.verify());

  /** La demande part après la lecture du cache de l'appareil : on l'attend avant d'y répondre. */
  async function answer(url: string, reply: (request: ReturnType<HttpTestingController['expectOne']>) => void): Promise<void> {
    await vi.waitFor(() => reply(http.expectOne(url)));
  }

  it('fetches a photo once and gives the same address to every later request', async () => {
    const first = service.load('p1');
    const second = service.load('p1');
    await answer('/api/v1/photos/p1', (request) => request.flush(new Blob(['x'], { type: 'image/jpeg' })));

    expect(await first).toBe('blob:photo-0');
    expect(await second).toBe('blob:photo-0');
    expect(await service.load('p1')).toBe('blob:photo-0');
    expect(created).toHaveLength(1);
  });

  it('gives nothing, without error, when the photo cannot be fetched (offline or removed)', async () => {
    const result = service.load('p2');
    await answer('/api/v1/photos/p2', (request) => request.error(new ProgressEvent('error')));

    expect(await result).toBeNull();
  });

  it('tries again later after a failure instead of remembering it', async () => {
    const failed = service.load('p3');
    await answer('/api/v1/photos/p3', (request) => request.error(new ProgressEvent('error')));
    await failed;

    const retry = service.load('p3');
    await answer('/api/v1/photos/p3', (request) => request.flush(new Blob(['x'], { type: 'image/png' })));

    expect(await retry).toBe('blob:photo-0');
  });

  it('gives nothing when there is no photo', async () => {
    expect(await service.load(null)).toBeNull();
  });
});
