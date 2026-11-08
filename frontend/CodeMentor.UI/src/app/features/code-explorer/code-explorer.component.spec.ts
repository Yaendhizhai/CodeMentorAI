import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CodeChunkSearchResult } from '../../core/models/code-search.model';
import { RepositoryProject } from '../../core/models/repository-project.model';
import { CodeExplorerComponent } from './code-explorer.component';

describe('CodeExplorerComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CodeExplorerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads repositories and requires an explicit selection before searching', () => {
    const fixture = TestBed.createComponent(CodeExplorerComponent);
    fixture.detectChanges();

    const repositoryRequest = http.expectOne('/api/repository-projects');
    expect(repositoryRequest.request.method).toBe('GET');
    repositoryRequest.flush([createProject({ id: firstProjectId, name: 'Commerce' })]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Commerce');
    submitSearch(fixture);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Select a repository before searching.');
    http.expectNone((request) => request.url.includes('/code-search'));
  });

  it('searches only the selected project and renders returned evidence', () => {
    const fixture = TestBed.createComponent(CodeExplorerComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([
      createProject({ id: firstProjectId, name: 'Commerce' }),
      createProject({ id: secondProjectId, name: 'Catalog' }),
    ]);
    fixture.detectChanges();

    selectRepository(fixture, secondProjectId);
    enterQuery(fixture, 'class Widget');
    selectResultLimit(fixture, '50 results');
    submitSearch(fixture);

    const searchRequest = http.expectOne(
      `/api/repository-projects/${secondProjectId}/code-search`,
    );
    expect(searchRequest.request.method).toBe('POST');
    expect(searchRequest.request.body).toEqual({ query: 'class Widget', limit: 50 });
    searchRequest.flush([createResult()]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('src/Widget.cs');
    expect(text).toContain('Chunk 2 · Lines 11–25');
    expect(text).toContain('Match score 2');
    expect(text).toContain('public class Widget {}');
    http.expectNone(`/api/repository-projects/${firstProjectId}/code-search`);
  });

  it('rejects empty and punctuation-only queries without sending a request', () => {
    const fixture = TestBed.createComponent(CodeExplorerComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([createProject({ id: firstProjectId })]);
    fixture.detectChanges();
    selectRepository(fixture, firstProjectId);

    enterQuery(fixture, '   ');
    submitSearch(fixture);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Enter a search query.');

    enterQuery(fixture, '!?.,');
    submitSearch(fixture);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      'Enter a query containing at least one letter or number.',
    );
    http.expectNone((request) => request.url.includes('/code-search'));
  });

  it('rejects query lengths over 200 and more than 10 distinct terms', () => {
    const fixture = TestBed.createComponent(CodeExplorerComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([createProject({ id: firstProjectId })]);
    fixture.detectChanges();
    selectRepository(fixture, firstProjectId);

    enterQuery(fixture, 'x'.repeat(201));
    submitSearch(fixture);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      'Search queries must be 200 characters or fewer.',
    );

    enterQuery(fixture, Array.from({ length: 11 }, (_, index) => `term${index}`).join(' '));
    submitSearch(fixture);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Use no more than 10 distinct search terms.');
    http.expectNone((request) => request.url.includes('/code-search'));
  });

  it('discards pending and displayed results when repository selection changes', () => {
    const fixture = TestBed.createComponent(CodeExplorerComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([
      createProject({ id: firstProjectId, name: 'Commerce' }),
      createProject({ id: secondProjectId, name: 'Catalog' }),
    ]);
    fixture.detectChanges();

    selectRepository(fixture, firstProjectId);
    enterQuery(fixture, 'Widget');
    submitSearch(fixture);
    const firstSearch = http.expectOne(`/api/repository-projects/${firstProjectId}/code-search`);

    selectRepository(fixture, secondProjectId);
    firstSearch.flush([createResult()]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('src/Widget.cs');

    enterQuery(fixture, 'Catalog');
    submitSearch(fixture);
    const secondSearch = http.expectOne(`/api/repository-projects/${secondProjectId}/code-search`);
    secondSearch.flush([createResult({ relativePath: 'src/Catalog.cs' })]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('src/Catalog.cs');
    expect(fixture.nativeElement.textContent).not.toContain('src/Widget.cs');
  });

  it('shows an empty state when search returns no matches', () => {
    const fixture = TestBed.createComponent(CodeExplorerComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([createProject({ id: firstProjectId })]);
    fixture.detectChanges();
    selectRepository(fixture, firstProjectId);
    enterQuery(fixture, 'missing-symbol');
    submitSearch(fixture);
    http.expectOne(`/api/repository-projects/${firstProjectId}/code-search`).flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No matching code chunks');
    expect(fixture.nativeElement.textContent).toContain('Try different terms');
  });

  it('shows API errors and retries the same project-scoped search', () => {
    const fixture = TestBed.createComponent(CodeExplorerComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush([createProject({ id: firstProjectId })]);
    fixture.detectChanges();
    selectRepository(fixture, firstProjectId);
    enterQuery(fixture, 'Widget');
    submitSearch(fixture);
    http.expectOne(`/api/repository-projects/${firstProjectId}/code-search`).flush(
      { title: 'Search unavailable', detail: 'The code search service is unavailable.' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('The code search service is unavailable.');
    const retryButton = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.includes('Retry search'));
    if (!retryButton) {
      throw new Error('The search retry button was not rendered.');
    }

    retryButton.click();
    const retryRequest = http.expectOne(`/api/repository-projects/${firstProjectId}/code-search`);
    expect(retryRequest.request.body).toEqual({ query: 'Widget', limit: 10 });
    retryRequest.flush([createResult()]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('src/Widget.cs');
  });

  it('allows repository-list retry after an API error', () => {
    const fixture = TestBed.createComponent(CodeExplorerComponent);
    fixture.detectChanges();
    http.expectOne('/api/repository-projects').flush(
      { title: 'Unavailable', detail: 'Repository list unavailable.' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Repository list unavailable.');
    const retryButton = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.includes('Try again'));
    if (!retryButton) {
      throw new Error('The repository retry button was not rendered.');
    }

    retryButton.click();
    http.expectOne('/api/repository-projects').flush([createProject()]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Default repository');
  });
});

const firstProjectId = '00e3ec8f-870d-4c7a-b922-9e792fb932ce';
const secondProjectId = '10e3ec8f-870d-4c7a-b922-9e792fb932ce';

function createProject(overrides: Partial<RepositoryProject> = {}): RepositoryProject {
  return {
    id: '20e3ec8f-870d-4c7a-b922-9e792fb932ce',
    name: 'Default repository',
    repositoryUrl: 'https://github.com/acme/default',
    description: null,
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function createResult(
  overrides: Partial<CodeChunkSearchResult> = {},
): CodeChunkSearchResult {
  return {
    relativePath: 'src/Widget.cs',
    chunkIndex: 2,
    startLine: 11,
    endLine: 25,
    content: 'public class Widget {}',
    matchScore: 2,
    ...overrides,
  };
}

function selectRepository(fixture: ReturnType<typeof TestBed.createComponent<CodeExplorerComponent>>, id: string): void {
  const select = fixture.nativeElement.querySelector('#repository-select') as HTMLSelectElement;
  select.value = id;
  select.dispatchEvent(new Event('change'));
  fixture.detectChanges();
}

function enterQuery(fixture: ReturnType<typeof TestBed.createComponent<CodeExplorerComponent>>, query: string): void {
  const input = fixture.nativeElement.querySelector('#code-query') as HTMLInputElement;
  input.value = query;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function selectResultLimit(
  fixture: ReturnType<typeof TestBed.createComponent<CodeExplorerComponent>>,
  label: string,
): void {
  const select = fixture.nativeElement.querySelector('#result-limit') as HTMLSelectElement;
  const option = Array.from(select.options).find((candidate) => candidate.textContent?.trim() === label);
  if (!option) {
    throw new Error(`Result limit option "${label}" was not rendered.`);
  }
  select.value = option.value;
  select.dispatchEvent(new Event('change'));
  fixture.detectChanges();
}

function submitSearch(fixture: ReturnType<typeof TestBed.createComponent<CodeExplorerComponent>>): void {
  (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
    new Event('submit', { bubbles: true, cancelable: true }),
  );
  fixture.detectChanges();
}
