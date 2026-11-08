import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('renders the workspace navigation and route outlet', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.brand-copy strong')?.textContent).toContain('CodeMentor');
    expect(compiled.querySelectorAll('.nav-link').length).toBeGreaterThanOrEqual(5);
    expect(compiled.querySelector('router-outlet')).not.toBeNull();
  });
});
