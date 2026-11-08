import { Component, signal } from '@angular/core';
import { PageHeadingComponent } from '../../shared/components/page-heading.component';
import { environment } from '../../../environments/environment';

@Component({
  imports: [PageHeadingComponent],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss',
})
export class SettingsComponent {
  protected readonly apiBaseUrl = environment.apiBaseUrl;
  protected readonly darkTheme = signal(this.readThemePreference());

  protected toggleTheme(): void {
    const enabled = !this.darkTheme();
    this.darkTheme.set(enabled);
    localStorage.setItem('codementor-theme', enabled ? 'dark' : 'light');
    document.documentElement.dataset['theme'] = enabled ? 'dark' : 'light';
  }

  private readThemePreference(): boolean {
    return typeof localStorage !== 'undefined' && localStorage.getItem('codementor-theme') === 'dark';
  }
}
