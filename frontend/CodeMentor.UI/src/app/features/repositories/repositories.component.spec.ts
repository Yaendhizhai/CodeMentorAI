import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { RepositoryProject } from '../../core/models/repository-project.model';
import { RepositoriesComponent } from './repositories.component';

describe('RepositoriesComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositoriesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows a loading state while the repository list request is pending', () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Loading repositories…');
    http.expectOne('/api/repository-projects').flush([]);
  });

  it('validates required fields before making a request', async () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([]);

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Enter a display name.');
    expect(fixture.nativeElement.textContent).toContain('Enter a GitHub repository URL.');
    http.expectNone('/api/repository-projects');
  });

  it('shows registration details returned by the API', async () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([]);
    const name = fixture.nativeElement.querySelector('#repository-name') as HTMLInputElement;
    const url = fixture.nativeElement.querySelector('#repository-url') as HTMLInputElement;
    name.value = 'Commerce';
    name.dispatchEvent(new Event('input'));
    url.value = 'https://github.com/acme/commerce';
    url.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const request = http.expectOne('/api/repository-projects');
    request.flush({
      id: '00e3ec8f-870d-4c7a-b922-9e792fb932ce',
      name: 'Commerce',
      repositoryUrl: 'https://github.com/acme/commerce',
      description: null,
      createdAtUtc: '2026-01-01T00:00:00Z',
      updatedAtUtc: '2026-01-01T00:00:00Z',
    }, { status: 201, statusText: 'Created' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Repository registered');
    expect(fixture.nativeElement.textContent).toContain('00e3ec8f-870d-4c7a-b922-9e792fb932ce');

    const listRequest = http.expectOne('/api/repository-projects');
    expect(listRequest.request.method).toBe('GET');
    listRequest.flush([
      {
        id: '00e3ec8f-870d-4c7a-b922-9e792fb932ce',
        name: 'Commerce',
        repositoryUrl: 'https://github.com/acme/commerce',
        description: null,
        createdAtUtc: '2026-01-01T00:00:00Z',
        updatedAtUtc: '2026-01-01T00:00:00Z',
      },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Registered repositories');
    expect(fixture.nativeElement.textContent).toContain('Commerce');
  });

  it('shows repository fields and links each result to its details page', () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([
      createProject({
        name: 'Commerce',
        repositoryUrl: 'https://github.com/acme/commerce',
        description: 'Commerce services',
      }),
    ]);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Commerce');
    expect(host.textContent).toContain('https://github.com/acme/commerce');
    expect(host.textContent).toContain('Commerce services');
    expect(host.textContent).toContain('Jan 1, 2026');
    const detailsLink = host.querySelector('.repository-item') as HTMLAnchorElement;
    expect(detailsLink.getAttribute('href')).toBe('/repositories/00e3ec8f-870d-4c7a-b922-9e792fb932ce');
  });

  it('shows an empty state when the API returns no repositories', () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No repositories registered yet');
  });

  it('shows an error and lets the user refresh the repository list', () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush(
      { title: 'Unavailable', detail: 'The service is temporarily unavailable.' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('The service is temporarily unavailable.');
    const host = fixture.nativeElement as HTMLElement;
    const retryButton = Array.from(host.querySelectorAll('button'))
      .find((button) => button.textContent?.includes('Try again'));
    if (!retryButton) {
      throw new Error('The retry button was not rendered.');
    }
    retryButton.click();
    fixture.detectChanges();

    const refreshRequest = http.expectOne('/api/repository-projects');
    expect(refreshRequest.request.method).toBe('GET');
    refreshRequest.flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No repositories registered yet');
  });
});

function createProject(overrides: Partial<RepositoryProject> = {}): RepositoryProject {
  return {
    id: '00e3ec8f-870d-4c7a-b922-9e792fb932ce',
    name: 'Default repository',
    repositoryUrl: null,
    description: null,
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}
