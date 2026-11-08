import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
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

  it('validates required fields before making a request', async () => {
    const fixture = TestBed.createComponent(RepositoriesComponent);
    fixture.detectChanges();

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
  });
});
