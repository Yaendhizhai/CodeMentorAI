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
    const detailsLink = host.querySelector('.repository-link') as HTMLAnchorElement;
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

  it('ingests a repository, prevents duplicate clicks, shows success, and refreshes the list', () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([createProject({ name: 'Commerce' })]);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const ingestButton = host.querySelector('.ingest-button') as HTMLButtonElement;
    ingestButton.click();
    fixture.detectChanges();
    expect(ingestButton.disabled).toBe(true);
    expect(ingestButton.textContent).toContain('Ingesting');
    ingestButton.click();

    const ingestionRequest = http.expectOne(
      '/api/repository-projects/00e3ec8f-870d-4c7a-b922-9e792fb932ce/ingest',
    );
    expect(ingestionRequest.request.method).toBe('POST');
    expect(ingestionRequest.request.body).toBeNull();
    ingestionRequest.flush({
      analysisSessionId: '10e3ec8f-870d-4c7a-b922-9e792fb932ce',
      repositoryProjectId: '00e3ec8f-870d-4c7a-b922-9e792fb932ce',
      treeSha: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
      fileCount: 4,
      chunkCount: 7,
      completedAtUtc: '2026-01-02T03:04:05Z',
    });
    fixture.detectChanges();

    const refreshRequest = http.expectOne('/api/repository-projects');
    expect(refreshRequest.request.method).toBe('GET');
    refreshRequest.flush([createProject({ name: 'Commerce' })]);
    fixture.detectChanges();
    expect(host.textContent).toContain('Ingested 4 files and 7 chunks.');
    expect(host.textContent).toContain('10e3ec8f-870d-4c7a-b922-9e792fb932ce');
  });

  it('shows clear conflict feedback when a repository was already ingested', () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([createProject()]);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.ingest-button') as HTMLButtonElement).click();
    http.expectOne('/api/repository-projects/00e3ec8f-870d-4c7a-b922-9e792fb932ce/ingest').flush(
      { title: 'Repository already ingested', detail: 'This repository project has already been ingested.' },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('This repository has already been ingested.');
  });

  it('shows clear size-limit feedback when ingestion returns 413', () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([createProject()]);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.ingest-button') as HTMLButtonElement).click();
    http.expectOne('/api/repository-projects/00e3ec8f-870d-4c7a-b922-9e792fb932ce/ingest').flush(
      { title: 'Repository exceeds ingestion limits', detail: 'The repository exceeds ingestion limits.' },
      { status: 413, statusText: 'Payload Too Large' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'This repository exceeds the ingestion size or file limits.',
    );
  });

  it('shows API problem details for general ingestion failures', () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([createProject()]);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.ingest-button') as HTMLButtonElement).click();
    http.expectOne('/api/repository-projects/00e3ec8f-870d-4c7a-b922-9e792fb932ce/ingest').flush(
      { title: 'GitHub ingestion failed', detail: 'The repository could not be retrieved from GitHub.' },
      { status: 502, statusText: 'Bad Gateway' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('The repository could not be retrieved from GitHub.');
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
