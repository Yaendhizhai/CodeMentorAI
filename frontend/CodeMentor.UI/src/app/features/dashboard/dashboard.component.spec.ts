import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { DashboardComponent } from './dashboard.component';

describe('DashboardComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the repository total from the repository listing API', () => {
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();

    const request = http.expectOne('/api/repository-projects');
    expect(request.request.method).toBe('GET');
    request.flush([
      { id: '10000000-0000-0000-0000-000000000001' },
      { id: '20000000-0000-0000-0000-000000000002' },
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('2');
    expect(fixture.nativeElement.textContent).toContain('Registered in this workspace');
  });
});
