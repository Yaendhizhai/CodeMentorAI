import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RepositoryApiService } from './repository-api.service';
import { RepositoryProject } from '../models/repository-project.model';

describe('RepositoryApiService', () => {
  let service: RepositoryApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RepositoryApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('registers a repository through the existing API route', () => {
    const request = {
      name: 'Commerce',
      repositoryUrl: 'https://github.com/acme/commerce',
      description: null,
    };
    const expected: RepositoryProject = {
      id: '00e3ec8f-870d-4c7a-b922-9e792fb932ce',
      name: request.name,
      repositoryUrl: request.repositoryUrl,
      description: null,
      createdAtUtc: '2026-01-01T00:00:00Z',
      updatedAtUtc: '2026-01-01T00:00:00Z',
    };
    let response: RepositoryProject | undefined;

    service.register(request).subscribe((project) => (response = project));

    const apiRequest = http.expectOne('/api/repository-projects');
    expect(apiRequest.request.method).toBe('POST');
    expect(apiRequest.request.body).toEqual(request);
    apiRequest.flush(expected, { status: 201, statusText: 'Created' });
    expect(response).toEqual(expected);
  });

  it('loads one repository by ID through the existing API route', () => {
    const id = '00e3ec8f-870d-4c7a-b922-9e792fb932ce';
    const expected: RepositoryProject = {
      id,
      name: 'Commerce',
      repositoryUrl: 'https://github.com/acme/commerce',
      description: null,
      createdAtUtc: '2026-01-01T00:00:00Z',
      updatedAtUtc: '2026-01-01T00:00:00Z',
    };
    let response: RepositoryProject | undefined;

    service.getById(id).subscribe((project) => (response = project));

    const apiRequest = http.expectOne(`/api/repository-projects/${id}`);
    expect(apiRequest.request.method).toBe('GET');
    apiRequest.flush(expected);
    expect(response).toEqual(expected);
  });

  it('loads all repository projects through the configured projects route', () => {
    const expected: RepositoryProject[] = [
      {
        id: '00e3ec8f-870d-4c7a-b922-9e792fb932ce',
        name: 'Commerce',
        repositoryUrl: 'https://github.com/acme/commerce',
        description: 'Commerce services',
        createdAtUtc: '2026-01-01T00:00:00Z',
        updatedAtUtc: '2026-01-01T00:00:00Z',
      },
    ];
    let response: RepositoryProject[] | undefined;

    service.getAll().subscribe((projects) => (response = projects));

    const apiRequest = http.expectOne('/api/repository-projects');
    expect(apiRequest.request.method).toBe('GET');
    apiRequest.flush(expected);
    expect(response).toEqual(expected);
  });
});
